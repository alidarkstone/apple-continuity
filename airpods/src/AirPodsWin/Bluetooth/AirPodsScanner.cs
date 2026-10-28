using System.Runtime.InteropServices.WindowsRuntime;
using AirPodsWin.Core;
using Windows.Devices.Bluetooth.Advertisement;

namespace AirPodsWin.Bluetooth;

/// <summary>
/// Passively listens for Apple proximity pairing advertisements.
/// Passive scanning needs no pairing and no special permission.
/// </summary>
public sealed class AirPodsScanner : IDisposable
{
    readonly BluetoothLEAdvertisementWatcher _watcher;

    public event Action<ProximityPairingMessage, short>? MessageReceived;
    public event Action<BluetoothLEAdvertisementWatcherStatus>? Stopped;

    public AirPodsScanner()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Passive,
        };
        _watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(
            new BluetoothLEManufacturerData { CompanyId = ProximityPairingMessage.AppleCompanyId });
        _watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.Zero;
        _watcher.Received += OnReceived;
        _watcher.Stopped += (_, e) => Stopped?.Invoke(_watcher.Status);
    }

    public BluetoothLEAdvertisementWatcherStatus Status => _watcher.Status;

    public void Start() => _watcher.Start();
    public void Stop() => _watcher.Stop();

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var section in args.Advertisement.GetManufacturerDataByCompanyId(ProximityPairingMessage.AppleCompanyId))
        {
            var message = ProximityPairingMessage.TryParse(section.Data.ToArray());
            if (message is not null)
                MessageReceived?.Invoke(message, args.RawSignalStrengthInDBm);
        }
    }

    public void Dispose()
    {
        _watcher.Received -= OnReceived;
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            _watcher.Stop();
    }
}
