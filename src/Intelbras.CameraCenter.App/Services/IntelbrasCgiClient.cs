using System.Net;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed class IntelbrasCgiClient : IDisposable
{
    private readonly HttpClient _http;

    public IntelbrasCgiClient(CameraDevice device, string password, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(device.Username, password),
            PreAuthenticate = false,
            AllowAutoRedirect = true
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(string.Concat("http://", device.Host, ":", device.HttpPort, "/")),
            Timeout = timeout ?? TimeSpan.FromSeconds(8)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Intelbras-Camera-Center/1.0");
    }

    public Task<string> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        => GetStringAsync("cgi-bin/magicBox.cgi?action=getSystemInfo", cancellationToken);

    public Task<string> GetSerialAsync(CancellationToken cancellationToken = default)
        => GetStringAsync("cgi-bin/magicBox.cgi?action=getSerialNo", cancellationToken);

    public Task<string> GetSoftwareVersionAsync(CancellationToken cancellationToken = default)
        => GetStringAsync("cgi-bin/magicBox.cgi?action=getSoftwareVersion", cancellationToken);

    public Task<string> GetConfigAsync(string name, CancellationToken cancellationToken = default)
        => GetStringAsync(string.Concat("cgi-bin/configManager.cgi?action=getConfig&name=", Uri.EscapeDataString(name)), cancellationToken);

    public async Task<HttpResponseMessage> OpenEventStreamAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "cgi-bin/eventManager.cgi?action=attach&codes=[All]");

        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> GetStringAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}
