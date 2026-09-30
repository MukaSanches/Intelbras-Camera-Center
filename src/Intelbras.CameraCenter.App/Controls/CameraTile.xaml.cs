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
    private bool _isPlaying;
    private bool _recording;
    private string? _recordingPath;
    private bool _disposed;

    public CameraTile(CameraRepository repository, CameraDevice device)
    {
        InitializeComponent();

        _repository = repository;
        _device = device;

        CameraNameText.Text = device.Name;
        CameraAddressText.Text = device.DisplayAddress;
        StreamText.Text = device.StreamTechnology;

        _player = new MediaPlayer(VlcRuntime.Instance);
        _player.Playing += (_, _) => Dispatcher.Invoke(() =>
        {
            _isPlaying = true;
            StatusText.Text = _recording ? "GRAVANDO" : "AO VIVO";
            IdleText.Visibility = Visibility.Collapsed;
            PlayButton.Content = "■ Parar";
        });
        _player.Stopped += (_, _) => Dispatcher.Invoke(SetStoppedUi);
        _player.EncounteredError += (_, _) => Dispatcher.Invoke(() =>
        {
            StatusText.Text = "ERRO";
            IdleText.Text = _repository.HasCredential(_device)
                ? "Não foi possível abrir o stream"
                : "Stream protegido • configure o acesso uma única vez";
            IdleText.Visibility = Visibility.Visible;
        });

        VideoSurface.MediaPlayer = _player;
    }

    private void PlayClick(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            _player.Stop();
            return;
        }

        if (_device.AuthenticationRequired && !_repository.HasCredential(_device))
        {
            var dialog = new CredentialWizardWindow(_repository, _device)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true)
                return;
        }

        StartPlayback();
    }

    private void StartPlayback()
    {
        var password = _repository.GetPassword(_device);
        var uri = _device.BuildStreamUri(password);

        if (uri is null)
        {
            IdleText.Text = "Endereço de stream inválido";
            IdleText.Visibility = Visibility.Visible;
            return;
        }

        _media?.Dispose();
        _media = new Media(VlcRuntime.Instance, uri);

        if (uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase))
        {
            _media.AddOption(":rtsp-tcp");
        }

        _media.AddOption(":network-caching=350");
        _media.AddOption(":clock-jitter=0");
        _media.AddOption(":clock-synchro=0");

        if (_recording)
        {
            _recordingPath ??= Path.Combine(
                AppPaths.Recordings,
                string.Concat(SafeFileName(_device.Name), "-", DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".ts"));

            var normalized = _recordingPath.Replace(Path.DirectorySeparatorChar, '/');
            var quote = '"';
            _media.AddOption(string.Concat(
                ":sout=#duplicate{dst=display,dst=std{access=file,mux=ts,dst=", quote, normalized, quote, "}}"));
            _media.AddOption(":sout-keep");
        }

        StatusText.Text = "CONECTANDO";
        IdleText.Text = "Conectando...";
        IdleText.Visibility = Visibility.Visible;
        _player.Play(_media);
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

    private void RecordClick(object sender, RoutedEventArgs e)
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
            StartPlayback();
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
        try { _player.Stop(); } catch { }
        VideoSurface.MediaPlayer = null;
        _media?.Dispose();
        _player.Dispose();
        _onvif?.Dispose();
    }
}
