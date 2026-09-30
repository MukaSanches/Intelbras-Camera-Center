using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Intelbras.CameraCenter.App.Dialogs;
using Intelbras.CameraCenter.App.Models;
using Intelbras.CameraCenter.App.Services;
using LibVLCSharp.Shared;

namespace Intelbras.CameraCenter.App.Controls;

public partial class CameraTile : UserControl, IDisposable
{
    private readonly CameraRepository _repository;
    private readonly CameraDevice _device;
    private readonly MediaPlayer _player;
    private Media? _media;
    private OnvifClient? _onvif;
    private CancellationTokenSource? _playbackCts;
    private bool _isPlaying;
    private bool _resolving;
    private bool _recording;
    private bool _audioMuted;
    private string? _recordingPath;
    private bool _disposed;

    public CameraTile(CameraRepository repository, CameraDevice device)
    {
        InitializeComponent();

        _repository = repository;
        _device = device;

        CameraNameText.Text = device.Name;
        CameraAddressText.Text = device.DisplayAddress;
        StreamText.Text = string.Concat(device.StreamTechnology, " • universal resolver");

        _player = new MediaPlayer(VlcRuntime.Instance)
        {
            Volume = 100,
            Mute = false
        };

        _player.Playing += (_, _) => Dispatcher.Invoke(() =>
        {
            _isPlaying = true;
            StatusText.Text = _recording ? "GRAVANDO" : "AO VIVO";
            IdleText.Visibility = Visibility.Collapsed;
            PlayButton.Content = "■ Parar";
        });

        _player.Stopped += (_, _) => Dispatcher.Invoke(() =>
        {
            if (!_resolving)
                SetStoppedUi();
        });

        _player.EncounteredError += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_resolving)
                return;

            StatusText.Text = "ERRO";
            IdleText.Text = _repository.HasCredential(_device)
                ? "O stream caiu. Clique em Reproduzir para renegociar automaticamente."
                : "Stream protegido • configure o acesso uma única vez";
            IdleText.Visibility = Visibility.Visible;
        });

        VideoSurface.MediaPlayer = _player;
    }

    private async void PlayClick(object sender, RoutedEventArgs e)
    {
        if (_isPlaying || _resolving)
        {
            _playbackCts?.Cancel();
            _resolving = false;
            _player.Stop();
            SetStoppedUi();
            return;
        }

        await StartPlaybackAsync();
    }

    private async Task StartPlaybackAsync()
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();
        var cancellationToken = _playbackCts.Token;

        _resolving = true;
        _isPlaying = false;
        PlayButton.Content = "■ Cancelar";
        StatusText.Text = "RESOLVENDO";
        IdleText.Text = "Detectando o stream correto...";
        IdleText.Visibility = Visibility.Visible;

        try
        {
            var password = _repository.GetPassword(_device);
            var resolver = new UniversalStreamResolverService();
            var resolution = await resolver.ResolveAsync(_device, password, cancellationToken);

            if (resolution.CredentialsLikelyRequired && !_repository.HasCredential(_device))
            {
                _resolving = false;
                var dialog = new CredentialWizardWindow(_repository, _device)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() != true)
                {
                    SetStoppedUi();
                    return;
                }

                password = _repository.GetPassword(_device);
                _resolving = true;
                resolution = await resolver.ResolveAsync(_device, password, cancellationToken);
            }

            if (resolution.Candidates.Count == 0)
            {
                StatusText.Text = "SEM STREAM";
                IdleText.Text = "Nenhum stream compatível foi encontrado. Em Dispositivos, use Editar para informar uma URL RTSP/HTTP manual.";
                IdleText.Visibility = Visibility.Visible;
                return;
            }

            var index = 0;

            foreach (var candidate in resolution.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;

                StatusText.Text = "TESTANDO";
                IdleText.Text = string.Concat(
                    "Tentativa ", index, "/", resolution.Candidates.Count,
                    "\n", candidate.Label);
                IdleText.Visibility = Visibility.Visible;

                if (await TryPlayCandidateAsync(candidate, cancellationToken))
                {
                    _resolving = false;
                    _isPlaying = true;
                    StatusText.Text = _recording ? "GRAVANDO" : "AO VIVO";
                    StreamText.Text = string.Concat(candidate.Label, " • vídeo + áudio auto");
                    PlayButton.Content = "■ Parar";
                    return;
                }
            }

            StatusText.Text = "NÃO ABRIU";
            IdleText.Text = "O dispositivo respondeu na rede, mas nenhum perfil de mídia conhecido abriu. Tente “Acesso único” ou informe a URL de stream do fabricante.";
            IdleText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = "ERRO";
            IdleText.Text = string.Concat("Falha ao negociar o stream: ", ex.Message);
            IdleText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!_isPlaying)
            {
                _resolving = false;
                PlayButton.Content = "▶ Reproduzir";
            }
        }
    }

    private async Task<bool> TryPlayCandidateAsync(
        StreamCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            _player.Stop();
            _media?.Dispose();
            _media = new Media(VlcRuntime.Instance, candidate.Uri);

            if (candidate.ForceTcp &&
                (candidate.Uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
                 candidate.Uri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)))
            {
                _media.AddOption(":rtsp-tcp");
            }

            _media.AddOption(string.Concat(":network-caching=", candidate.NetworkCaching));
            _media.AddOption(":clock-jitter=0");
            _media.AddOption(":clock-synchro=0");
            _media.AddOption(":audio-time-stretch");
            _media.AddOption(":no-video-title-show");

            if (_recording)
                AddRecordingOptions(_media);

            _player.Mute = _audioMuted;
            _player.Volume = 100;

            if (!_player.Play(_media))
                return false;

            for (var i = 0; i < 24; i++)
            {
                await Task.Delay(250, cancellationToken);

                if (_player.IsPlaying)
                    return true;
            }

            _player.Stop();
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            try { _player.Stop(); } catch { }
            return false;
        }
    }

    private void AddRecordingOptions(Media media)
    {
        _recordingPath ??= Path.Combine(
            AppPaths.Recordings,
            string.Concat(SafeFileName(_device.Name), "-", DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".ts"));

        var normalized = _recordingPath.Replace(Path.DirectorySeparatorChar, '/');
        var quote = '"';
        media.AddOption(string.Concat(
            ":sout=#duplicate{dst=display,dst=std{access=file,mux=ts,dst=", quote, normalized, quote, "}}"));
        media.AddOption(":sout-keep");
    }

    private void AudioClick(object sender, RoutedEventArgs e)
    {
        _audioMuted = !_audioMuted;
        _player.Mute = _audioMuted;
        AudioButton.Content = _audioMuted ? "🔇 Mudo" : "🔊 Áudio";
    }

    private void SnapshotClick(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        var path = Path.Combine(
            AppPaths.Snapshots,
            string.Concat(SafeFileName(_device.Name), "-", DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".png"));

        var ok = _player.TakeSnapshot(0, path, 0, 0);
        StatusText.Text = ok ? "SNAPSHOT" : "SEM VÍDEO";
    }

    private async void RecordClick(object sender, RoutedEventArgs e)
    {
        _recording = !_recording;

        if (_recording)
        {
            _recordingPath = Path.Combine(
                AppPaths.Recordings,
                string.Concat(SafeFileName(_device.Name), "-", DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".ts"));
            RecordButton.Content = "■ Parar gravação";
        }
        else
        {
            RecordButton.Content = "● Gravar";
            _recordingPath = null;
        }

        if (_isPlaying)
        {
            _player.Stop();
            _isPlaying = false;
            await StartPlaybackAsync();
        }
    }

    private OnvifClient GetOnvif()
        => _onvif ??= new OnvifClient(_device, _repository.GetPassword(_device));

    private async void PtzStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string direction)
            return;

        var (pan, tilt, zoom) = direction switch
        {
            "left" => (-0.55, 0.0, 0.0),
            "right" => (0.55, 0.0, 0.0),
            "up" => (0.0, 0.55, 0.0),
            "down" => (0.0, -0.55, 0.0),
            "zoomin" => (0.0, 0.0, 0.55),
            "zoomout" => (0.0, 0.0, -0.55),
            _ => (0.0, 0.0, 0.0)
        };

        try
        {
            StatusText.Text = "PTZ";
            await GetOnvif().ContinuousMoveAsync(pan, tilt, zoom);
        }
        catch
        {
            StatusText.Text = "PTZ N/D";
        }
    }

    private async void PtzStop(object sender, MouseButtonEventArgs e)
        => await StopPtzAsync();

    private async void PtzStopClick(object sender, RoutedEventArgs e)
        => await StopPtzAsync();

    private async Task StopPtzAsync()
    {
        try
        {
            if (_onvif is not null)
                await _onvif.StopAsync();
            StatusText.Text = _isPlaying ? "AO VIVO" : "PRONTO";
        }
        catch
        {
            StatusText.Text = "PTZ N/D";
        }
    }

    private void SetStoppedUi()
    {
        _isPlaying = false;
        StatusText.Text = "PRONTO";
        IdleText.Text = "Clique em Reproduzir";
        IdleText.Visibility = Visibility.Visible;
        PlayButton.Content = "▶ Reproduzir";
    }

    private static string SafeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "camera" : name;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        try { _player.Stop(); } catch { }
        VideoSurface.MediaPlayer = null;
        _media?.Dispose();
        _player.Dispose();
        _onvif?.Dispose();
    }
}
