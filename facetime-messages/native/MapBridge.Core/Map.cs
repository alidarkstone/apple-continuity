// Bluetooth Message Access Profile (MAP 1.x): mesaj listesi, bMessage ve olay raporları.
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace MapBridge.Core;

public static class MapUuids
{
    public static readonly byte[] Mas = Convert.FromHexString("BB582B40420C11DBB0DE0800200C9A66");
    public static readonly byte[] Mns = Convert.FromHexString("BB582B41420C11DBB0DE0800200C9A66");
}

public sealed record MapMessage(
    string Handle, string Folder, DateTimeOffset Date, string? SenderName, string? SenderAddress,
    string? RecipientAddress, bool Read, string Text)
{
    public bool FromMe => Folder is "sent" or "outbox";
    // Sohbet anahtarı: karşı tarafın adresi.
    public string Peer => (FromMe ? RecipientAddress : SenderAddress) ?? SenderName ?? "?";
}

public sealed record ListingEntry(string Handle, string? Subject, DateTimeOffset Date, string? SenderName,
    string? SenderAddress, string? RecipientAddress, bool Read);

public sealed record BMessage(string? OriginatorName, string? OriginatorTel, string? RecipientTel, string Text);

public sealed record MapEvent(string Type, string? Handle, string? Folder);

public static class MapFormat
{
    public static DateTimeOffset ParseDate(string? s)
    {
        if (s is { Length: >= 15 } &&
            DateTime.TryParseExact(s[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            return new DateTimeOffset(d);
        return DateTimeOffset.Now;
    }

    public static List<ListingEntry> ParseListing(byte[] xml)
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(xml).TrimEnd('\0'));
        return doc.Root!.Elements("msg").Select(m => new ListingEntry(
            (string)m.Attribute("handle")!,
            (string?)m.Attribute("subject"),
            ParseDate((string?)m.Attribute("datetime")),
            (string?)m.Attribute("sender_name"),
            (string?)m.Attribute("sender_addressing"),
            (string?)m.Attribute("recipient_addressing"),
            ((string?)m.Attribute("read"))?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true)).ToList();
    }

    public static MapEvent? ParseEvent(byte[] xml)
    {
        var e = XDocument.Parse(Encoding.UTF8.GetString(xml).TrimEnd('\0')).Root?.Element("event");
        if (e == null) return null;
        return new MapEvent((string)e.Attribute("type")!, (string?)e.Attribute("handle"), ((string?)e.Attribute("folder"))?.ToLowerInvariant());
    }

    public static BMessage ParseBMessage(string s)
    {
        string? origName = null, origTel = null, recipTel = null;
        var lines = s.Replace("\r\n", "\n").Split('\n');
        var depth = 0;          // 0: BMSG kökü (gönderen kartı), >0: BENV içi (alıcı kartı)
        var inMsg = false;
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (line == "BEGIN:MSG") { inMsg = true; continue; }
            if (line == "END:MSG") { inMsg = false; continue; }
            if (inMsg) { if (text.Length > 0) text.Append('\n'); text.Append(line); continue; }
            if (line == "BEGIN:BENV") depth++;
            else if (line == "END:BENV") depth--;
            else if (Field(line, "TEL") is { } tel) { if (depth == 0) origTel ??= tel; else recipTel ??= tel; }
            else if (Field(line, "EMAIL") is { } mail) { if (depth == 0) origTel ??= mail; else recipTel ??= mail; }
            else if (depth == 0 && (Field(line, "FN") ?? Field(line, "N")) is { Length: > 0 } name) origName ??= name.Replace(";", " ").Trim();
        }
        return new BMessage(origName, origTel, recipTel, text.ToString());
    }

    private static string? Field(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = line[key.Length..];
        if (rest.Length == 0 || (rest[0] != ':' && rest[0] != ';')) return null;
        var colon = rest.IndexOf(':');
        return colon < 0 ? null : rest[(colon + 1)..].Trim();
    }

    public static string BuildBMessage(string to, string text)
    {
        var msg = "BEGIN:MSG\r\n" + text.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\nEND:MSG\r\n";
        var sb = new StringBuilder();
        sb.Append("BEGIN:BMSG\r\nVERSION:1.0\r\nSTATUS:UNREAD\r\nTYPE:SMS_GSM\r\nFOLDER:telecom/msg/outbox\r\n");
        sb.Append("BEGIN:VCARD\r\nVERSION:2.1\r\nN:\r\nTEL:\r\nEND:VCARD\r\n");
        sb.Append("BEGIN:BENV\r\n");
        sb.Append($"BEGIN:VCARD\r\nVERSION:2.1\r\nN:{to}\r\n{(to.Contains('@') ? "EMAIL" : "TEL")}:{to}\r\nEND:VCARD\r\n");
        sb.Append($"BEGIN:BBODY\r\nCHARSET:UTF-8\r\nLENGTH:{Encoding.UTF8.GetByteCount(msg)}\r\n");
        sb.Append(msg);
        sb.Append("END:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n");
        return sb.ToString();
    }
}

// MAS istemcisi: bağlantı kurulduktan sonra telecom/msg klasöründe durur.
public sealed class MapSession(ObexClient obex)
{
    private readonly Dictionary<string, MapMessage> _cache = new();

    public async Task OpenAsync()
    {
        await obex.ConnectAsync(MapUuids.Mas);
        await obex.SetPathAsync("");
        await obex.SetPathAsync("telecom");
        await obex.SetPathAsync("msg");
    }

    public async Task<List<MapMessage>> ListAsync(string folder, int max)
    {
        var xml = await obex.GetAsync("x-bt/MAP-msg-listing", folder,
            AppParams.Build((AppParams.MaxListCount, AppParams.U16((ushort)max))));
        var result = new List<MapMessage>();
        foreach (var e in MapFormat.ParseListing(xml))
            result.Add(await GetAsync(e.Handle, folder, e));
        return result;
    }

    public async Task<MapMessage> GetAsync(string handle, string folder, ListingEntry? entry = null)
    {
        if (_cache.TryGetValue(handle, out var cached)) return cached;
        var raw = await obex.GetAsync("x-bt/message", handle,
            AppParams.Build((AppParams.Attachment, AppParams.U8(0)), (AppParams.Charset, AppParams.U8(1))));
        var b = MapFormat.ParseBMessage(Encoding.UTF8.GetString(raw));
        var m = new MapMessage(handle, folder, entry?.Date ?? DateTimeOffset.Now,
            entry?.SenderName ?? b.OriginatorName, entry?.SenderAddress ?? b.OriginatorTel,
            entry?.RecipientAddress ?? b.RecipientTel, entry?.Read ?? false,
            b.Text.Length > 0 ? b.Text : entry?.Subject ?? "");
        _cache[handle] = m;
        return m;
    }

    public Task PushAsync(string to, string text) =>
        obex.PutAsync("x-bt/message", "outbox", AppParams.Build((AppParams.Charset, AppParams.U8(1))),
            Encoding.UTF8.GetBytes(MapFormat.BuildBMessage(to, text)));

    // Gövde zorunlu ve tek bir dolgu baytıdır (0x30).
    public Task SetNotificationsAsync(bool on) =>
        obex.PutAsync("x-bt/MAP-NotificationRegistration", null,
            AppParams.Build((AppParams.NotificationStatus, AppParams.U8(on ? (byte)1 : (byte)0))), new byte[] { 0x30 });

    public Task CloseAsync() => obex.DisconnectAsync();
}
