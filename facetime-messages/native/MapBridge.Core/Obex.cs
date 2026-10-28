// OBEX (IrOBEX 1.5) paket çerçeveleme, istemci ve basit sunucu.
// Bluetooth MAP, RFCOMM üzerinde bu protokolü konuşur. Akış tabanlıdır; testte bellek akışlarıyla çalışır.
using System.Buffers.Binary;
using System.Text;

namespace MapBridge.Core;

public static class Op
{
    public const byte Connect = 0x80, Disconnect = 0x81, Put = 0x02, PutFinal = 0x82, GetFinal = 0x83, SetPath = 0x85;
    public const byte Continue = 0x90, Success = 0xA0;
}

public static class Hi
{
    public const byte Name = 0x01, Type = 0x42, Target = 0x46, Who = 0x4A, Body = 0x48, EndOfBody = 0x49,
        AppParams = 0x4C, ConnectionId = 0xCB, Length = 0xC3;
}

public sealed class ObexPacket
{
    public byte Code;
    public byte[] Prefix = Array.Empty<byte>();
    public readonly List<(byte Id, byte[] Value)> Headers = new();

    public ObexPacket(byte code, params byte[] prefix) { Code = code; Prefix = prefix; }

    public ObexPacket Add(byte id, byte[] value) { Headers.Add((id, value)); return this; }

    // Unicode başlıklar UTF-16BE + iki baytlık sonlandırıcıdır; boş ad (SetPath kök) değer taşımaz.
    public ObexPacket AddName(string name) =>
        Add(Hi.Name, name.Length == 0 ? Array.Empty<byte>() : Encoding.BigEndianUnicode.GetBytes(name + "\0"));

    public ObexPacket AddType(string type) => Add(Hi.Type, Encoding.ASCII.GetBytes(type + "\0"));

    public ObexPacket AddUInt(byte id, uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        return Add(id, b);
    }

    public byte[]? Get(byte id) => Headers.FirstOrDefault(h => h.Id == id).Value;

    public uint? GetUInt(byte id) => Get(id) is { Length: 4 } b ? BinaryPrimitives.ReadUInt32BigEndian(b) : null;

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        ms.WriteByte(Code);
        ms.Write(new byte[2]);
        ms.Write(Prefix);
        foreach (var (id, value) in Headers)
        {
            ms.WriteByte(id);
            switch (id & 0xC0)
            {
                case 0x00:
                case 0x40:
                    var len = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)(value.Length + 3));
                    ms.Write(len);
                    ms.Write(value);
                    break;
                case 0x80: ms.WriteByte(value[0]); break;
                default: ms.Write(value, 0, 4); break;
            }
        }
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(1), (ushort)bytes.Length);
        return bytes;
    }

    public static ObexPacket Decode(byte[] data, int prefixLength)
    {
        var p = new ObexPacket(data[0], data[3..(3 + prefixLength)]);
        var i = 3 + prefixLength;
        while (i < data.Length)
        {
            var id = data[i++];
            switch (id & 0xC0)
            {
                case 0x00:
                case 0x40:
                    var len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i));
                    p.Add(id, data[(i + 2)..(i + len - 1)]);
                    i += len - 1;
                    break;
                case 0x80: p.Add(id, new[] { data[i] }); i += 1; break;
                default: p.Add(id, data[i..(i + 4)]); i += 4; break;
            }
        }
        return p;
    }

    public static async Task<byte[]> ReadRawAsync(Stream s, CancellationToken ct = default)
    {
        var head = new byte[3];
        await s.ReadExactlyAsync(head, ct);
        var len = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(1));
        var data = new byte[len];
        head.CopyTo(data, 0);
        await s.ReadExactlyAsync(data.AsMemory(3), ct);
        return data;
    }

    // İstek tarafında önek uzunluğu işlem koduna bağlıdır.
    public static int RequestPrefixLength(byte code) => code switch { Op.Connect => 4, Op.SetPath => 2, _ => 0 };
}

public static class AppParams
{
    public const byte MaxListCount = 0x01, Attachment = 0x0A, NotificationStatus = 0x0E, Charset = 0x14;

    public static byte[] Build(params (byte Tag, byte[] Value)[] items)
    {
        using var ms = new MemoryStream();
        foreach (var (tag, value) in items) { ms.WriteByte(tag); ms.WriteByte((byte)value.Length); ms.Write(value); }
        return ms.ToArray();
    }

    public static byte[] U8(byte v) => new[] { v };
    public static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
}

public class ObexException(string message) : Exception(message);

public sealed class ObexClient(Stream input, Stream output)
{
    private uint? _connectionId;
    private int _maxPacket = 0x1000;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private async Task<ObexPacket> SendAsync(ObexPacket p, int responsePrefix = 0)
    {
        await output.WriteAsync(p.Encode());
        await output.FlushAsync();
        return ObexPacket.Decode(await ObexPacket.ReadRawAsync(input), responsePrefix);
    }

    private ObexPacket New(byte code, params byte[] prefix)
    {
        var p = new ObexPacket(code, prefix);
        if (_connectionId is { } id) p.AddUInt(Hi.ConnectionId, id);
        return p;
    }

    private static void Expect(ObexPacket r, string what, params byte[] ok)
    {
        if (!ok.Contains(r.Code)) throw new ObexException($"{what} başarısız (0x{r.Code:X2})");
    }

    public async Task ConnectAsync(byte[] target)
    {
        var r = await SendAsync(new ObexPacket(Op.Connect, 0x10, 0x00, 0xFF, 0xFF).Add(Hi.Target, target), 4);
        Expect(r, "OBEX bağlantısı", Op.Success);
        _connectionId = r.GetUInt(Hi.ConnectionId);
        _maxPacket = Math.Min(0xFFFF, BinaryPrimitives.ReadUInt16BigEndian(r.Prefix.AsSpan(2)));
    }

    public async Task DisconnectAsync()
    {
        try { await SendAsync(New(Op.Disconnect)); } catch { /* bağlantı zaten kapanmış olabilir */ }
    }

    // name == "" kök klasöre, null + up üst klasöre gider.
    public async Task SetPathAsync(string? name, bool up = false)
    {
        await _lock.WaitAsync();
        try
        {
            var p = New(Op.SetPath, (byte)(0x02 | (up ? 0x01 : 0x00)), 0x00);
            if (name != null) p.AddName(name);
            Expect(await SendAsync(p), $"SetPath '{name}'", Op.Success);
        }
        finally { _lock.Release(); }
    }

    public async Task<byte[]> GetAsync(string type, string? name, byte[]? appParams)
    {
        await _lock.WaitAsync();
        try
        {
            var body = new MemoryStream();
            var p = New(Op.GetFinal);
            if (name != null) p.AddName(name);
            p.AddType(type);
            if (appParams is { Length: > 0 }) p.Add(Hi.AppParams, appParams);
            while (true)
            {
                var r = await SendAsync(p);
                Expect(r, $"GET {type}", Op.Continue, Op.Success);
                foreach (var (id, v) in r.Headers)
                    if (id is Hi.Body or Hi.EndOfBody) body.Write(v);
                if (r.Code == Op.Success) return body.ToArray();
                p = New(Op.GetFinal);
            }
        }
        finally { _lock.Release(); }
    }

    public async Task PutAsync(string type, string? name, byte[]? appParams, byte[] body)
    {
        await _lock.WaitAsync();
        try
        {
            var chunk = Math.Max(256, _maxPacket - 512);
            var offset = 0;
            var first = true;
            while (true)
            {
                var last = body.Length - offset <= chunk;
                var p = New(last ? Op.PutFinal : Op.Put);
                if (first)
                {
                    if (name != null) p.AddName(name);
                    p.AddType(type);
                    if (appParams is { Length: > 0 }) p.Add(Hi.AppParams, appParams);
                    first = false;
                }
                var n = Math.Min(chunk, body.Length - offset);
                p.Add(last ? Hi.EndOfBody : Hi.Body, body[offset..(offset + n)]);
                offset += n;
                var r = await SendAsync(p);
                if (last) { Expect(r, $"PUT {type}", Op.Success); return; }
                Expect(r, $"PUT {type}", Op.Continue, Op.Success);
            }
        }
        finally { _lock.Release(); }
    }
}

// Tek bağlantılı OBEX sunucusu: MAP'te iPhone'un yeni mesaj bildirimlerini (MNS) almak için.
public sealed class ObexServer(Stream input, Stream output, byte[] who)
{
    public event Action<string, byte[]>? Put; // (type, body)

    public async Task RunAsync(CancellationToken ct = default)
    {
        string? type = null;
        var body = new MemoryStream();
        while (!ct.IsCancellationRequested)
        {
            var raw = await ObexPacket.ReadRawAsync(input, ct);
            var req = ObexPacket.Decode(raw, ObexPacket.RequestPrefixLength(raw[0]));
            ObexPacket resp;
            switch (req.Code)
            {
                case Op.Connect:
                    resp = new ObexPacket(Op.Success, 0x10, 0x00, 0xFF, 0xFF).AddUInt(Hi.ConnectionId, 1).Add(Hi.Who, who);
                    break;
                case Op.Put:
                case Op.PutFinal:
                    if (req.Get(Hi.Type) is { } t) type = Encoding.ASCII.GetString(t).TrimEnd('\0');
                    foreach (var (id, v) in req.Headers)
                        if (id is Hi.Body or Hi.EndOfBody) body.Write(v);
                    if (req.Code == Op.PutFinal)
                    {
                        Put?.Invoke(type ?? "", body.ToArray());
                        type = null;
                        body = new MemoryStream();
                        resp = new ObexPacket(Op.Success);
                    }
                    else resp = new ObexPacket(Op.Continue);
                    break;
                case Op.Disconnect:
                    await WriteAsync(new ObexPacket(Op.Success));
                    return;
                default:
                    resp = new ObexPacket(Op.Success);
                    break;
            }
            await WriteAsync(resp);
        }
    }

    private async Task WriteAsync(ObexPacket p)
    {
        await output.WriteAsync(p.Encode());
        await output.FlushAsync();
    }
}
