using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Intelbras.CameraCenter.App.Services;

public sealed class LocalApiServer : IDisposable
{
    private readonly CameraRepository _cameras;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private Task? _loop;

    public LocalApiServer(CameraRepository cameras, int port)
    {
        _cameras = cameras;
        _port = port;
    }

    public bool Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(requestLine))
                return;

            var parts = requestLine.Split(' ');
            var path = parts.Length > 1 ? parts[1] : "/";

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
            {
            }

            var (status, contentType, body) = BuildResponse(path);
            var payload = Encoding.UTF8.GetBytes(body);
            var headers = string.Concat(
                "HTTP/1.1 ", status, "\r\n",
                "Content-Type: ", contentType, "; charset=utf-8\r\n",
                "Content-Length: ", payload.Length, "\r\n",
                "Cache-Control: no-store\r\n",
                "Access-Control-Allow-Origin: *\r\n",
                "Connection: close\r\n\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
        }
    }

    private (string Status, string ContentType, string Body) BuildResponse(string path)
    {
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return ("200 OK", "application/json",
                JsonSerializer.Serialize(new
                {
                    app = "Intelbras Camera Center",
                    version = "1.1.0",
                    status = "ok",
                    cameras = _cameras.Devices.Count
                }));
        }

        if (path.StartsWith("/api/cameras", StringComparison.OrdinalIgnoreCase))
        {
            var safe = _cameras.Devices.Select(x => new
            {
                x.Id,
                x.Name,
                x.Host,
                x.Kind,
                x.Channel,
                x.UseSubStream,
                x.EnableEvents,
                x.StreamTechnology
            });

            return ("200 OK", "application/json", JsonSerializer.Serialize(safe));
        }

        if (path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
        {
            var eventsEnabled = _cameras.Devices.Count(x => x.EnableEvents);
            var body = string.Concat(
                "# HELP intelbras_camera_center_cameras_total Configured cameras\n",
                "# TYPE intelbras_camera_center_cameras_total gauge\n",
                "intelbras_camera_center_cameras_total ", _cameras.Devices.Count, "\n",
                "# HELP intelbras_camera_center_event_sources_total Event-enabled devices\n",
                "# TYPE intelbras_camera_center_event_sources_total gauge\n",
                "intelbras_camera_center_event_sources_total ", eventsEnabled, "\n");

            return ("200 OK", "text/plain", body);
        }

        return ("404 Not Found", "application/json", "{\"error\":\"not_found\"}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
    }
}
