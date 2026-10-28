using System.IO.Pipes;
using System.Text;
using MapBridge.Core;
using Xunit;

namespace MapBridge.Tests;

public class MapTests
{
    [Fact]
    public void Packet_round_trips_all_header_kinds()
    {
        var p = new ObexPacket(Op.GetFinal).AddUInt(Hi.ConnectionId, 7).AddName("inbox").AddType("x-bt/MAP-msg-listing")
            .Add(Hi.AppParams, AppParams.Build((AppParams.MaxListCount, AppParams.U16(50))));
        var d = ObexPacket.Decode(p.Encode(), 0);
        Assert.Equal(Op.GetFinal, d.Code);
        Assert.Equal(7u, d.GetUInt(Hi.ConnectionId));
        Assert.Equal("inbox\0", Encoding.BigEndianUnicode.GetString(d.Get(Hi.Name)!));
        Assert.Equal("x-bt/MAP-msg-listing\0", Encoding.ASCII.GetString(d.Get(Hi.Type)!));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x00, 0x32 }, d.Get(Hi.AppParams));
    }

    [Fact]
    public void Empty_name_header_has_no_terminator()
    {
        var bytes = new ObexPacket(Op.SetPath, 0x02, 0x00).AddName("").Encode();
        Assert.Equal(new byte[] { 0x85, 0x00, 0x08, 0x02, 0x00, 0x01, 0x00, 0x03 }, bytes);
    }

    [Fact]
    public void BMessage_build_and_parse()
    {
        var s = MapFormat.BuildBMessage("+905550000001", "Merhaba\nnasılsın? 👋");
        var b = MapFormat.ParseBMessage(s);
        Assert.Equal("+905550000001", b.RecipientTel);
        Assert.Equal("Merhaba\nnasılsın? 👋", b.Text);
        var msg = "BEGIN:MSG\r\nMerhaba\r\nnasılsın? 👋\r\nEND:MSG\r\n";
        Assert.Contains($"LENGTH:{Encoding.UTF8.GetByteCount(msg)}\r\n", s);
    }

    [Fact]
    public void BMessage_parses_originator_from_incoming_message()
    {
        const string s = "BEGIN:BMSG\r\nVERSION:1.0\r\nSTATUS:UNREAD\r\nTYPE:SMS_GSM\r\nFOLDER:TELECOM/MSG/INBOX\r\n" +
                         "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8:Yılmaz;Ayşe\r\nTEL:+905550000001\r\nEND:VCARD\r\n" +
                         "BEGIN:BENV\r\nBEGIN:BBODY\r\nCHARSET:UTF-8\r\nLENGTH:33\r\nBEGIN:MSG\r\nAkşam görüşelim\r\nEND:MSG\r\nEND:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n";
        var b = MapFormat.ParseBMessage(s);
        Assert.Equal("Yılmaz Ayşe", b.OriginatorName);
        Assert.Equal("+905550000001", b.OriginatorTel);
        Assert.Equal("Akşam görüşelim", b.Text);
    }

    [Fact]
    public void Listing_and_event_parse()
    {
        var listing = MapFormat.ParseListing(Encoding.UTF8.GetBytes(
            "<MAP-msg-listing version=\"1.0\"><msg handle=\"20000100001\" subject=\"Selam\" datetime=\"20261028T154300\" " +
            "sender_name=\"Ayşe\" sender_addressing=\"+905550000001\" type=\"SMS_GSM\" read=\"no\"/></MAP-msg-listing>"));
        var e = Assert.Single(listing);
        Assert.Equal("20000100001", e.Handle);
        Assert.Equal(new DateTime(2026, 10, 28, 15, 43, 0), e.Date.DateTime);
        Assert.False(e.Read);

        var ev = MapFormat.ParseEvent(Encoding.UTF8.GetBytes(
            "<MAP-event-report version=\"1.0\"><event type=\"NewMessage\" handle=\"20000100002\" folder=\"TELECOM/MSG/INBOX\" msg_type=\"SMS_GSM\"/></MAP-event-report>"));
        Assert.Equal(new MapEvent("NewMessage", "20000100002", "telecom/msg/inbox"), ev);
    }

    // Uçtan uca: sahte bir iPhone MAS sunucusuna karşı bağlan, listele, mesaj gönder.
    [Fact]
    public async Task Session_talks_to_fake_iphone()
    {
        using var toPhone = new AnonymousPipeServerStream(PipeDirection.Out);
        using var phoneIn = new AnonymousPipeClientStream(PipeDirection.In, toPhone.ClientSafePipeHandle);
        using var toPc = new AnonymousPipeServerStream(PipeDirection.Out);
        using var pcIn = new AnonymousPipeClientStream(PipeDirection.In, toPc.ClientSafePipeHandle);

        var pushed = new MemoryStream();
        var paths = new List<string>();
        var phone = Task.Run(async () =>
        {
            async Task Reply(ObexPacket p) { await toPc.WriteAsync(p.Encode()); await toPc.FlushAsync(); }
            var listingParts = 0;
            while (true)
            {
                var raw = await ObexPacket.ReadRawAsync(phoneIn);
                var req = ObexPacket.Decode(raw, ObexPacket.RequestPrefixLength(raw[0]));
                if (req.Code != Op.Connect) Assert.Equal(0x1234u, req.GetUInt(Hi.ConnectionId));
                var type = req.Get(Hi.Type) is { } t ? Encoding.ASCII.GetString(t).TrimEnd('\0') : null;
                switch (req.Code)
                {
                    case Op.Connect:
                        Assert.Equal(MapUuids.Mas, req.Get(Hi.Target));
                        await Reply(new ObexPacket(Op.Success, 0x10, 0, 0x04, 0x00).AddUInt(Hi.ConnectionId, 0x1234));
                        break;
                    case Op.SetPath:
                        paths.Add(Encoding.BigEndianUnicode.GetString(req.Get(Hi.Name) ?? Array.Empty<byte>()).TrimEnd('\0'));
                        await Reply(new ObexPacket(Op.Success));
                        break;
                    case Op.GetFinal when type == "x-bt/MAP-msg-listing" || listingParts == 1:
                        // Listeyi iki parça halinde gönder (Continue + Success).
                        var xml = Encoding.UTF8.GetBytes("<MAP-msg-listing version=\"1.0\"><msg handle=\"1\" datetime=\"20261028T120000\" sender_addressing=\"+905550000001\" read=\"yes\"/></MAP-msg-listing>");
                        if (listingParts++ == 0) await Reply(new ObexPacket(Op.Continue).Add(Hi.Body, xml[..20]));
                        else await Reply(new ObexPacket(Op.Success).Add(Hi.EndOfBody, xml[20..]));
                        break;
                    case Op.GetFinal when type == "x-bt/message":
                        await Reply(new ObexPacket(Op.Success).Add(Hi.EndOfBody, Encoding.UTF8.GetBytes(
                            "BEGIN:BMSG\r\nBEGIN:VCARD\r\nTEL:+905550000001\r\nEND:VCARD\r\nBEGIN:BENV\r\nBEGIN:BBODY\r\nBEGIN:MSG\r\nSelam\r\nEND:MSG\r\nEND:BBODY\r\nEND:BENV\r\nEND:BMSG\r\n")));
                        break;
                    case Op.Put:
                    case Op.PutFinal:
                        foreach (var (id, v) in req.Headers) if (id is Hi.Body or Hi.EndOfBody) pushed.Write(v);
                        await Reply(new ObexPacket(req.Code == Op.PutFinal ? Op.Success : Op.Continue));
                        break;
                    case Op.Disconnect:
                        await Reply(new ObexPacket(Op.Success));
                        return;
                }
            }
        });

        var session = new MapSession(new ObexClient(pcIn, toPhone));
        await session.OpenAsync();
        Assert.Equal(new[] { "", "telecom", "msg" }, paths);

        var msgs = await session.ListAsync("inbox", 50);
        var m = Assert.Single(msgs);
        Assert.Equal("Selam", m.Text);
        Assert.Equal("+905550000001", m.Peer);
        Assert.False(m.FromMe);

        var longText = new string('a', 3000); // 0x400 paket sınırında birden fazla PUT parçası gerektirir
        await session.PushAsync("+905550000001", longText);
        var sent = MapFormat.ParseBMessage(Encoding.UTF8.GetString(pushed.ToArray()));
        Assert.Equal(longText, sent.Text);
        Assert.Equal("+905550000001", sent.RecipientTel);

        await session.CloseAsync();
        await phone.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Server_raises_event_reports()
    {
        using var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var serverIn = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
        using var toClient = new AnonymousPipeServerStream(PipeDirection.Out);
        using var clientIn = new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle);

        var server = new ObexServer(serverIn, toClient, MapUuids.Mns);
        var got = new TaskCompletionSource<(string, byte[])>();
        server.Put += (t, b) => got.TrySetResult((t, b));
        var run = server.RunAsync();

        var client = new ObexClient(clientIn, toServer);
        await client.ConnectAsync(MapUuids.Mns);
        var report = Encoding.UTF8.GetBytes("<MAP-event-report version=\"1.0\"><event type=\"NewMessage\" handle=\"9\" folder=\"TELECOM/MSG/INBOX\"/></MAP-event-report>");
        await client.PutAsync("x-bt/MAP-event-report-1.0", null, null, report);
        var (type, body) = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("x-bt/MAP-event-report-1.0", type);
        Assert.Equal("9", MapFormat.ParseEvent(body)!.Handle);
        await client.DisconnectAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
