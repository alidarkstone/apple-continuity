using System.Runtime.InteropServices;

namespace AirPodsWin.Audio;

// Minimal Core Audio / Device Topology / KS COM definitions.
// Vtable order must match the Windows SDK headers exactly.

enum EDataFlow { Render = 0, Capture = 1, All = 2 }
enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

static class DeviceState
{
    public const uint Active = 0x1;
    public const uint Unplugged = 0x8;
}

[StructLayout(LayoutKind.Sequential)]
struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(Guid formatId, int propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    public static readonly PropertyKey DeviceFriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public IntPtr Pointer;

    public const ushort VT_LPWSTR = 31;
}

[StructLayout(LayoutKind.Sequential)]
struct KsProperty
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

static class NativeMethods
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IntPtr client);
    void UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(uint access, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out uint state);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeviceTopology
{
    void GetConnectorCount(out uint count);
    void GetConnector(uint index, out IConnector connector);
    void GetSubunitCount(out uint count);
    void GetSubunit(uint index, out IntPtr subunit);
    void GetPartById(uint id, out IntPtr part);
    void GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
    void GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
}

[ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IConnector
{
    void GetConnectorType(out int type);
    void GetDataFlow(out int flow);
    void ConnectTo(IConnector connectTo);
    void Disconnect();
    void IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
    void GetConnectedTo(out IConnector connectedTo);
    void GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPart
{
    void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetLocalId(out uint id);
    void GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetPartType(out int type);
    void GetSubType(out Guid subType);
    void GetControlInterfaceCount(out uint count);
    void GetControlInterface(uint index, out IntPtr controlInterface);
    void EnumPartsIncoming(out IntPtr parts);
    void EnumPartsOutgoing(out IntPtr parts);
    void GetTopologyObject(out IntPtr topology);
    void Activate(uint clsContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void RegisterControlChangeCallback(ref Guid iid, IntPtr notify);
    void UnregisterControlChangeCallback(IntPtr notify);
}

[ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IKsControl
{
    [PreserveSig]
    int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
    [PreserveSig]
    int KsMethod(IntPtr method, uint methodLength, IntPtr data, uint dataLength, out uint bytesReturned);
    [PreserveSig]
    int KsEvent(IntPtr ksEvent, uint eventLength, IntPtr data, uint dataLength, out uint bytesReturned);
}

/// <summary>Undocumented but stable since Windows 7; used by every "switch default device" tool.</summary>
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
class PolicyConfigComObject { }

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    void GetMixFormat(IntPtr a, IntPtr b);
    void GetDeviceFormat(IntPtr a, int b, IntPtr c);
    void ResetDeviceFormat(IntPtr a);
    void SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
    void GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
    void SetProcessingPeriod(IntPtr a, IntPtr b);
    void GetShareMode(IntPtr a, IntPtr b);
    void SetShareMode(IntPtr a, IntPtr b);
    void GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    void SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}
