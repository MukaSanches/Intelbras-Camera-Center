using System.Windows;
using Intelbras.CameraCenter.App.Models;

namespace Intelbras.CameraCenter.App.Dialogs;

public partial class AddCameraWindow : Window
{
    private readonly CameraDevice? _source;

    public CameraDevice? Result { get; private set; }
    public string PlainPassword { get; private set; } = "";

    public AddCameraWindow(CameraDevice? source = null, string? currentPassword = null)
    {
        InitializeComponent();
        _source = source;

        KindBox.ItemsSource = Enum.GetValues<DeviceKind>();

        if (source is null)
        {
            KindBox.SelectedItem = DeviceKind.Camera;
            NameBox.Text = "Câmera";
            UserBox.Text = "admin";
            return;
        }

        Title = "Editar dispositivo";
        NameBox.Text = source.Name;
        HostBox.Text = source.Host;
        KindBox.SelectedItem = source.Kind;
        UserBox.Text = source.Username;
        PasswordBox.Password = currentPassword ?? "";
        RtspPortBox.Text = source.RtspPort.ToString();
        HttpPortBox.Text = source.HttpPort.ToString();
        ChannelBox.Text = source.Channel.ToString();
        SubStreamCheck.IsChecked = source.UseSubStream;
        EventsCheck.IsChecked = source.EnableEvents;
        CustomRtspBox.Text = source.CustomRtspUrl;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            MessageBox.Show(this, "Informe o IP ou host do equipamento.", "Intelbras Camera Center",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(RtspPortBox.Text, out var rtspPort) || rtspPort is < 1 or > 65535 ||
            !int.TryParse(HttpPortBox.Text, out var httpPort) || httpPort is < 1 or > 65535 ||
            !int.TryParse(ChannelBox.Text, out var channel) || channel < 1)
        {
            MessageBox.Show(this, "Revise portas e número do canal.", "Intelbras Camera Center",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = new CameraDevice
        {
            Id = _source?.Id ?? Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? HostBox.Text.Trim() : NameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            Kind = KindBox.SelectedItem is DeviceKind kind ? kind : DeviceKind.Camera,
            Username = UserBox.Text.Trim(),
            RtspPort = rtspPort,
            HttpPort = httpPort,
            Channel = channel,
            UseSubStream = SubStreamCheck.IsChecked == true,
            EnableEvents = EventsCheck.IsChecked == true,
            CustomRtspUrl = CustomRtspBox.Text.Trim()
        };

        PlainPassword = PasswordBox.Password;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
