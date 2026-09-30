using System.Text.Json;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed class IntegrationSettingsRepository
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public IntegrationSettings Settings { get; private set; } = new();

    private static string FilePath => Path.Combine(AppPaths.Config, "integrations.json");

    public async Task LoadAsync()
    {
        AppPaths.EnsureCreated();

        if (!File.Exists(FilePath))
            return;

        try
        {
            await using var stream = File.OpenRead(FilePath);
            Settings = await JsonSerializer.DeserializeAsync<IntegrationSettings>(stream, _json)
                       ?? new IntegrationSettings();
        }
        catch
        {
            Settings = new IntegrationSettings();
        }
    }

    public async Task SaveAsync()
    {
        AppPaths.EnsureCreated();
        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, Settings, _json);
    }

    public string GetHomeAssistantToken()
        => DpapiProtector.Unprotect(Settings.HomeAssistantTokenEncrypted);

    public void SetHomeAssistantToken(string token)
        => Settings.HomeAssistantTokenEncrypted = DpapiProtector.Protect(token);
}
