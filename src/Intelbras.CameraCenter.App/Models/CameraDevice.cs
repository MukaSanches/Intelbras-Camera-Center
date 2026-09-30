using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Intelbras.CameraCenter.App.Models;

public enum DeviceKind
{
    Camera,
    DVR,
    NVR,
    Mibo,
    Other
}

public sealed class CameraDevice : INotifyPropertyChanged
{
    private string _name = "Nova câmera";
    private string _host = "";
    private int _rtspPort = 554;
    private int _httpPort = 80;
    private int _channel = 1;
    private bool _useSubStream;
    private string _username = "admin";
    private string _passwordEncrypted = "";
    private DeviceKind _kind = DeviceKind.Camera;
    private string _customRtspUrl = "";
    private bool _enableEvents = true;
    private string _manufacturer = "Desconhecido";
    private string _discoverySource = "Manual";
    private int _channelCountHint = 1;
    private bool _authenticationRequired;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Host { get => _host; set => Set(ref _host, value); }
    public int RtspPort { get => _rtspPort; set => Set(ref _rtspPort, value); }
    public int HttpPort { get => _httpPort; set => Set(ref _httpPort, value); }
    public int Channel { get => _channel; set => Set(ref _channel, value); }
    public bool UseSubStream { get => _useSubStream; set => Set(ref _useSubStream, value); }
    public string Username { get => _username; set => Set(ref _username, value); }
    public string PasswordEncrypted
    {
        get => _passwordEncrypted;
        set
        {
            if (_passwordEncrypted == value)
                return;
            _passwordEncrypted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PasswordEncrypted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessStatus)));
        }
    }
    public DeviceKind Kind { get => _kind; set => Set(ref _kind, value); }
    public string CustomRtspUrl { get => _customRtspUrl; set => Set(ref _customRtspUrl, value); }
    public bool EnableEvents { get => _enableEvents; set => Set(ref _enableEvents, value); }
    public string Manufacturer { get => _manufacturer; set => Set(ref _manufacturer, value); }
    public string DiscoverySource { get => _discoverySource; set => Set(ref _discoverySource, value); }
    public int ChannelCountHint { get => _channelCountHint; set => Set(ref _channelCountHint, Math.Max(1, value)); }
    public bool AuthenticationRequired { get => _authenticationRequired; set { Set(ref _authenticationRequired, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessStatus))); } }

    [JsonIgnore]
    public string AccessStatus => !string.IsNullOrWhiteSpace(PasswordEncrypted)
        ? "Credencial salva"
        : AuthenticationRequired
            ? "Protegido • acesso único"
            : "Descoberto";

    [JsonIgnore]
    public string DisplayAddress => string.IsNullOrWhiteSpace(Host) ? "não configurado" : Host;

    [JsonIgnore]
    public string StreamTechnology
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CustomRtspUrl))
                return UseSubStream ? "RTSP • Substream" : "RTSP • Main stream";

            return Uri.TryCreate(CustomRtspUrl, UriKind.Absolute, out var uri)
                ? uri.Scheme.ToUpperInvariant()
                : "STREAM";
        }
    }

    public Uri? BuildStreamUri(string password)
    {
        if (!string.IsNullOrWhiteSpace(CustomRtspUrl))
            return Uri.TryCreate(CustomRtspUrl, UriKind.Absolute, out var custom) ? custom : null;

        if (string.IsNullOrWhiteSpace(Host))
            return null;

        var user = Uri.EscapeDataString(Username ?? "");
        var pass = Uri.EscapeDataString(password ?? "");
        var subtype = UseSubStream ? 1 : 0;
        var authority = string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)
            ? Host
            : string.Concat(user, ":", pass, "@", Host);

        var url = string.Concat("rtsp://", authority, ":", RtspPort,
            "/cam/realmonitor?channel=", Math.Max(1, Channel), "&subtype=", subtype);

        return Uri.TryCreate(url, UriKind.Absolute, out var result) ? result : null;
    }

    public Uri? BuildRtspUri(string password) => BuildStreamUri(password);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
