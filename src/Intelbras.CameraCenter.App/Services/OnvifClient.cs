using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed record OnvifCapabilities(string? MediaXAddr, string? PtzXAddr, string? EventsXAddr);

public sealed class OnvifClient : IDisposable
{
    private readonly CameraDevice _device;
    private readonly string _password;
    private readonly HttpClient _http;
    private OnvifCapabilities? _capabilities;
    private string? _profileToken;

    public OnvifClient(CameraDevice device, string password)
    {
        _device = device;
        _password = password;

        _http = new HttpClient(new HttpClientHandler
        {
            Credentials = new NetworkCredential(device.Username, password),
            PreAuthenticate = false,
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(7)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Intelbras-Camera-Center/1.1");
    }

    public async Task<OnvifCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_capabilities is not null)
            return _capabilities;

        var deviceService = BuildDeviceServiceUri();

        const string body = """
<tds:GetCapabilities xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
  <tds:Category>All</tds:Category>
</tds:GetCapabilities>
""";

        var xml = await SendSoapAsync(
            deviceService,
            "http://www.onvif.org/ver10/device/wsdl/GetCapabilities",
            body,
            cancellationToken);

        var document = XDocument.Parse(xml);

        string? GetXAddr(string localName) =>
            document.Descendants()
                .FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                ?.Attributes()
                .FirstOrDefault(x => x.Name.LocalName.Equals("XAddr", StringComparison.OrdinalIgnoreCase))
                ?.Value;

        _capabilities = new OnvifCapabilities(
            GetXAddr("Media"),
            GetXAddr("PTZ"),
            GetXAddr("Events"));

        return _capabilities;
    }

    public async Task<string> GetProfileTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_profileToken))
            return _profileToken;

        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(capabilities.MediaXAddr))
            throw new InvalidOperationException("O equipamento não anunciou o serviço ONVIF Media.");

        const string body = """
<trt:GetProfiles xmlns:trt="http://www.onvif.org/ver10/media/wsdl" />
""";

        var xml = await SendSoapAsync(
            new Uri(capabilities.MediaXAddr),
            "http://www.onvif.org/ver10/media/wsdl/GetProfiles",
            body,
            cancellationToken);

        var document = XDocument.Parse(xml);
        _profileToken = document.Descendants()
            .Where(x => x.Name.LocalName.Equals("Profiles", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("token", StringComparison.OrdinalIgnoreCase))?.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        if (string.IsNullOrWhiteSpace(_profileToken))
            throw new InvalidOperationException("Nenhum perfil ONVIF Media foi retornado.");

        return _profileToken;
    }

    public async Task ContinuousMoveAsync(double pan, double tilt, double zoom, CancellationToken cancellationToken = default)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(capabilities.PtzXAddr))
            throw new InvalidOperationException("PTZ ONVIF não foi anunciado por este dispositivo.");

        var token = SecurityElement.Escape(await GetProfileTokenAsync(cancellationToken)) ?? "";

        var body = $"""
<tptz:ContinuousMove xmlns:tptz="http://www.onvif.org/ver20/ptz/wsdl"
                     xmlns:tt="http://www.onvif.org/ver10/schema">
  <tptz:ProfileToken>{token}</tptz:ProfileToken>
  <tptz:Velocity>
    <tt:PanTilt x="{pan.ToString(System.Globalization.CultureInfo.InvariantCulture)}" y="{tilt.ToString(System.Globalization.CultureInfo.InvariantCulture)}" />
    <tt:Zoom x="{zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}" />
  </tptz:Velocity>
</tptz:ContinuousMove>
""";

        await SendSoapAsync(
            new Uri(capabilities.PtzXAddr),
            "http://www.onvif.org/ver20/ptz/wsdl/ContinuousMove",
            body,
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(capabilities.PtzXAddr))
            return;

        var token = SecurityElement.Escape(await GetProfileTokenAsync(cancellationToken)) ?? "";

        var body = $"""
<tptz:Stop xmlns:tptz="http://www.onvif.org/ver20/ptz/wsdl">
  <tptz:ProfileToken>{token}</tptz:ProfileToken>
  <tptz:PanTilt>true</tptz:PanTilt>
  <tptz:Zoom>true</tptz:Zoom>
</tptz:Stop>
""";

        await SendSoapAsync(
            new Uri(capabilities.PtzXAddr),
            "http://www.onvif.org/ver20/ptz/wsdl/Stop",
            body,
            cancellationToken);
    }

    public async Task<string> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        var profile = await GetProfileTokenAsync(cancellationToken);

        return string.Concat(
            "ONVIF conectado • Perfil: ", profile,
            " • Media: ", string.IsNullOrWhiteSpace(capabilities.MediaXAddr) ? "não" : "sim",
            " • PTZ: ", string.IsNullOrWhiteSpace(capabilities.PtzXAddr) ? "não" : "sim",
            " • Events: ", string.IsNullOrWhiteSpace(capabilities.EventsXAddr) ? "não" : "sim");
    }

    private Uri BuildDeviceServiceUri()
    {
        var scheme = _device.HttpPort == 443 ? "https" : "http";
        return new Uri(string.Concat(scheme, "://", _device.Host, ":", _device.HttpPort, "/onvif/device_service"));
    }

    private async Task<string> SendSoapAsync(
        Uri endpoint,
        string action,
        string body,
        CancellationToken cancellationToken)
    {
        var envelope = BuildEnvelope(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.Concat("application/soap+xml; charset=utf-8; action=\"", action, "\""));

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(string.Concat("ONVIF HTTP ", (int)response.StatusCode, ": ", payload));

        return payload;
    }

    private string BuildEnvelope(string body)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToBase64String(nonceBytes);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var digestBytes = SHA1.HashData(nonceBytes
            .Concat(Encoding.UTF8.GetBytes(created))
            .Concat(Encoding.UTF8.GetBytes(_password))
            .ToArray());

        var digest = Convert.ToBase64String(digestBytes);
        var username = SecurityElement.Escape(_device.Username) ?? "";

        return $"""
<?xml version="1.0" encoding="utf-8"?>
<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
            xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"
            xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">
  <s:Header>
    <wsse:Security s:mustUnderstand="1">
      <wsse:UsernameToken>
        <wsse:Username>{username}</wsse:Username>
        <wsse:Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</wsse:Password>
        <wsse:Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{nonce}</wsse:Nonce>
        <wsu:Created>{created}</wsu:Created>
      </wsse:UsernameToken>
    </wsse:Security>
  </s:Header>
  <s:Body>
    {body}
  </s:Body>
</s:Envelope>
""";
    }

    public void Dispose() => _http.Dispose();
}
