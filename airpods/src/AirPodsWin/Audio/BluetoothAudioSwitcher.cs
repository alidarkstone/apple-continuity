using System.Runtime.InteropServices;

namespace AirPodsWin.Audio;

public sealed record AudioEndpoint(string Id, string Name, bool IsActive);

/// <summary>
/// Connects / disconnects a paired Bluetooth headset and makes it the default
/// output, the way the Windows "Connect" button in Settings does it: by sending
/// the Bluetooth audio driver's one-shot reconnect property to the KS filter
/// behind the audio endpoint. Works for already-paired devices only.
/// </summary>
public static class BluetoothAudioSwitcher
{
    static readonly Guid KsPropSetIdBtAudio = new("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d");
    const uint KsPropertyOneshotReconnect = 0;
    const uint KsPropertyOneshotDisconnect = 1;
    const uint KsPropertyTypeGet = 0x1;
    const uint ClsCtxAll = 0x17;

    static readonly Guid IidDeviceTopology = typeof(IDeviceTopology).GUID;
    static readonly Guid IidKsControl = typeof(IKsControl).GUID;

    /// <summary>Render endpoints whose name contains the Bluetooth device name, connected or not.</summary>
    public static IReadOnlyList<AudioEndpoint> FindEndpoints(string bluetoothName) =>
        WithEndpoints(bluetoothName, (_, e) => e).ToList();

    public static Task<bool> ConnectAsync(string bluetoothName, TimeSpan timeout) => Task.Run(async () =>
    {
        // Right after pairing the endpoints take a while to appear (driver install), so keep
        // looking, and re-send the reconnect every few seconds until one becomes active.
        var deadline = DateTime.UtcNow + timeout;
        var lastSend = DateTime.MinValue;
        while (DateTime.UtcNow < deadline)
        {
            var endpoints = FindEndpoints(bluetoothName);
            var stereo = PreferStereo(endpoints.Where(e => e.IsActive));
            if (stereo is not null)
            {
                SetDefault(stereo.Id);
                return true;
            }
            if (endpoints.Count > 0 && DateTime.UtcNow - lastSend > TimeSpan.FromSeconds(3))
            {
                WithEndpoints(bluetoothName, (device, _) => SendOneShot(device, KsPropertyOneshotReconnect));
                lastSend = DateTime.UtcNow;
            }
            await Task.Delay(250);
        }
        return false;
    });

    public static Task<bool> DisconnectAsync(string bluetoothName) => Task.Run(() =>
        WithEndpoints(bluetoothName, (device, e) => e.IsActive && SendOneShot(device, KsPropertyOneshotDisconnect))
            .ToList()
            .Any(ok => ok));

    public static void SetDefault(string endpointId)
    {
        var policy = (IPolicyConfig)new PolicyConfigComObject();
        try
        {
            policy.SetDefaultEndpoint(endpointId, ERole.Console);
            policy.SetDefaultEndpoint(endpointId, ERole.Multimedia);
            policy.SetDefaultEndpoint(endpointId, ERole.Communications);
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>The A2DP "Headphones" endpoint sounds far better than the Hands-Free one.</summary>
    static AudioEndpoint? PreferStereo(IEnumerable<AudioEndpoint> endpoints)
    {
        var list = endpoints.ToList();
        return list.FirstOrDefault(e => !e.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) &&
                                        !e.Name.Contains("Eller Serbest", StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault();
    }

    static IEnumerable<T> WithEndpoints<T>(string bluetoothName, Func<IMMDevice, AudioEndpoint, T> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active | DeviceState.Unplugged, out var collection);
        collection.GetCount(out uint count);

        var results = new List<T>();
        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            try
            {
                device.GetId(out string id);
                device.GetState(out uint state);
                string name = FriendlyName(device);
                if (name.Contains(bluetoothName, StringComparison.OrdinalIgnoreCase))
                    results.Add(action(device, new AudioEndpoint(id, name, state == DeviceState.Active)));
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        Marshal.ReleaseComObject(collection);
        Marshal.ReleaseComObject(enumerator);
        return results;
    }

    static string FriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0 /* STGM_READ */, out var store);
        try
        {
            var key = PropertyKey.DeviceFriendlyName;
            store.GetValue(ref key, out var value);
            try
            {
                return value.VarType == PropVariant.VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
            }
            finally
            {
                NativeMethods.PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>
    /// Endpoint -> its topology -> connector -> the KS filter connector it is wired to ->
    /// that part's IKsControl, then send the Bluetooth audio one-shot property.
    /// </summary>
    static bool SendOneShot(IMMDevice device, uint propertyId)
    {
        object? topologyObj = null, ksObj = null;
        IConnector? connector = null, connectedTo = null;
        try
        {
            var iid = IidDeviceTopology;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out topologyObj);
            var topology = (IDeviceTopology)topologyObj;
            topology.GetConnector(0, out connector);
            connector.GetConnectedTo(out connectedTo);

            var part = (IPart)connectedTo;
            var ksIid = IidKsControl;
            part.Activate(ClsCtxAll, ref ksIid, out ksObj);
            var ks = (IKsControl)ksObj;

            var property = new KsProperty { Set = KsPropSetIdBtAudio, Id = propertyId, Flags = KsPropertyTypeGet };
            int hr = ks.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
            return hr >= 0;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            Release(ksObj);
            Release(connectedTo);
            Release(connector);
            Release(topologyObj);
        }
    }

    static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o))
            Marshal.ReleaseComObject(o);
    }
}
