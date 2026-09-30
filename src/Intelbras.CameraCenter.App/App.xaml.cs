using System.Windows;
using Intelbras.CameraCenter.App.Services;
using LibVLCSharp.Shared;

namespace Intelbras.CameraCenter.App;

public partial class App : System.Windows.Application
{
    public static CameraRepository Cameras { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Core.Initialize();
        AppPaths.EnsureCreated();

        Cameras = new CameraRepository();
        await Cameras.LoadAsync();

        var window = new MainWindow(Cameras);
        MainWindow = window;
        window.Show();
    }
}
