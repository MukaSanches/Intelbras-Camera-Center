using LibVLCSharp.Shared;

namespace Intelbras.CameraCenter.App.Services;

public static class VlcRuntime
{
    private static readonly Lazy<LibVLC> LazyInstance = new(() =>
        new LibVLC(
            "--no-video-title-show",
            "--no-snapshot-preview",
            "--quiet"));

    public static LibVLC Instance => LazyInstance.Value;
}
