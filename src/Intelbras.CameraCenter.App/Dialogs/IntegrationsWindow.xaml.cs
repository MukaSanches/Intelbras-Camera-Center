using System.Windows;
using Intelbras.CameraCenter.App.Services;

namespace Intelbras.CameraCenter.App.Dialogs;

public partial class IntegrationsWindow : Window
{
    private readonly IntegrationSettingsRepository _repository;

    public IntegrationsWindow(IntegrationSettingsRepository repository)
    {
        InitializeComponent();
        _repository = repository;

        var settings = repository.Settings;
        Go2RtcBox.Text = settings.Go2RtcUrl;
        FrigateBox.Text = settings.FrigateUrl;
        HomeAssistantBox.Text = settings.HomeAssistantUrl;
        HomeAssistantTokenBox.Password = repository.GetHomeAssistantToken();
        EnableApiCheck.IsChecked = settings.EnableLocalApi;
        ApiPortBox.Text = settings.LocalApiPort.ToString();
    }

    private IntegrationHubService CreateHub()
        => new(_repository.Settings, HomeAssistantTokenBox.Password);

    private void SyncForm()
    {
        _repository.Settings.Go2RtcUrl = Go2RtcBox.Text.Trim();
        _repository.Settings.FrigateUrl = FrigateBox.Text.Trim();
        _repository.Settings.HomeAssistantUrl = HomeAssistantBox.Text.Trim();
        _repository.Settings.EnableLocalApi = EnableApiCheck.IsChecked == true;

        if (int.TryParse(ApiPortBox.Text, out var port) && port is > 1024 and <= 65535)
            _repository.Settings.LocalApiPort = port;
    }

    private async void TestGo2RtcClick(object sender, RoutedEventArgs e)
    {
        SyncForm();
        using var hub = CreateHub();
        var result = await hub.ProbeGo2RtcAsync();
        StatusText.Text = string.Concat(result.Success ? "OK • " : "ERRO • ", result.Name, " • ", result.Detail);
    }

    private async void TestFrigateClick(object sender, RoutedEventArgs e)
    {
        SyncForm();
        using var hub = CreateHub();
        var result = await hub.ProbeFrigateAsync();
        StatusText.Text = string.Concat(result.Success ? "OK • " : "ERRO • ", result.Name, " • ", result.Detail);
    }

    private async void TestHomeAssistantClick(object sender, RoutedEventArgs e)
    {
        SyncForm();
        using var hub = CreateHub();
        var result = await hub.ProbeHomeAssistantAsync();
        StatusText.Text = string.Concat(result.Success ? "OK • " : "ERRO • ", result.Name, " • ", result.Detail);
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        SyncForm();

        if (!int.TryParse(ApiPortBox.Text, out var port) || port is <= 1024 or > 65535)
        {
            MessageBox.Show(this, "A porta da API local deve estar entre 1025 e 65535.",
                "Intelbras Camera Center", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _repository.Settings.LocalApiPort = port;
        _repository.SetHomeAssistantToken(HomeAssistantTokenBox.Password);
        await _repository.SaveAsync();

        MessageBox.Show(this,
            "Integrações salvas. Alterações na porta da API local entram em vigor ao reiniciar o aplicativo.",
            "Intelbras Camera Center", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
