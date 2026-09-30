using System.Collections.ObjectModel;
using System.Text.Json;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Services;

public sealed class CameraRepository
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<CameraDevice> Devices { get; } = new();

    public async Task LoadAsync()
    {
        AppPaths.EnsureCreated();

        if (!File.Exists(AppPaths.DevicesFile))
            return;

        try
        {
            await using var stream = File.OpenRead(AppPaths.DevicesFile);
            var devices = await JsonSerializer.DeserializeAsync<List<CameraDevice>>(stream, _jsonOptions)
                          ?? new List<CameraDevice>();

            Devices.Clear();
            foreach (var device in devices)
                Devices.Add(device);
        }
        catch
        {
            var backup = string.Concat(AppPaths.DevicesFile, ".corrupt-", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            try { File.Copy(AppPaths.DevicesFile, backup, true); } catch { }
        }
    }

    public async Task SaveAsync()
    {
        AppPaths.EnsureCreated();
        var temp = string.Concat(AppPaths.DevicesFile, ".tmp");

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, Devices.ToList(), _jsonOptions);
        }

        File.Move(temp, AppPaths.DevicesFile, true);
    }

    public bool HasCredential(CameraDevice device)
        => !string.IsNullOrWhiteSpace(device.PasswordEncrypted);

    public string GetPassword(CameraDevice device) => DpapiProtector.Unprotect(device.PasswordEncrypted);

    public void SetPassword(CameraDevice device, string password)
        => device.PasswordEncrypted = DpapiProtector.Protect(password);
}
