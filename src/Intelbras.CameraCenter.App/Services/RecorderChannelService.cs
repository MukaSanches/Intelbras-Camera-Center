using System.Text.RegularExpressions;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public static class RecorderChannelService
{
    public static async Task<int> ExpandVerifiedRecorderAsync(
        CameraRepository repository,
        CameraDevice source,
        string deviceInfo)
    {
        var combined = string.Concat(source.Name, " ", source.Manufacturer, " ", source.DiscoverySource, " ", deviceInfo);

        if (Regex.IsMatch(combined, @"\bi?MHDX\b|\bDVR\b", RegexOptions.IgnoreCase))
            source.Kind = DeviceKind.DVR;
        else if (Regex.IsMatch(combined, @"\bi?NVD\b|\bNVR\b", RegexOptions.IgnoreCase))
            source.Kind = DeviceKind.NVR;

        var channelCount = Math.Max(source.ChannelCountHint, UniversalDiscoveryService.InferChannelCount(combined));
        source.ChannelCountHint = channelCount;

        if (source.Kind is not (DeviceKind.DVR or DeviceKind.NVR) || channelCount <= 1)
        {
            await repository.SaveAsync();
            return 0;
        }

        var created = 0;
        var baseName = NormalizeBaseName(source.Name);
        source.Channel = Math.Max(1, source.Channel);
        source.Name = string.Concat(baseName, " • CH ", source.Channel.ToString("D2"));

        for (var channel = 1; channel <= channelCount; channel++)
        {
            if (repository.Devices.Any(x =>
                    x.Host.Equals(source.Host, StringComparison.OrdinalIgnoreCase) &&
                    x.Channel == channel))
                continue;

            repository.Devices.Add(new CameraDevice
            {
                Name = string.Concat(baseName, " • CH ", channel.ToString("D2")),
                Host = source.Host,
                RtspPort = source.RtspPort,
                HttpPort = source.HttpPort,
                Channel = channel,
                Username = source.Username,
                PasswordEncrypted = source.PasswordEncrypted,
                Kind = source.Kind,
                UseSubStream = source.UseSubStream,
                EnableEvents = source.EnableEvents,
                Manufacturer = source.Manufacturer,
                DiscoverySource = source.DiscoverySource,
                ChannelCountHint = channelCount,
                AuthenticationRequired = false
            });
            created++;
        }

        await repository.SaveAsync();
        return created;
    }

    private static string NormalizeBaseName(string name)
        => Regex.Replace(name, @"\s*•\s*CH\s*\d+\s*$", "", RegexOptions.IgnoreCase).Trim();
}
