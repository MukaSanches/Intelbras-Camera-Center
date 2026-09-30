using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed class EventMonitorService
{
    public async Task RunAsync(
        CameraDevice device,
        string password,
        Action<CameraEvent> onEvent,
        CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var api = new IntelbrasCgiClient(device, password, Timeout.InfiniteTimeSpan);
                using var response = await api.OpenEventStreamAsync(cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                backoff = TimeSpan.FromSeconds(2);

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("Code=", StringComparison.OrdinalIgnoreCase))
                        continue;

                    onEvent(Parse(device, line));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                try { await Task.Delay(backoff, cancellationToken); } catch { return; }
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }
        }
    }

    private static CameraEvent Parse(CameraDevice device, string line)
    {
        var values = line.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        values.TryGetValue("Code", out var code);
        values.TryGetValue("action", out var action);

        code ??= "Unknown";
        action ??= "";

        var summary = code switch
        {
            var value when value.Contains("Traffic", StringComparison.OrdinalIgnoreCase) => "Evento de tráfego / LPR",
            var value when value.Contains("Face", StringComparison.OrdinalIgnoreCase) => "Evento facial",
            var value when value.Contains("Motion", StringComparison.OrdinalIgnoreCase) => "Movimento detectado",
            var value when value.Contains("CrossLine", StringComparison.OrdinalIgnoreCase) => "Cruzamento de linha",
            var value when value.Contains("CrossRegion", StringComparison.OrdinalIgnoreCase) => "Intrusão em região",
            _ => code
        };

        return new CameraEvent(
            DateTimeOffset.Now,
            device.Id,
            device.Name,
            code,
            action,
            summary,
            line);
    }
}
