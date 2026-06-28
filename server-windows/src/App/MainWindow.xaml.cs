using System;
using System.Windows;
using System.Windows.Media;
using OpenWirelessDisplay.Core;

namespace OpenWirelessDisplay;

public partial class MainWindow : Window
{
    private StreamServer? _server;

    public MainWindow()
    {
        InitializeComponent();
        LoadMonitors();
    }

    private void LoadMonitors()
    {
        var monitors = ScreenCapturer.ListMonitors();
        MonitorCombo.ItemsSource = monitors;
        MonitorCombo.DisplayMemberPath = nameof(ScreenCapturer.MonitorInfo.Label);
        // Selecciona el principal por defecto.
        int sel = 0;
        for (int i = 0; i < monitors.Count; i++)
            if (monitors[i].Primary) { sel = i; break; }
        MonitorCombo.SelectedIndex = monitors.Count > 0 ? sel : -1;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int screenIndex = (MonitorCombo.SelectedItem is ScreenCapturer.MonitorInfo m) ? m.Index : 0;
            _server = new StreamServer(Environment.MachineName, screenIndex: screenIndex);
            _server.Log += OnLog;
            _server.ClientCountChanged += OnClientCountChanged;
            _server.PinChanged += OnPinChanged;
            _server.Start();

            PinText.Text = _server.CurrentPin;
            StatusDot.Fill = (Brush)FindResource("SuccessBrush");
            StatusText.Text = "Activo";
            InfoText.Text = $"IP: {MdnsResponder.GetLocalIPv4()}    Puerto: {_server.Port}    Clientes: 0";
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            MonitorCombo.IsEnabled = false;
        }
        catch (Exception ex)
        {
            OnLog($"ERROR al iniciar: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _server?.Stop();
        _server = null;
        StatusDot.Fill = (Brush)FindResource("DangerBrush");
        StatusText.Text = "Detenido";
        PinText.Text = "------";
        InfoText.Text = "IP: -    Puerto: -    Clientes: 0";
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        MonitorCombo.IsEnabled = true;
    }

    private void OnPinChanged(string pin) =>
        Dispatcher.Invoke(() => PinText.Text = pin);

    private void OnClientCountChanged(int count) =>
        Dispatcher.Invoke(() =>
        {
            if (_server != null)
                InfoText.Text = $"IP: {MdnsResponder.GetLocalIPv4()}    Puerto: {_server.Port}    Clientes: {count}";
        });

    private void OnLog(string message) =>
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogScroll.ScrollToEnd();
        });

    protected override void OnClosed(EventArgs e)
    {
        _server?.Stop();
        base.OnClosed(e);
    }
}
