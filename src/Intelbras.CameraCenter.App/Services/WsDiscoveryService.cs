using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Intelbras.CameraCenter.App.Services;

public sealed record DiscoveredDevice(string Host, string XAddr, string Types);

public sealed class WsDiscoveryService
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 3702;

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var probe = BuildProbe();
        var bytes = Encoding.UTF8.GetBytes(probe);
        await udp.SendAsync(bytes, bytes.Length,
            new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration ?? TimeSpan.FromSeconds(4));

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                var xml = Encoding.UTF8.GetString(packet.Buffer);
                foreach (var item in Parse(xml, packet.RemoteEndPoint.Address.ToString()))
                    results[item.Host] = item;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore malformed discovery responses from unrelated devices.
            }
        }

        return results.Values.OrderBy(x => x.Host).ToList();
    }

    private static IEnumerable<DiscoveredDevice> Parse(string xml, string fallbackHost)
    {
        var document = XDocument.Parse(xml);

        var xAddrs = document.Descendants()
            .Where(x => x.Name.LocalName.Equals("XAddrs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var types = document.Descendants()
            .FirstOrDefault(x => x.Name.LocalName.Equals("Types", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? "";

        foreach (var xaddr in xAddrs)
        {
            var host = fallbackHost;
            if (Uri.TryCreate(xaddr, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                host = uri.Host;

            yield return new DiscoveredDevice(host, xaddr, types);
        }
    }

    private static string BuildProbe()
    {
        var id = Guid.NewGuid();
        return string.Concat(
            "<?xml version="1.0" encoding="UTF-8"?>",
            "<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope" ",
            "xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing" ",
            "xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery" ",
            "xmlns:dn="http://www.onvif.org/ver10/network/wsdl">",
            "<e:Header><w:MessageID>uuid:", id,
            "</w:MessageID><w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>",
            "<w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header>",
            "<e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>");
    }
}
