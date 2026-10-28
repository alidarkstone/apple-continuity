// Süreklilik Bluetooth köprüsü: iPhone'a MAP (Message Access Profile) ile bağlanır.
// Electron ile stdin/stdout üzerinden satır başına bir JSON konuşur:
//   istek  {"id":1,"cmd":"devices"} | {"id":2,"cmd":"connect","device":"..."} | {"id":3,"cmd":"list","folder":"inbox","max":50}
//          {"id":4,"cmd":"send","to":"+90...","text":"..."}
//   yanıt  {"id":1,"ok":true,"result":...} | {"id":1,"ok":false,"error":"..."}
//   olay   {"event":"status","connected":true} | {"event":"message","message":{...}}
using System.Text.Json;
using System.Text.Json.Nodes;
using MapBridge.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MapBridge;

public static class Program
{
    private const ushort MasShortId = 0x1132, MnsShortId = 0x1133;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Out = new(1, 1);

    private static StreamSocket? _masSocket;
    private static MapSession? _map;
    private static RfcommServiceProvider? _mnsProvider;
    private static StreamSocketListener? _mnsListener;

    public static async Task Main()
    {
        Console.InputEncoding = Console.OutputEncoding = System.Text.Encoding.UTF8;
        string? line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            _ = HandleAsync(line);
        }
        await ShutdownAsync();
    }

    private static async Task HandleAsync(string line)
    {
        JsonNode? id = null;
        try
        {
            var req = JsonNode.Parse(line)!.AsObject();
            id = req["id"]?.DeepClone();
            object? result = (string?)req["cmd"] switch
            {
                "devices" => await DevicesAsync(),
                "connect" => await ConnectAsync((string)req["device"]!),
                "list" => (await Map().ListAsync((string?)req["folder"] ?? "inbox", (int?)req["max"] ?? 50)).Select(ToJson).ToList(),
                "send" => await SendAsync((string)req["to"]!, (string)req["text"]!),
                "disconnect" => await DisconnectAsync(),
                var c => throw new InvalidOperationException($"Bilinmeyen komut: {c}"),
            };
            await WriteAsync(new { id, ok = true, result });
        }
        catch (Exception e)
        {
            await WriteAsync(new { id, ok = false, error = e.Message });
        }
    }

    private static MapSession Map() => _map ?? throw new InvalidOperationException("iPhone'a bağlı değil");

    private static async Task<object> DevicesAsync()
    {
        var selector = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromShortId(MasShortId));
        var services = await DeviceInformation.FindAllAsync(selector);
        var list = new List<object>();
        foreach (var s in services)
        {
            var svc = await RfcommDeviceService.FromIdAsync(s.Id);
            list.Add(new { id = s.Id, name = svc?.Device?.Name ?? s.Name });
            svc?.Dispose();
        }
        return list;
    }

    private static async Task<object> ConnectAsync(string deviceServiceId)
    {
        await DisconnectAsync();
        var svc = await RfcommDeviceService.FromIdAsync(deviceServiceId)
                  ?? throw new InvalidOperationException("iPhone bulunamadı. Bluetooth ile eşleştirildiğinden emin ol.");
        var access = await svc.RequestAccessAsync();
        if (access != DeviceAccessStatus.Allowed) throw new InvalidOperationException("Bluetooth erişimi reddedildi");

        _masSocket = new StreamSocket();
        await _masSocket.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
        var obex = new ObexClient(_masSocket.InputStream.AsStreamForRead(0), _masSocket.OutputStream.AsStreamForWrite(0));
        _map = new MapSession(obex);
        await _map.OpenAsync();

        await StartMnsAsync();
        try { await _map.SetNotificationsAsync(true); }
        catch (Exception e) { await WriteAsync(new { @event = "warning", message = "Anlık bildirim açılamadı: " + e.Message }); }

        var name = svc.Device?.Name ?? "iPhone";
        svc.Device!.ConnectionStatusChanged += async (d, _) =>
        {
            if (d.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                await DisconnectAsync();
                await WriteAsync(new { @event = "status", connected = false });
            }
        };
        await WriteAsync(new { @event = "status", connected = true, device = name });
        return new { device = name };
    }

    // iPhone yeni mesajları MNS üzerinden bize bildirir: burada RFCOMM sunucusu açıp yayınlarız.
    private static async Task StartMnsAsync()
    {
        if (_mnsProvider != null) return;
        _mnsProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(MnsShortId));
        _mnsListener = new StreamSocketListener();
        _mnsListener.ConnectionReceived += (listener, args) => _ = ServeMnsAsync(args.Socket);
        await _mnsListener.BindServiceNameAsync(_mnsProvider.ServiceId.AsString(),
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

        // SDP: Hizmet adı ve MAP profil tanımlayıcısı (0x1134, sürüm 1.2).
        var writer = new DataWriter();
        writer.WriteBytes(new byte[] { 0x35, 0x08, 0x35, 0x06, 0x19, 0x11, 0x34, 0x09, 0x01, 0x02 });
        _mnsProvider.SdpRawAttributes.Add(0x0009, writer.DetachBuffer());
        writer = new DataWriter();
        var name = System.Text.Encoding.UTF8.GetBytes("MAP MNS-Sureklilik");
        writer.WriteByte(0x25);
        writer.WriteByte((byte)name.Length);
        writer.WriteBytes(name);
        _mnsProvider.SdpRawAttributes.Add(0x0100, writer.DetachBuffer());
        _mnsProvider.StartAdvertising(_mnsListener, true);
    }

    private static async Task ServeMnsAsync(StreamSocket socket)
    {
        using (socket)
        {
            var server = new ObexServer(socket.InputStream.AsStreamForRead(0), socket.OutputStream.AsStreamForWrite(0), MapUuids.Mns);
            server.Put += (type, body) => _ = OnEventAsync(type, body);
            try { await server.RunAsync(); } catch { /* iPhone bağlantıyı kapattı */ }
        }
    }

    private static async Task OnEventAsync(string type, byte[] body)
    {
        try
        {
            if (!type.StartsWith("x-bt/MAP-event-report")) return;
            var ev = MapFormat.ParseEvent(body);
            if (ev is not { Type: "NewMessage", Handle: { } handle } || _map == null) return;
            var folder = ev.Folder?.Split('/').Last() ?? "inbox";
            var m = await _map.GetAsync(handle, folder);
            await WriteAsync(new { @event = "message", message = ToJson(m) });
        }
        catch (Exception e)
        {
            await WriteAsync(new { @event = "warning", message = "Bildirim işlenemedi: " + e.Message });
        }
    }

    private static async Task<object> SendAsync(string to, string text)
    {
        await Map().PushAsync(to, text);
        return new { sent = true };
    }

    private static async Task<object?> DisconnectAsync()
    {
        if (_map != null) { try { await _map.CloseAsync(); } catch { } _map = null; }
        _masSocket?.Dispose();
        _masSocket = null;
        return null;
    }

    private static async Task ShutdownAsync()
    {
        await DisconnectAsync();
        _mnsProvider?.StopAdvertising();
        _mnsListener?.Dispose();
    }

    private static object ToJson(MapMessage m) => new
    {
        id = m.Handle,
        chatId = m.Peer,
        text = m.Text,
        date = m.Date.ToUnixTimeMilliseconds(),
        fromMe = m.FromMe,
        sender = m.FromMe ? null : (m.SenderName ?? m.SenderAddress),
        senderAddress = m.SenderAddress,
        recipientAddress = m.RecipientAddress,
        read = m.Read,
    };

    private static async Task WriteAsync(object o)
    {
        var s = JsonSerializer.Serialize(o, Json);
        await Out.WaitAsync();
        try { await Console.Out.WriteLineAsync(s); await Console.Out.FlushAsync(); }
        finally { Out.Release(); }
    }
}
