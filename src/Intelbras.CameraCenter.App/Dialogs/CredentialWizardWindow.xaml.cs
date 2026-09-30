using System.Windows;
using Intelbras.CameraCenter.App.Models;
using Intelbras.CameraCenter.App.Services;

namespace Intelbras.CameraCenter.App.Dialogs;

public partial class CredentialWizardWindow : Window
{
    private readonly CameraRepository _repository;
    private readonly CameraDevice _device;

    public CredentialWizardWindow(CameraRepository repository, CameraDevice device)
    {
        InitializeComponent();
        _repository = repository;
        _device = device;

        DeviceText.Text = string.Concat(
            device.Name, " • ", device.Host,
            device.ChannelCountHint > 1 ? string.Concat(" • até ", device.ChannelCountHint, " canais detectados") : "");

        UsernameBox.Text = string.IsNullOrWhiteSpace(device.Username) ? "admin" : device.Username;
        PasswordBox.Password = repository.GetPassword(device);
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "Informe a credencial configurada no equipamento.";
            return;
        }

        SaveButton.IsEnabled = false;
        StatusText.Text = "Validando acesso...";

        try
        {
            var targets = SameHostCheck.IsChecked == true
                ? _repository.Devices.Where(x => x.Host.Equals(_device.Host, StringComparison.OrdinalIgnoreCase)).ToList()
                : [_device];

            foreach (var target in targets)
            {
                target.Username = username;
                _repository.SetPassword(target, password);
            }

            var verified = false;
            string deviceInfo = "";

            try
            {
                using var cgi = new IntelbrasCgiClient(_device, password, TimeSpan.FromSeconds(5));
                deviceInfo = await cgi.GetDeviceInfoAsync();
                verified = true;
            }
            catch
            {
                try
                {
                    using var onvif = new OnvifClient(_device, password);
                    await onvif.ProbeAsync();
                    verified = true;
                }
                catch
                {
                }
            }

            _device.AuthenticationRequired = !verified;
            await _repository.SaveAsync();

            var createdChannels = 0;
            if (verified && !string.IsNullOrWhiteSpace(deviceInfo))
                createdChannels = await RecorderChannelService.ExpandVerifiedRecorderAsync(_repository, _device, deviceInfo);

            StatusText.Text = verified
                ? string.Concat("Credencial validada e protegida pelo Windows.",
                    createdChannels > 0 ? string.Concat(" • ", createdChannels, " canais adicionados.") : "")
                : "Credencial salva. O dispositivo não respondeu aos métodos CGI/ONVIF conhecidos; ela será usada quando o stream for aberto.";

            DialogResult = true;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();
}
