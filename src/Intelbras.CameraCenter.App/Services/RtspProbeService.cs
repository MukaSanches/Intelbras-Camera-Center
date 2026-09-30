using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Intelbras.CameraCenter.App.Services;

public enum RtspProbeState
{
    Available,
    AuthenticationRequired,
    Unavailable
}

public sealed record RtspProbeResult(RtspProbeState State, int StatusCode, string Server);

public sealed class RtspProbeService
{
    public async Task<RtspProbeResult> ProbeAsync(
        Uri uri,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            return new(RtspProbeState.Unavailable, 0, "");

        var first = await SendDescribeAsync(uri, null, cancellationToken);

        if (first.StatusCode == 200)
            return new(RtspProbeState.Available, 200, first.Server);

        if (first.StatusCode != 401)
            return new(RtspProbeState.Unavailable, first.StatusCode, first.Server);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new(RtspProbeState.AuthenticationRequired, 401, first.Server);

        var challenge = first.Authenticate;
        if (string.IsNullOrWhiteSpace(challenge))
            return new(RtspProbeState.AuthenticationRequired, 401, first.Server);

        string? authorization = null;

        if (challenge.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
        {
            authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(username, ":", password)));
        }
        else if (challenge.StartsWith("Digest", StringComparison.OrdinalIgnoreCase))
        {
            authorization = BuildDigestAuthorization(uri, username, password, challenge);
        }

        if (string.IsNullOrWhiteSpace(authorization))
            return new(RtspProbeState.AuthenticationRequired, 401, first.Server);

        var second = await SendDescribeAsync(uri, authorization, cancellationToken);

        return second.StatusCode == 200
            ? new(RtspProbeState.Available, 200, second.Server)
            : second.StatusCode == 401
                ? new(RtspProbeState.AuthenticationRequired, 401, second.Server)
                : new(RtspProbeState.Unavailable, second.StatusCode, second.Server);
    }

    private static async Task<(int StatusCode, string Authenticate, string Server)> SendDescribeAsync(
        Uri uri,
        string? authorization,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1400));

            await client.ConnectAsync(uri.Host, uri.Port > 0 ? uri.Port : 554, timeout.Token);
            await using var stream = client.GetStream();

            var clean = new UriBuilder(uri)
            {
                UserName = "",
                Password = ""
            }.Uri.AbsoluteUri;

            var request = new StringBuilder()
                .Append("DESCRIBE ").Append(clean).Append(" RTSP/1.0\r\n")
                .Append("CSeq: 1\r\n")
                .Append("Accept: application/sdp\r\n")
                .Append("User-Agent: Intelbras-Camera-Center/1.3\r\n");

            if (!string.IsNullOrWhiteSpace(authorization))
                request.Append("Authorization: ").Append(authorization).Append("\r\n");

            request.Append("\r\n");

            var bytes = Encoding.ASCII.GetBytes(request.ToString());
            await stream.WriteAsync(bytes.AsMemory(), timeout.Token);
            await stream.FlushAsync(timeout.Token);

            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
            var statusLine = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(statusLine))
                return (0, "", "");

            var statusParts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var statusCode = statusParts.Length > 1 && int.TryParse(statusParts[1], out var code) ? code : 0;

            string authenticate = "";
            string server = "";

            while (true)
            {
                var line = await reader.ReadLineAsync(timeout.Token);
                if (string.IsNullOrEmpty(line))
                    break;

                var colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();

                if (name.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase))
                    authenticate = value;
                else if (name.Equals("Server", StringComparison.OrdinalIgnoreCase))
                    server = value;
            }

            return (statusCode, authenticate, server);
        }
        catch
        {
            return (0, "", "");
        }
    }

    private static string? BuildDigestAuthorization(
        Uri uri,
        string username,
        string password,
        string challenge)
    {
        var values = Regex.Matches(challenge, @"(\w+)=(?:""([^""]*)""|([^,\s]+))")
            .Cast<Match>()
            .ToDictionary(
                m => m.Groups[1].Value,
                m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value,
                StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("realm", out var realm) ||
            !values.TryGetValue("nonce", out var nonce))
            return null;

        values.TryGetValue("qop", out var qopValues);
        values.TryGetValue("opaque", out var opaque);

        var qop = qopValues?.Split(',').Select(x => x.Trim()).FirstOrDefault(x =>
            x.Equals("auth", StringComparison.OrdinalIgnoreCase));

        var cleanUri = new UriBuilder(uri) { UserName = "", Password = "" }.Uri.AbsoluteUri;
        var ha1 = Md5(string.Concat(username, ":", realm, ":", password));
        var ha2 = Md5(string.Concat("DESCRIBE:", cleanUri));

        string response;
        string? cnonce = null;
        const string nc = "00000001";

        if (!string.IsNullOrWhiteSpace(qop))
        {
            cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            response = Md5(string.Concat(ha1, ":", nonce, ":", nc, ":", cnonce, ":", qop, ":", ha2));
        }
        else
        {
            response = Md5(string.Concat(ha1, ":", nonce, ":", ha2));
        }

        var header = new StringBuilder("Digest ")
            .Append("username=\"").Append(username).Append("\", ")
            .Append("realm=\"").Append(realm).Append("\", ")
            .Append("nonce=\"").Append(nonce).Append("\", ")
            .Append("uri=\"").Append(cleanUri).Append("\", ")
            .Append("response=\"").Append(response).Append("\"");

        if (!string.IsNullOrWhiteSpace(qop) && cnonce is not null)
        {
            header.Append(", qop=").Append(qop)
                .Append(", nc=").Append(nc)
                .Append(", cnonce=\"").Append(cnonce).Append("\"");
        }

        if (!string.IsNullOrWhiteSpace(opaque))
            header.Append(", opaque=\"").Append(opaque).Append("\"");

        return header.ToString();
    }

    private static string Md5(string text)
        => Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(text))).ToLowerInvariant();
}
