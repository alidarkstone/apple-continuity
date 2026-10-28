using System.Diagnostics;
using System.Windows;
using AirPodsWin.Audio;
using AirPodsWin.Bluetooth;
using AirPodsWin.Core;
using AirPodsWin.UI;

namespace AirPodsWin;

public partial class App : Application
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan AutoConnectCooldown = TimeSpan.FromSeconds(30);

    Mutex? _singleInstance;
    AppSettings _settings = new();
    AirPodsScanner? _scanner;
    AirPodsTracker _tracker = new();
    PopupWindow? _popup;
    TrayIcon? _tray;
    PairedDevice? _device;
    DateTimeOffset _lastAutoConnect = DateTimeOffset.MinValue;
    bool _connecting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "AirPodsWin.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        _popup = new PopupWindow { ConnectRequested = ConnectAsync };
        _tray = new TrayIcon(this);
        _tray.Show();

        await RefreshPairedDeviceAsync();
        StartScanner();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scanner?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal AppSettings Settings => _settings;
    internal PairedDevice? Device => _device;
    internal ProximityPairingMessage? LastStatus => _tracker.IsPresent ? _tracker.Current : null;

    internal async Task RefreshPairedDeviceAsync()
    {
        var paired = await PairedAirPods.FindAsync();
        _device = paired.FirstOrDefault(d => d.Name.Equals(_settings.DeviceName, StringComparison.OrdinalIgnoreCase))
                  ?? paired.FirstOrDefault();

        if (_device is null)
        {
            _tray?.Notify("AirPods bulunamadı",
                "AirPods'u bir kez Windows Ayarlar > Bluetooth üzerinden eşleştirin. Sonrası otomatik.");
        }
        _tray?.Refresh();
    }

    void StartScanner()
    {
        _tracker = new AirPodsTracker(new TrackerOptions { MinRssi = _settings.MinRssi });
        _scanner = new AirPodsScanner();
        _scanner.MessageReceived += (message, rssi) => Dispatcher.BeginInvoke(() => OnAdvertisement(message, rssi));
        try
        {
            _scanner.Start();
        }
        catch (Exception ex)
        {
            _tray?.Notify("Bluetooth kapalı", "Bluetooth açılınca uygulamayı yeniden başlatın.\n" + ex.Message);
        }
    }

    internal void ApplySettings()
    {
        _settings.Save();
        _scanner?.Dispose();
        StartScanner();
        _tray?.Refresh();
    }

    async void OnAdvertisement(ProximityPairingMessage message, short rssi)
    {
        var evt = _tracker.Process(message, rssi);
        if (evt == TrackerEvent.None)
            return;

        _tray?.Refresh();
        if (_popup!.IsShowing)
            _popup.Update(message);

        switch (evt)
        {
            case TrackerEvent.LidOpened when _settings.ShowPopupOnLidOpen:
                bool connected = await IsConnectedAsync();
                _popup.ShowCard(Title(message), message, connected ? ConnectionState.Connected : ConnectionState.Disconnected);
                if (!connected && _settings.AutoConnectOnLidOpen)
                    await ConnectFromPopupAsync();
                break;

            case TrackerEvent.PutInEar when _settings.AutoConnectOnInEar:
                if (DateTimeOffset.UtcNow - _lastAutoConnect < AutoConnectCooldown || await IsConnectedAsync())
                    break;
                _lastAutoConnect = DateTimeOffset.UtcNow;
                _popup.ShowCard(Title(message), message, ConnectionState.Connecting);
                await ConnectFromPopupAsync();
                break;
        }
    }

    internal void ShowCardNow()
    {
        var message = _tracker.Current ?? new ProximityPairingMessage { Model = AirPodsModel.AirPodsPro2 };
        _popup!.ShowCard(Title(message), message, ConnectionState.Disconnected);
    }

    async Task ConnectFromPopupAsync()
    {
        _popup!.SetState(ConnectionState.Connecting);
        bool ok = await ConnectAsync();
        _popup.SetState(ok ? ConnectionState.Connected : ConnectionState.Failed);
    }

    internal async Task<bool> ConnectAsync()
    {
        if (_device is null)
            await RefreshPairedDeviceAsync();
        if (_device is null || _connecting)
            return false;

        _connecting = true;
        try
        {
            bool ok = await BluetoothAudioSwitcher.ConnectAsync(_device.Name, ConnectTimeout);
            if (!ok)
                _tray?.Notify("Bağlanamadı", $"{_device.Name} bağlanamadı. Kutu açık ve yakında mı?");
            return ok;
        }
        finally
        {
            _connecting = false;
            _tray?.Refresh();
        }
    }

    internal async Task DisconnectAsync()
    {
        if (_device is not null)
            await BluetoothAudioSwitcher.DisconnectAsync(_device.Name);
        _tray?.Refresh();
    }

    async Task<bool> IsConnectedAsync()
    {
        if (_device is null)
            return false;
        try
        {
            return await PairedAirPods.IsConnectedAsync(_device.Id);
        }
        catch
        {
            return false;
        }
    }

    string Title(ProximityPairingMessage message) => _device?.Name ?? message.Model.DisplayName();

    internal static void OpenBluetoothSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
}
