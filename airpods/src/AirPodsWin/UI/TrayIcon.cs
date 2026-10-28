using System.Drawing;
using AirPodsWin.Core;
using Forms = System.Windows.Forms;

namespace AirPodsWin.UI;

/// <summary>Notification-area icon with battery tooltip and the app's menu.</summary>
sealed class TrayIcon : IDisposable
{
    readonly App _app;
    readonly Forms.NotifyIcon _icon;
    readonly Icon _image;

    readonly Forms.ToolStripMenuItem _status = new() { Enabled = false };
    readonly Forms.ToolStripMenuItem _connect = new("Şimdi bağlan");
    readonly Forms.ToolStripMenuItem _disconnect = new("Bağlantıyı kes");
    readonly Forms.ToolStripMenuItem _popupOnLid = new("Kutu açılınca kartı göster") { CheckOnClick = true };
    readonly Forms.ToolStripMenuItem _autoOnLid = new("Kutu açılınca otomatik bağlan") { CheckOnClick = true };
    readonly Forms.ToolStripMenuItem _autoOnEar = new("Kulağa takınca otomatik bağlan") { CheckOnClick = true };
    readonly Forms.ToolStripMenuItem _startup = new("Windows ile başlat") { CheckOnClick = true };
    readonly Forms.ToolStripMenuItem _devices = new("Cihaz");

    public TrayIcon(App app)
    {
        _app = app;
        _image = CreateIcon();
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "AirPods", ContextMenuStrip = BuildMenu() };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                _app.ShowCardNow();
        };
    }

    public void Show() => _icon.Visible = true;

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    public void Refresh()
    {
        string name = _app.Device?.Name ?? "Eşleşmiş AirPods yok";
        string battery = Describe(_app.LastStatus);
        _status.Text = battery.Length > 0 ? $"{name} · {battery}" : name;

        string tip = battery.Length > 0 ? $"{name}\n{battery}" : name;
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;

        var s = _app.Settings;
        _popupOnLid.Checked = s.ShowPopupOnLidOpen;
        _autoOnLid.Checked = s.AutoConnectOnLidOpen;
        _autoOnEar.Checked = s.AutoConnectOnInEar;
        _startup.Checked = AppSettings.StartWithWindows;
    }

    static string Describe(ProximityPairingMessage? m)
    {
        if (m is null)
            return "";
        if (m.Model.IsSingleBattery())
            return $"Pil {Pct(m.SingleBattery)}";
        return $"Sol {Pct(m.LeftBattery)}  Sağ {Pct(m.RightBattery)}  Kutu {Pct(m.CaseBattery)}";
    }

    static string Pct(int? v) => v is int p ? $"%{p}" : "—";

    Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        _connect.Click += async (_, _) => await _app.ConnectAsync();
        _disconnect.Click += async (_, _) => await _app.DisconnectAsync();
        _popupOnLid.Click += (_, _) => { _app.Settings.ShowPopupOnLidOpen = _popupOnLid.Checked; _app.ApplySettings(); };
        _autoOnLid.Click += (_, _) => { _app.Settings.AutoConnectOnLidOpen = _autoOnLid.Checked; _app.ApplySettings(); };
        _autoOnEar.Click += (_, _) => { _app.Settings.AutoConnectOnInEar = _autoOnEar.Checked; _app.ApplySettings(); };
        _startup.Click += (_, _) => AppSettings.StartWithWindows = _startup.Checked;
        _devices.DropDownOpening += async (_, _) => await FillDevicesAsync();
        _devices.DropDownItems.Add(new Forms.ToolStripMenuItem("…") { Enabled = false });

        menu.Items.Add(_status);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_connect);
        menu.Items.Add(_disconnect);
        menu.Items.Add(new Forms.ToolStripMenuItem("Kartı göster", null, (_, _) => _app.ShowCardNow()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_devices);
        menu.Items.Add(_popupOnLid);
        menu.Items.Add(_autoOnLid);
        menu.Items.Add(_autoOnEar);
        menu.Items.Add(_startup);
        menu.Items.Add(new Forms.ToolStripMenuItem("Bluetooth ayarları", null, (_, _) => App.OpenBluetoothSettings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Çıkış", null, (_, _) => _app.Shutdown()));
        menu.Opening += (_, _) => Refresh();
        return menu;
    }

    async Task FillDevicesAsync()
    {
        var paired = await Bluetooth.PairedAirPods.FindAsync();
        _devices.DropDownItems.Clear();
        if (paired.Count == 0)
            _devices.DropDownItems.Add(new Forms.ToolStripMenuItem("Eşleşmiş AirPods yok") { Enabled = false });

        foreach (var d in paired)
        {
            var item = new Forms.ToolStripMenuItem(d.Name) { Checked = d.Id == _app.Device?.Id };
            item.Click += async (_, _) =>
            {
                _app.Settings.DeviceName = d.Name;
                _app.ApplySettings();
                await _app.RefreshPairedDeviceAsync();
            };
            _devices.DropDownItems.Add(item);
        }
    }

    /// <summary>Uses the app icon embedded in the exe (assets/AirPodsWin.ico).</summary>
    static Icon CreateIcon() =>
        (Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null) ?? (Icon)SystemIcons.Application.Clone();

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
