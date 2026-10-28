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
    static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(60);

    Mutex? _singleInstance;
    AppSettings _settings = new();
    AirPodsScanner? _scanner;
    readonly BluetoothRadio _radio = new();
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
        _popup = new PopupWindow { ConnectRequested = ConnectAsync, PairRequested = PairAndConnectAsync };
        _tray = new TrayIcon(this);
        _tray.Show();

        _radio.StateChanged += on => Dispatcher.BeginInvoke(() => OnRadioChanged(on));
        if (_settings.ManageBluetooth && !await _radio.EnsureOnAsync())
            _tray.Notify("Bluetooth açılamadı", "Bluetooth'u Windows Ayarlar'dan açın.");

        await RefreshPairedDeviceAsync();
        StartScanner();
    }

    async void OnRadioChanged(bool on)
    {
        if (!on)
            return;
        // The watcher stops for good when the radio goes off; start a fresh one.
        _scanner?.Dispose();
        StartScanner();
        await RefreshPairedDeviceAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scanner?.Dispose();
        if (_settings.ManageBluetooth)
            _radio.Restore();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal AppSettings Settings => _settings;
    internal PairedDevice? Device => _device;
    internal ProximityPairingMessage? LastStatus => _tracker.IsPresent ? _tracker.Current : null;

    internal async Task RefreshPairedDeviceAsync()
    {
        IReadOnlyList<PairedDevice> paired;
        try
        {
            paired = await PairedAirPods.FindAsync();
        }
        catch (Exception)
        {
            paired = Array.Empty<PairedDevice>();
        }
        _device = paired.FirstOrDefault(d => d.Name.Equals(_settings.DeviceName, StringComparison.OrdinalIgnoreCase))
                  ?? paired.FirstOrDefault();
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
            _tray?.Notify("Bluetooth kapalı", "Bluetooth açılınca tarama kendiliğinden yeniden başlar.\n" + ex.Message);
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
                if (_device is null)
                    await RefreshPairedDeviceAsync();
                if (_device is null)
                {
                    _popup.ShowCard(Title(message), message, ConnectionState.NotPaired);
                    break;
                }
                bool connected = await IsConnectedAsync();
                _popup.ShowCard(Title(message), message, connected ? ConnectionState.Connected : ConnectionState.Disconnected);
                if (!connected && _settings.AutoConnectOnLidOpen)
                    await ConnectFromPopupAsync();
                break;

            case TrackerEvent.PutInEar when _settings.AutoConnectOnInEar && _device is not null:
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
        _popup!.ShowCard(Title(message), message, _device is null ? ConnectionState.NotPaired : ConnectionState.Disconnected);
    }

    async Task ConnectFromPopupAsync()
    {
        _popup!.SetState(ConnectionState.Connecting);
        bool ok;
        try
        {
            ok = await ConnectAsync();
        }
        catch (Exception)
        {
            ok = false;
        }
        _popup.SetState(ok ? ConnectionState.Connected : ConnectionState.Failed);
    }

    /// <summary>First-time setup from the card: pair with Windows, then route audio to the AirPods.</summary>
    internal async Task<bool> PairAndConnectAsync()
    {
        var paired = await AirPodsPairing.PairAsync(PairTimeout);
        if (paired is null)
            return false;

        _settings.DeviceName = paired.Name;
        _settings.Save();
        await RefreshPairedDeviceAsync();
        // Windows normally connects right after pairing; this also makes it the default output.
        return await ConnectAsync();
    }

    internal async Task<bool> ConnectAsync()
    {
        if (_device is null)
            await RefreshPairedDeviceAsync();
        if (_device is null)
        {
            _tray?.Notify("AirPods eşleşmemiş", "Kutunun kapağını aç ve kartta \"Windows ile eşleştir\"e bas.");
            return false;
        }
        if (_connecting)
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
