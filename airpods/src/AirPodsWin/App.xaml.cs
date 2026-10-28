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
    static readonly TimeSpan PairConnectTimeout = TimeSpan.FromSeconds(45);

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
            paired = await PairedAirPods.FindAsync(_settings.DeviceId);
        }
        catch (Exception ex)
        {
            Log.Write("Paired device lookup failed: " + ex.Message);
            paired = Array.Empty<PairedDevice>();
        }
        _device = paired.FirstOrDefault();
        Log.Write($"Paired AirPods: {(_device is null ? "none" : _device.Name)} (of {paired.Count})");
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
        Log.Write("Pairing started");
        var deadline = DateTime.UtcNow + PairTimeout;
        using var cancel = new CancellationTokenSource(PairTimeout);
        using var pairing = new AirPodsPairing();
        pairing.CandidatesChanged += () => Dispatcher.BeginInvoke(() => _popup!.ShowChoices(pairing.Candidates));
        pairing.Start();

        // Give the scan a moment to find an Apple audio device (identified by address, not name).
        // Prefer one that is present right now; several could be remembered from earlier scans.
        string? id = null;
        for (var until = DateTime.UtcNow.AddSeconds(4); DateTime.UtcNow < until && id is null; await Task.Delay(250))
        {
            var apple = pairing.Candidates.Where(c => c.IsAppleAudio).ToList();
            id = (apple.Count == 1 ? apple[0] : apple.FirstOrDefault(c => c.IsPresent))?.Id;
        }

        if (id is null)
        {
            // Never guess: let the user say which nearby device is theirs.
            id = await _popup!.ChooseDeviceAsync(pairing.Candidates);
            if (id is null)
            {
                Log.Write("Pairing: cancelled by user");
                return false;
            }
            _popup.SetState(ConnectionState.Pairing);
        }

        var paired = await pairing.PairAsync(id, deadline, cancel.Token);
        if (paired is null)
            return false;

        _settings.DeviceId = paired.Id;
        _settings.Save();
        await RefreshPairedDeviceAsync();
        // The audio endpoints appear some seconds after pairing; wait for them, connect and make them the default output.
        return await ConnectAsync(PairConnectTimeout);
    }

    internal Task<bool> ConnectAsync() => ConnectAsync(ConnectTimeout);

    async Task<bool> ConnectAsync(TimeSpan timeout)
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
            var endpoints = BluetoothAudioSwitcher.FindEndpoints(_device.Name);
            Log.Write($"Connect '{_device.Name}': endpoints [{string.Join(", ", endpoints.Select(e => $"{e.Name} active={e.IsActive}"))}]");
            bool ok = await BluetoothAudioSwitcher.ConnectAsync(_device.Name, timeout);
            Log.Write($"Connect '{_device.Name}': {(ok ? "ok" : "failed")}");
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
