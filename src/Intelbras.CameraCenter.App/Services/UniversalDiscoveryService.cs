using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed class UniversalDiscoveryService
{
    private static readonly int[] ProbePorts = [80, 443, 554, 37777, 8000, 8080, 8554];
    private static readonly string[] VideoKeywords =
    [
        "intelbras", "dahua", "onvif", "network video", "ip camera", "camera",
        "dvr", "nvr", "mhdx", "nvd", "invd", "imhdx", "web service"
    ];

    public async Task<IReadOnlyList<NetworkVideoEndpoint>> DiscoverAsync(
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, NetworkVideoEndpoint>(StringComparer.OrdinalIgnoreCase);

        var onvifTask = DiscoverOnvifAsync(cancellationToken);
        var ssdpTask = DiscoverSsdpAsync(duration ?? TimeSpan.FromSeconds(3), cancellationToken);
        var subnetTask = ScanLocalSubnetsAsync(cancellationToken);

        foreach (var source in await Task.WhenAll(onvifTask, ssdpTask, subnetTask))
        {
            foreach (var endpoint in source)
                Merge(results, endpoint);
        }

        return results.Values
            .OrderByDescending(x => x.Manufacturer.Equals("Intelbras", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => IpSortKey(x.Host))
            .ToList();
    }

    private static async Task<IReadOnlyList<NetworkVideoEndpoint>> DiscoverOnvifAsync(CancellationToken cancellationToken)
    {
        var discovered = await new WsDiscoveryService()
            .DiscoverAsync(TimeSpan.FromSeconds(4), cancellationToken);

        return discovered.Select(x => new NetworkVideoEndpoint(
            x.Host,
            string.Concat("ONVIF • ", x.Host),
            InferManufacturer(x.Types),
            DeviceKind.Camera,
            HttpPortFromXAddr(x.XAddr),
            554,
            1,
            false,
            "ONVIF / WS-Discovery",
            x.Types)).ToList();
    }

    private static async Task<IReadOnlyList<NetworkVideoEndpoint>> DiscoverSsdpAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, NetworkVideoEndpoint>(StringComparer.OrdinalIgnoreCase);

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var request = string.Join("\r\n",
            "M-SEARCH * HTTP/1.1",
            "HOST: 239.255.255.250:1900",
            "MAN: \"ssdp:discover\"",
            "MX: 2",
            "ST: ssdp:all",
            "",
            "");

        var payload = Encoding.ASCII.GetBytes(request);
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                var text = Encoding.UTF8.GetString(packet.Buffer);
                var headers = ParseHeaders(text);

                headers.TryGetValue("SERVER", out var server);
                headers.TryGetValue("LOCATION", out var location);
                headers.TryGetValue("USN", out var usn);

                var evidence = string.Join(" • ", new[] { server, location, usn }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!LooksLikeVideo(evidence))
                    continue;

                var host = packet.RemoteEndPoint.Address.ToString();
                var httpPort = 80;
                if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
                {
                    host = uri.Host;
                    httpPort = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
                }

                results[host] = new NetworkVideoEndpoint(
                    host,
                    string.Concat("UPnP vídeo • ", host),
                    InferManufacturer(evidence),
                    InferKind(evidence),
                    httpPort,
                    554,
                    InferChannelCount(evidence),
                    false,
                    "SSDP / UPnP",
                    evidence);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }

        return results.Values.ToList();
    }

    private static async Task<IReadOnlyList<NetworkVideoEndpoint>> ScanLocalSubnetsAsync(CancellationToken cancellationToken)
    {
        var hosts = GetCandidateIpv4Hosts();
        var results = new List<NetworkVideoEndpoint>();
        using var gate = new SemaphoreSlim(64);

        var tasks = hosts.Select(async host =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var endpoint = await ProbeHostAsync(host, cancellationToken);
                if (endpoint is not null)
                {
                    lock (results)
                        results.Add(endpoint);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private static IReadOnlyList<string> GetCandidateIpv4Hosts()
    {
        var networks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var bytes = address.Address.GetAddressBytes();
                if (bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254))
                    continue;

                networks.Add(string.Concat(bytes[0], ".", bytes[1], ".", bytes[2]));
                localIps.Add(address.Address.ToString());
            }
        }

        var hosts = new List<string>();
        foreach (var prefix in networks.Take(8))
        {
            for (var i = 1; i <= 254; i++)
            {
                var host = string.Concat(prefix, ".", i);
                if (!localIps.Contains(host))
                    hosts.Add(host);
            }
        }

        return hosts;
    }

    private static async Task<NetworkVideoEndpoint?> ProbeHostAsync(string host, CancellationToken cancellationToken)
    {
        var open = new List<int>();

        foreach (var port in ProbePorts)
        {
            if (await IsPortOpenAsync(host, port, TimeSpan.FromMilliseconds(280), cancellationToken))
                open.Add(port);
        }

        if (open.Count == 0)
            return null;

        var httpPort = open.Contains(80) ? 80 : open.Contains(443) ? 443 : open.Contains(8080) ? 8080 : 80;
        var rtspPort = open.Contains(554) ? 554 : open.Contains(8554) ? 8554 : 554;

        var fingerprint = await ProbeHttpFingerprintAsync(host, httpPort, cancellationToken);
        var hasVideoPort = open.Contains(554) || open.Contains(8554) || open.Contains(37777);
        var evidence = string.Concat("ports=", string.Join(",", open), " • ", fingerprint.Text);

        if (!hasVideoPort && !LooksLikeVideo(evidence))
            return null;

        var manufacturer = InferManufacturer(evidence);
        var kind = InferKind(evidence);
        if (kind == DeviceKind.Other && open.Contains(37777))
            kind = DeviceKind.DVR;

        var label = manufacturer == "Intelbras"
            ? string.Concat("Intelbras • ", host)
            : string.Concat("Vídeo IP • ", host);

        return new NetworkVideoEndpoint(
            host,
            label,
            manufacturer,
            kind,
            httpPort,
            rtspPort,
            InferChannelCount(evidence),
            fingerprint.AuthenticationRequired,
            "LAN Probe / RTSP / HTTP",
            evidence);
    }

    private static async Task<(string Text, bool AuthenticationRequired)> ProbeHttpFingerprintAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var scheme = port == 443 ? "https" : "http";

        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                AllowAutoRedirect = false
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(900) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Intelbras-Camera-Center/1.2");

            using var response = await http.GetAsync(
                string.Concat(scheme, "://", host, ":", port, "/"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var text = new StringBuilder();
            text.Append(response.StatusCode).Append(' ');
            text.Append(response.Headers.Server?.ToString()).Append(' ');
            text.Append(response.Headers.WwwAuthenticate.ToString()).Append(' ');

            if (response.Content.Headers.ContentLength is null or < 65536)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                text.Append(body.Length > 8192 ? body[..8192] : body);
            }

            return (text.ToString(), response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }
        catch
        {
            return ("", false);
        }
    }

    private static async Task<bool> IsPortOpenAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            await client.ConnectAsync(IPAddress.Parse(host), port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseHeaders(string response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in response.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
                continue;
            headers[line[..index].Trim()] = line[(index + 1)..].Trim();
        }
        return headers;
    }

    private static bool LooksLikeVideo(string? text)
        => !string.IsNullOrWhiteSpace(text) &&
           VideoKeywords.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static string InferManufacturer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Genérico";

        if (text.Contains("intelbras", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mhdx", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("invd", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(text, @"\bnvd\s*\d", RegexOptions.IgnoreCase))
            return "Intelbras";

        if (text.Contains("dahua", StringComparison.OrdinalIgnoreCase))
            return "Dahua / Intelbras-compatible";

        return "ONVIF / Genérico";
    }

    private static DeviceKind InferKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DeviceKind.Other;

        if (Regex.IsMatch(text, @"\bi?mhdx\b|\bdvr\b", RegexOptions.IgnoreCase))
            return DeviceKind.DVR;

        if (Regex.IsMatch(text, @"\bi?nvd\b|\bnvr\b", RegexOptions.IgnoreCase))
            return DeviceKind.NVR;

        if (Regex.IsMatch(text, @"\bcamera\b|\bipc\b|networkvideotransmitter", RegexOptions.IgnoreCase))
            return DeviceKind.Camera;

        return DeviceKind.Other;
    }

    public static int InferChannelCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;

        var explicitMatch = Regex.Match(text,
            @"(?:max(?:imum)?\s*)?(?:video\s*)?channels?\s*[:=]\s*(\d{1,3})",
            RegexOptions.IgnoreCase);

        if (explicitMatch.Success &&
            int.TryParse(explicitMatch.Groups[1].Value, out var explicitCount) &&
            explicitCount is >= 1 and <= 320)
            return explicitCount;

        var model = Regex.Match(text,
            @"\b(?:i?MHDX|i?NVD)\s*[- ]?(\d{4})\b",
            RegexOptions.IgnoreCase);

        if (model.Success)
        {
            var digits = model.Groups[1].Value;
            if (int.TryParse(digits[^2..], out var count) && count is >= 1 and <= 64)
                return count;
        }

        return 1;
    }

    private static int HttpPortFromXAddr(string xaddr)
    {
        if (!Uri.TryCreate(xaddr, UriKind.Absolute, out var uri))
            return 80;
        return uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
    }

    private static void Merge(
        IDictionary<string, NetworkVideoEndpoint> results,
        NetworkVideoEndpoint incoming)
    {
        if (!results.TryGetValue(incoming.Host, out var existing))
        {
            results[incoming.Host] = incoming;
            return;
        }

        var manufacturer = existing.Manufacturer == "Intelbras" ? existing.Manufacturer : incoming.Manufacturer;
        var kind = existing.Kind != DeviceKind.Other ? existing.Kind : incoming.Kind;
        var channels = Math.Max(existing.ChannelCountHint, incoming.ChannelCountHint);
        var auth = existing.AuthenticationRequired || incoming.AuthenticationRequired;
        var source = string.Join(" + ",
            existing.DiscoveryTechnology.Split(" + ")
                .Concat(incoming.DiscoveryTechnology.Split(" + "))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        results[incoming.Host] = existing with
        {
            Manufacturer = manufacturer,
            Kind = kind,
            ChannelCountHint = channels,
            AuthenticationRequired = auth,
            DiscoveryTechnology = source,
            Evidence = string.Concat(existing.Evidence, " • ", incoming.Evidence)
        };
    }

    private static long IpSortKey(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return long.MaxValue;

        var b = ip.GetAddressBytes();
        return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
    }
}
