using System.Windows;
using System.Windows.Controls;
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
        StreamText.Text = device.UseSubStream ? "Substream" : "Main stream";

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
            IdleText.Text = "Não foi possível abrir o stream";
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

        StartPlayback();
    }

    private void StartPlayback()
    {
        var password = _repository.GetPassword(_device);
        var uri = _device.BuildRtspUri(password);

        if (uri is null)
        {
            IdleText.Text = "Endereço RTSP inválido";
            IdleText.Visibility = Visibility.Visible;
            return;
        }

        _media?.Dispose();
        _media = new Media(VlcRuntime.Instance, uri);

        _media.AddOption(":rtsp-tcp");
        _media.AddOption(":network-caching=350");
        _media.AddOption(":clock-jitter=0");
        _media.AddOption(":clock-synchro=0");

        if (_recording)
        {
            _recordingPath ??= Path.Combine(
                AppPaths.Recordings,
                string.Concat(SafeFileName(_device.Name), "-", DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".ts"));

            var normalized = _recordingPath.Replace("\", "/");
            _media.AddOption(string.Concat(
                ":sout=#duplicate{dst=display,dst=std{access=file,mux=ts,dst=\"", normalized, "\"}}"));
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
    }
}
