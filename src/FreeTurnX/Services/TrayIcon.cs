using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FreeTurn.Core;
using static FreeTurnX.Strings;

namespace FreeTurnX.Services;

/// <summary>Значок в области уведомлений - аналог строки меню macOS. Цвет точки - состояние подключения.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon;
    readonly AppModel model;
    readonly Action showWindow;
    Icon baseIcon;
    string iconVariant;
    Icon? current;

    public TrayIcon(AppModel model, Action showWindow, Action quit)
    {
        this.model = model;
        this.showWindow = showWindow;
        iconVariant = model.Settings.AppIcon;
        baseIcon = AppIcons.Tray(iconVariant);
        icon = new NotifyIcon { Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) showWindow(); };
        BuildMenu(quit);
        icon.ContextMenuStrip.Opening += (_, e) =>
        {
            BuildMenu(quit);
            // WinForms may mark an initially empty menu as cancelled before Opening.
            e.Cancel = false;
        };
        model.PropertyChanged += OnChanged;
        Update();
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    void BuildMenu(Action quit)
    {
        var menu = icon.ContextMenuStrip!;
        while (menu.Items.Count > 0) menu.Items[0].Dispose();
        var state = model.Connection.State;
        menu.Items.Add(new ToolStripMenuItem(StatusLine(state)) { Enabled = false });
        if (state is ConnectionState.Connected && !model.Socks5Mode)
        {
            var s = model.Connection.Stats;
            menu.Items.Add(new ToolStripMenuItem($"↓ {Formatters.Rate(s.RxRate, L10n.IsEnglish)}   ↑ {Formatters.Rate(s.TxRate, L10n.IsEnglish)}") { Enabled = false });
        }
        menu.Items.Add(new ToolStripSeparator());
        if (model.CaptchaNeedsAttention) menu.Items.Add(L("Пройти проверку"), null, (_, _) => model.ReopenCaptcha());
        menu.Items.Add(new ToolStripMenuItem(state.IsActive ? L("Отключить") : L("Подключить"), null, async (_, _) => await model.ToggleConnectionAsync())
        {
            Enabled = model.HasConnectionTarget && state is not ConnectionState.Stopping,
        });
        if (model.Servers.List.Count > 0 && !model.Socks5Mode)
        {
            var servers = new ToolStripMenuItem(L("Сервер")) { Enabled = !state.IsActive };
            foreach (var server in model.Servers.List)
                servers.DropDownItems.Add(new ToolStripMenuItem(server.Name, null, (_, _) => model.SetActive(server.Id)) { Checked = server.Id == model.Servers.ActiveId });
            menu.Items.Add(servers);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L("Открыть FreeTurnX"), null, (_, _) => showWindow());
        menu.Items.Add(L("Выйти"), null, (_, _) => quit());
    }

    string StatusLine(ConnectionState state) => state switch
    {
        ConnectionState.Starting => L("Запуск…"),
        ConnectionState.Connecting => L("Подключение…"),
        ConnectionState.CaptchaWait => L("Проверка CAPTCHA"),
        ConnectionState.Connected => model.Socks5Mode ? L("SOCKS5 подключён") : $"{L("Подключено")} — {model.ActiveServer?.Name}",
        ConnectionState.Stopping => L("Отключение…"),
        ConnectionState.Failed => L("Ошибка"),
        _ => "FreeTurnX",
    };

    void Update()
    {
        if (model.Settings.AppIcon != iconVariant)
        {
            iconVariant = model.Settings.AppIcon;
            baseIcon.Dispose();
            baseIcon = AppIcons.Tray(iconVariant);
        }
        var state = model.Connection.State;
        var color = state switch
        {
            ConnectionState.Connected => Color.FromArgb(40, 200, 130),
            ConnectionState.Starting or ConnectionState.Connecting or ConnectionState.Stopping => Color.FromArgb(70, 140, 245),
            ConnectionState.CaptchaWait => Color.FromArgb(250, 168, 46),
            ConnectionState.Failed => Color.FromArgb(235, 85, 80),
            _ => (Color?)null,
        };
        var next = color is { } c ? WithDot(c) : (Icon)baseIcon.Clone();
        icon.Icon = next;
        current?.Dispose();
        current = next;
        // Подсказка значка ограничена 63 символами.
        var status = StatusLine(state);
        var tip = "FreeTurnX" + (Environment.GetEnvironmentVariable("FREETURN_DATA_DIR") is { Length: > 0 } ? " · Test" : "");
        if (status != "FreeTurnX") tip += " — " + status;
        icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    Icon WithDot(Color color)
    {
        using var bitmap = baseIcon.ToBitmap();
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var d = bitmap.Width * 0.5f;
            var rect = new RectangleF(bitmap.Width - d, bitmap.Height - d, d - 0.5f, d - 0.5f);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.White, Math.Max(1, bitmap.Width / 16f));
            g.FillEllipse(brush, rect);
            g.DrawEllipse(pen, rect);
        }
        var handle = bitmap.GetHicon();
        // Icon.FromHandle не владеет HICON: копируем и освобождаем, иначе каждая смена состояния течёт.
        var copy = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return copy;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        model.PropertyChanged -= OnChanged;
        icon.Visible = false;
        icon.Dispose();
        current?.Dispose();
        baseIcon.Dispose();
    }
}
