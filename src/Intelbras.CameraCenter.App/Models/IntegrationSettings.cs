namespace Intelbras.CameraCenter.App.Models;

public sealed class IntegrationSettings
{
    public string Go2RtcUrl { get; set; } = "http://127.0.0.1:1984";
    public string FrigateUrl { get; set; } = "http://127.0.0.1:5000";
    public string HomeAssistantUrl { get; set; } = "http://homeassistant.local:8123";
    public string HomeAssistantTokenEncrypted { get; set; } = "";
    public bool EnableLocalApi { get; set; } = true;
    public int LocalApiPort { get; set; } = 17777;
}
