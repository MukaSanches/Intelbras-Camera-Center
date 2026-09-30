using System.Windows;
using Intelbras.CameraCenter.App.Services;
using LibVLCSharp.Shared;

namespace Intelbras.CameraCenter.App;

public partial class App : System.Windows.Application
{
    public static CameraRepository Cameras { get; private set; } = null!;
    public static IntegrationSettingsRepository Integrations { get; private set; } = null!;
    public static LocalApiServer? LocalApi { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Core.Initialize();
        AppPaths.EnsureCreated();

        Cameras = new CameraRepository();
        await Cameras.LoadAsync();

        Integrations = new IntegrationSettingsRepository();
        await Integrations.LoadAsync();

        if (Integrations.Settings.EnableLocalApi)
        {
            LocalApi = new LocalApiServer(Cameras, Integrations.Settings.LocalApiPort);
            LocalApi.Start();
        }

        var window = new MainWindow(Cameras);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LocalApi?.Dispose();
        base.OnExit(e);
    }
}
