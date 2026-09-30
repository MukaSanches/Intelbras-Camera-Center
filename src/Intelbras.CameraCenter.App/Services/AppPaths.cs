namespace Intelbras.CameraCenter.App.Services;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IntelbrasCameraCenter");

    public static string Config => Path.Combine(Root, "config");
    public static string Recordings => Path.Combine(Root, "recordings");
    public static string Snapshots => Path.Combine(Root, "snapshots");
    public static string DevicesFile => Path.Combine(Config, "devices.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Recordings);
        Directory.CreateDirectory(Snapshots);
    }
}
