using System.Net.Http.Headers;
using System.Text.Json;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed record IntegrationProbeResult(bool Success, string Name, string Detail);

public sealed class IntegrationHubService : IDisposable
{
    private readonly IntegrationSettings _settings;
    private readonly string _homeAssistantToken;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(7) };

    public IntegrationHubService(IntegrationSettings settings, string homeAssistantToken)
    {
        _settings = settings;
        _homeAssistantToken = homeAssistantToken;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Intelbras-Camera-Center/1.1");
    }

    public async Task<IntegrationProbeResult> ProbeGo2RtcAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(Build(_settings.Go2RtcUrl, "/api"), cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(json);
            var version = document.RootElement.TryGetProperty("version", out var value)
                ? value.GetString()
                : null;

            return new(true, "go2rtc", string.Concat("Conectado", string.IsNullOrWhiteSpace(version) ? "" : " • v" + version));
        }
        catch (Exception ex)
        {
            return new(false, "go2rtc", ex.Message);
        }
    }

    public async Task<IntegrationProbeResult> ProbeFrigateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(Build(_settings.FrigateUrl, "/api/version"), cancellationToken);
            response.EnsureSuccessStatusCode();
            var version = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim().Trim('"');
            return new(true, "Frigate", string.Concat("Conectado", string.IsNullOrWhiteSpace(version) ? "" : " • v" + version));
        }
        catch (Exception ex)
        {
            return new(false, "Frigate", ex.Message);
        }
    }

    public async Task<IntegrationProbeResult> ProbeHomeAssistantAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Build(_settings.HomeAssistantUrl, "/api/"));
            if (!string.IsNullOrWhiteSpace(_homeAssistantToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _homeAssistantToken);

            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return new(true, "Home Assistant", "REST API autenticada");
        }
        catch (Exception ex)
        {
            return new(false, "Home Assistant", ex.Message);
        }
    }

    public async Task<IntegrationProbeResult> PublishToGo2RtcAsync(
        string streamName,
        Uri source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var safeName = NormalizeName(streamName);
            var url = string.Concat(
                Build(_settings.Go2RtcUrl, "/api/streams"),
                "?name=", Uri.EscapeDataString(safeName),
                "&src=", Uri.EscapeDataString(source.AbsoluteUri));

            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            return new(true, "go2rtc",
                string.Concat("Stream publicado como “", safeName,
                    "” • WebRTC/WHEP, HLS, MP4 e MJPEG disponíveis pelo gateway"));
        }
        catch (Exception ex)
        {
            return new(false, "go2rtc", ex.Message);
        }
    }

    public Uri BuildGo2RtcMp4Uri(string streamName)
        => new(string.Concat(Build(_settings.Go2RtcUrl, "/api/stream.mp4"), "?src=", Uri.EscapeDataString(NormalizeName(streamName))));

    public Uri BuildGo2RtcWebRtcEndpoint(string streamName)
        => new(string.Concat(Build(_settings.Go2RtcUrl, "/api/webrtc"), "?src=", Uri.EscapeDataString(NormalizeName(streamName))));

    private static string Build(string root, string path)
    {
        if (!Uri.TryCreate(root, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("URL de integração inválida.");

        return new Uri(baseUri, path).AbsoluteUri;
    }

    public void Dispose() => _http.Dispose();

    private static string NormalizeName(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "camera" : normalized;
    }
}
