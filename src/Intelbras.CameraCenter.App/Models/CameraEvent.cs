namespace Intelbras.CameraCenter.App.Models;

public sealed record CameraEvent(
    DateTimeOffset Timestamp,
    Guid DeviceId,
    string DeviceName,
    string Code,
    string Action,
    string Summary,
    string RawData);
