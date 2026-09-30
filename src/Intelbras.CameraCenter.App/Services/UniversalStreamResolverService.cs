using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed record StreamCandidate(
    Uri Uri,
    string Label,
    bool ForceTcp,
    int NetworkCaching = 600);

public sealed record StreamResolution(
    IReadOnlyList<StreamCandidate> Candidates,
    bool CredentialsLikelyRequired,
    string Summary);

public sealed class UniversalStreamResolverService
{
    public async Task<StreamResolution> ResolveAsync(
        CameraDevice device,
        string password,
        CancellationToken cancellationToken = default)
    {
        var trusted = new List<StreamCandidate>();
        var templates = new List<(Uri Uri, string Label)>();
        var authLikely = false;

        if (!string.IsNullOrWhiteSpace(device.CustomRtspUrl) &&
            Uri.TryCreate(device.CustomRtspUrl, UriKind.Absolute, out var custom))
        {
            var withCredentials = AddCredentials(custom, device.Username, password);
            AddTransportVariants(trusted, withCredentials, "URL configurada");
        }

        try
        {
            using var onvif = new OnvifClient(device, password);
            var onvifUri = await onvif.GetStreamUriAsync(cancellationToken);
            onvifUri = AddCredentials(onvifUri, device.Username, password);
            AddTransportVariants(trusted, onvifUri, "ONVIF GetStreamUri");
        }
        catch (HttpRequestException ex) when (
            ex.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            authLikely = true;
        }
        catch
        {
        }

        foreach (var item in BuildRtspTemplateUris(device, password))
            templates.Add(item);

        var distinctTemplates = templates
            .GroupBy(x => x.Uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var probe = new RtspProbeService();
        using var gate = new SemaphoreSlim(6);

        var probed = await Task.WhenAll(distinctTemplates.Select(async (candidate, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var result = await probe.ProbeAsync(
                    candidate.Uri,
                    device.Username,
                    password,
                    cancellationToken);

                return (Index: index, Candidate: candidate, Result: result);
            }
            finally
            {
                gate.Release();
            }
        }));

        var verified = probed
            .Where(x => x.Result.State == RtspProbeState.Available)
            .OrderBy(x => x.Index)
            .Select(x => x.Candidate)
            .ToList();

        if (probed.Any(x => x.Result.State == RtspProbeState.AuthenticationRequired))
            authLikely = true;

        var final = new List<StreamCandidate>();
        final.AddRange(trusted);

        foreach (var item in verified)
            AddTransportVariants(final, item.Uri, item.Label);

        if (verified.Count == 0)
        {
            foreach (var item in distinctTemplates.Take(8))
                AddTransportVariants(final, item.Uri, string.Concat(item.Label, " • fallback"));
        }

        foreach (var item in BuildHttpFallbacks(device, password))
            final.Add(new StreamCandidate(item.Uri, item.Label, false, 900));

        final = final
            .GroupBy(x => string.Concat(x.Uri.AbsoluteUri, "|", x.ForceTcp), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(28)
            .ToList();

        return new StreamResolution(
            final,
            authLikely && string.IsNullOrEmpty(password),
            string.Concat(
                trusted.Count > 0 ? "ONVIF/configurado • " : "",
                verified.Count, " caminhos RTSP verificados • ",
                final.Count, " tentativas disponíveis"));
    }

    private static IEnumerable<(Uri Uri, string Label)> BuildRtspTemplateUris(
        CameraDevice device,
        string password)
    {
        var channel = Math.Max(1, device.Channel);
        var subtype = device.UseSubStream ? 1 : 0;
        var stream = device.UseSubStream ? 2 : 1;
        var evidence = string.Concat(device.Manufacturer, " ", device.Name, " ", device.DiscoverySource);

        var paths = new List<(string Path, string Label)>();

        void Add(string path, string label) => paths.Add((path, label));

        if (ContainsAny(evidence, "intelbras", "dahua", "amcrest", "mhdx", "nvd", "invd"))
            Add($"/cam/realmonitor?channel={channel}&subtype={subtype}", "Intelbras/Dahua/Amcrest");

        if (ContainsAny(evidence, "hikvision", "hilook"))
            Add($"/Streaming/Channels/{channel}{(device.UseSubStream ? "02" : "01")}", "Hikvision/HiLook");

        if (ContainsAny(evidence, "axis"))
            Add("/axis-media/media.amp", "Axis");

        if (ContainsAny(evidence, "reolink"))
            Add($"/Preview_{channel:D2}_{(device.UseSubStream ? "sub" : "main")}", "Reolink");

        if (ContainsAny(evidence, "uniview", "unv"))
            Add($"/media/video{stream}", "Uniview");

        if (ContainsAny(evidence, "tapo", "tp-link"))
            Add($"/stream{stream}", "TP-Link/Tapo");

        if (ContainsAny(evidence, "foscam"))
            Add(device.UseSubStream ? "/videoSub" : "/videoMain", "Foscam");

        // Universal path library. Order favors the most common global VMS conventions.
        Add($"/cam/realmonitor?channel={channel}&subtype={subtype}", "Dahua/Intelbras-compatible");
        Add($"/Streaming/Channels/{channel}{(device.UseSubStream ? "02" : "01")}", "Hikvision-compatible");
        Add($"/Preview_{channel:D2}_{(device.UseSubStream ? "sub" : "main")}", "Reolink-compatible");
        Add($"/media/video{stream}", "UNV-compatible");
        Add($"/stream{stream}", "Generic stream");
        Add(device.UseSubStream ? "/videoSub" : "/videoMain", "Generic videoMain/videoSub");
        Add("/axis-media/media.amp", "Axis-compatible");
        Add("/live", "Generic /live");
        Add("/live.sdp", "Generic /live.sdp");
        Add("/h264", "Generic /h264");
        Add("/h264.sdp", "Generic /h264.sdp");
        Add("/h264Preview_01_main", "Generic H264 preview");
        Add("/live/ch00_0", "Generic live channel");
        Add($"/ch{Math.Max(0, channel - 1)}_{subtype}.h264", "Generic H264 channel");
        Add("/11", "Generic channel 11");
        Add("/12", "Generic channel 12");
        Add("/mpeg4/media.amp", "Legacy MPEG4 camera");

        foreach (var (path, label) in paths)
        {
            var uri = BuildUri("rtsp", device.Host, device.RtspPort, path, device.Username, password);
            if (uri is not null)
                yield return (uri, label);
        }
    }

    private static IEnumerable<(Uri Uri, string Label)> BuildHttpFallbacks(
        CameraDevice device,
        string password)
    {
        var scheme = device.HttpPort == 443 ? "https" : "http";
        var paths = new[]
        {
            ("/video", "HTTP video"),
            ("/mjpeg", "MJPEG"),
            ("/videostream.cgi", "MJPEG CGI"),
            ("/mjpg/video.mjpg", "MJPEG Axis-compatible"),
            ("/axis-cgi/mjpg/video.cgi", "Axis MJPEG")
        };

        foreach (var (path, label) in paths)
        {
            var uri = BuildUri(scheme, device.Host, device.HttpPort, path, device.Username, password);
            if (uri is not null)
                yield return (uri, label);
        }
    }

    private static void AddTransportVariants(List<StreamCandidate> target, Uri uri, string label)
    {
        if (uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase))
        {
            target.Add(new StreamCandidate(uri, string.Concat(label, " • TCP"), true, 550));
            target.Add(new StreamCandidate(uri, string.Concat(label, " • auto/UDP"), false, 750));
        }
        else
        {
            target.Add(new StreamCandidate(uri, label, false, 900));
        }
    }

    private static Uri AddCredentials(Uri uri, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(uri.UserInfo))
            return uri;

        try
        {
            var builder = new UriBuilder(uri)
            {
                UserName = username,
                Password = password
            };
            return builder.Uri;
        }
        catch
        {
            return uri;
        }
    }

    private static Uri? BuildUri(
        string scheme,
        string host,
        int port,
        string pathAndQuery,
        string username,
        string password)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var authority = string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)
            ? host
            : string.Concat(Uri.EscapeDataString(username), ":", Uri.EscapeDataString(password), "@", host);

        var url = string.Concat(scheme, "://", authority, ":", port, pathAndQuery);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
