using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Intelbras.CameraCenter.App.Controls;
using Intelbras.CameraCenter.App.Dialogs;
using Intelbras.CameraCenter.App.Models;
using Intelbras.CameraCenter.App.Services;

namespace Intelbras.CameraCenter.App;

public partial class MainWindow : Window
{
    private readonly CameraRepository _repository;
    private readonly ObservableCollection<CameraEvent> _events = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _eventMonitors = new();

    public MainWindow(CameraRepository repository)
    {
        InitializeComponent();

        _repository = repository;
        DevicesGrid.ItemsSource = _repository.Devices;
        EventsGrid.ItemsSource = _events;

        RecordingsPathText.Text = AppPaths.Recordings;
        DataPathText.Text = AppPaths.Root;

        _repository.Devices.CollectionChanged += DevicesCollectionChanged;

        RebuildLiveGrid();
        StartEventMonitors();
        UpdateStatus();
    }

    private void DevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLiveGrid();
        RestartEventMonitors();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var count = _repository.Devices.Count;
        DeviceCountText.Text = string.Concat(count, count == 1 ? " dispositivo" : " dispositivos");
    }

    private void RebuildLiveGrid()
    {
        foreach (var child in LivePanel.Children.OfType<CameraTile>().ToList())
            child.Dispose();

        LivePanel.Children.Clear();

        foreach (var device in _repository.Devices)
            LivePanel.Children.Add(new CameraTile(_repository, device));

        if (_repository.Devices.Count == 0)
        {
            LivePanel.Children.Add(new TextBlock
            {
                Text = "Nenhuma câmera cadastrada. Use “Adicionar câmera” ou “Descobrir ONVIF”.",
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                FontSize = 15,
                Margin = new Thickness(8, 24, 0, 0)
            });
        }
    }

    private void NavClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page)
            return;

        LivePage.Visibility = page == "Live" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = page == "Devices" ? Visibility.Visible : Visibility.Collapsed;
        EventsPage.Visibility = page == "Events" ? Visibility.Visible : Visibility.Collapsed;
        RecordingsPage.Visibility = page == "Recordings" ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsPage.Visibility = page == "Analytics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddCameraClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCameraWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        _repository.SetPassword(dialog.Result, dialog.PlainPassword);
        _repository.Devices.Add(dialog.Result);
        await _repository.SaveAsync();
    }

    private async void EditCameraClick(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not CameraDevice source)
            return;

        var dialog = new AddCameraWindow(source, _repository.GetPassword(source)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        var edited = dialog.Result;
        source.Name = edited.Name;
        source.Host = edited.Host;
        source.Kind = edited.Kind;
        source.Username = edited.Username;
        source.RtspPort = edited.RtspPort;
        source.HttpPort = edited.HttpPort;
        source.Channel = edited.Channel;
        source.UseSubStream = edited.UseSubStream;
        source.EnableEvents = edited.EnableEvents;
        source.CustomRtspUrl = edited.CustomRtspUrl;
        _repository.SetPassword(source, dialog.PlainPassword);

        await _repository.SaveAsync();
        RebuildLiveGrid();
        RestartEventMonitors();
    }

    private async void RemoveCameraClick(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not CameraDevice selected)
            return;

        var confirmation = MessageBox.Show(
            this,
            string.Concat("Remover “", selected.Name, "”?"),
            "Intelbras Camera Center",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _repository.Devices.Remove(selected);
        await _repository.SaveAsync();
    }

    private async void DiscoverClick(object sender, RoutedEventArgs e)
    {
        DiscoverButton.IsEnabled = false;
        GlobalStatusText.Text = "Procurando dispositivos ONVIF...";

        try
        {
            var discovered = await new WsDiscoveryService().DiscoverAsync(TimeSpan.FromSeconds(4));
            var added = 0;

            foreach (var item in discovered)
            {
                if (_repository.Devices.Any(x => x.Host.Equals(item.Host, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _repository.Devices.Add(new CameraDevice
                {
                    Name = string.Concat("ONVIF ", item.Host),
                    Host = item.Host,
                    Kind = DeviceKind.Camera,
                    EnableEvents = false
                });
                added++;
            }

            if (added > 0)
                await _repository.SaveAsync();

            GlobalStatusText.Text = string.Concat(discovered.Count, " encontrados • ", added, " adicionados");
        }
        catch (Exception ex)
        {
            GlobalStatusText.Text = "Falha na descoberta ONVIF";
            MessageBox.Show(this, ex.Message, "Descoberta ONVIF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            DiscoverButton.IsEnabled = true;
        }
    }

    private async void DeviceInfoClick(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not CameraDevice device)
            return;

        try
        {
            GlobalStatusText.Text = "Consultando CGI...";
            using var api = new IntelbrasCgiClient(device, _repository.GetPassword(device));
            var info = await api.GetDeviceInfoAsync();

            MessageBox.Show(this, info, string.Concat("CGI • ", device.Name),
                MessageBoxButton.OK, MessageBoxImage.Information);
            GlobalStatusText.Text = "CGI conectado";
        }
        catch (Exception ex)
        {
            GlobalStatusText.Text = "CGI indisponível";
            MessageBox.Show(this,
                string.Concat("O equipamento não respondeu à API CGI compatível.\n\n", ex.Message),
                "Intelbras Camera Center",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void StartEventMonitors()
    {
        foreach (var device in _repository.Devices.Where(x => x.EnableEvents))
            StartEventMonitor(device);
    }

    private void RestartEventMonitors()
    {
        foreach (var cts in _eventMonitors.Values)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        _eventMonitors.Clear();
        StartEventMonitors();
    }

    private void StartEventMonitor(CameraDevice device)
    {
        if (_eventMonitors.ContainsKey(device.Id) || string.IsNullOrWhiteSpace(device.Host))
            return;

        var cts = new CancellationTokenSource();
        _eventMonitors[device.Id] = cts;
        var password = _repository.GetPassword(device);
        var monitor = new EventMonitorService();

        _ = Task.Run(() => monitor.RunAsync(device, password, evt =>
        {
            Dispatcher.Invoke(() =>
            {
                _events.Insert(0, evt);
                while (_events.Count > 2000)
                    _events.RemoveAt(_events.Count - 1);
            });
        }, cts.Token), cts.Token);
    }

    private void OpenRecordingsClick(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(AppPaths.Recordings) { UseShellExecute = true });
    }

    private void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _repository.Devices.CollectionChanged -= DevicesCollectionChanged;

        foreach (var cts in _eventMonitors.Values)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        foreach (var child in LivePanel.Children.OfType<CameraTile>().ToList())
            child.Dispose();
    }
}
