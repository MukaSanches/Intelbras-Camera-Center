using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed record NetworkVideoEndpoint(
    string Host,
    string DisplayName,
    string Manufacturer,
    DeviceKind Kind,
    int HttpPort,
    int RtspPort,
    int ChannelCountHint,
    bool AuthenticationRequired,
    string DiscoveryTechnology,
    string Evidence);
