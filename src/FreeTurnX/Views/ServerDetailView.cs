using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeTurn.Core;
using Microsoft.Win32;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

/// <summary>Параметры сервера. Правки сохраняются сразу.</summary>
public sealed class ServerDetailView : UserControl, IDisposable
{
    readonly AppModel model;
    readonly string serverId;
    Server server;

    public ServerDetailView(AppModel model, string serverId)
    {
        this.model = model;
        this.serverId = serverId;
        server = model.Servers.List.First(s => s.Id == serverId).Copy();
        model.PropertyChanged += OnModelChanged;
        Render();
    }

    /// <summary>Параметры сервера под живым подключением менять нельзя: ядро и движок уже запущены с ними.</summary>
    bool Locked => model.Connection.State.IsActive && model.Connection.ActiveServerId == serverId;
    bool lockedShown;

    void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.Settings))
        {
            server = model.Servers.List.First(s => s.Id == serverId).Copy();
            Render();
        }
        if (e.PropertyName == nameof(AppModel.Connection) && Locked != lockedShown) Render();
    }

    void Save(Action<Server> change, bool rerender = false)
    {
        if (Locked) return;
        change(server);
        model.Servers.Update(server.Copy());
        model.NotifyServers();
        if (rerender) Render();
    }

    /// <summary>Приватный режим: адреса, ключи и ссылка видны точками, но их можно ввести и заменить.</summary>
    Control Secret(string value, Action<string> changed, double width = 320)
    {
        if (!model.Settings.PrivacyMode) return Ui.Field(value, changed, width);
        var box = new PasswordBox { Password = value, MinWidth = width };
        box.PasswordChanged += (_, _) => changed(box.Password);
        return box;
    }

    void Render()
    {
        lockedShown = Locked;
        var c = server.Client;
        var o = server.Opts;
        var relay = c.Provider == Provider.Relay;
        var page = new System.Collections.Generic.List<UIElement>();

        if (Locked) page.Add(Ui.Hint(L("Остановите подключение, чтобы изменить этот сервер.")));
        if (!c.WireGuardActive) page.Add(Ui.Hint(L("Добавьте конфиг WireGuard, чтобы подключиться"), Ui.Orange));

        page.Add(Ui.Header(L("Подключение")));
        var connection = new System.Collections.Generic.List<UIElement>
        {
            Ui.Row(L("Имя сервера"), Ui.Field(server.Name, v => Save(s => s.Name = v))),
            Ui.Row(L("Адрес сервера"), Secret(c.ServerAddress, v => Save(s => s.Client.ServerAddress = v.Trim()))),
            Ui.Row(L("Способ"), Ui.Choice(c.Provider, [(Provider.Relay, L("Через релей")), (Provider.Direct, L("Напрямую"))],
                v => Save(s => s.Client.Provider = v, rerender: true))),
        };
        if (relay) connection.Add(Ui.Row(L("Ссылка на звонок"), Secret(c.CallLink, v => Save(s => s.Client.CallLink = v.Trim()))));
        page.Add(Ui.Card([.. connection]));
        if (relay) page.Add(Ui.Hint(L("Создайте звонок, скопируйте ссылку и не завершайте его.")));

        if (relay)
        {
            page.Add(Ui.Header(L("Производительность")));
            page.Add(Ui.Card(
                Ui.Row(L("Потоки"), Number(c.Threads, 1, model.Settings.UnsafeStreamLimits ? 96 : 24, v => Save(s => s.Client.Threads = v))),
                Ui.Row(L("Потоков на аккаунт"), Number(c.StreamsPerCred, 1, model.Settings.UnsafeStreamLimits ? 48 : 24, v => Save(s => s.Client.StreamsPerCred = v)))));
            page.Add(Ui.Hint(L("Рекомендуется 8–12 потоков: больше — выше риск блокировки со стороны сервиса звонков.")));
        }

        page.Add(Ui.Header(L("Обфускация")));
        var obf = new System.Collections.Generic.List<UIElement>
        {
            Ui.Row(L("Профиль"), Ui.Choice(o.ObfProfile, [.. ObfProfile.Values.Select(v => (v, v == ObfProfile.None ? L("Выкл") : v))],
                v => Save(s => s.Opts.ObfProfile = v, rerender: true))),
        };
        if (o.ObfEnabled)
        {
            var keyPanel = new Grid();
            keyPanel.ColumnDefinitions.Add(new ColumnDefinition());
            keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var key = Secret(o.ObfKey, v => Save(s => s.Opts.ObfKey = v.Trim()), 280);
            key.MinWidth = 0;
            key.FontFamily = new FontFamily("Consolas");
            keyPanel.Children.Add(key);
            var gen = Ui.Button(L("Сгенерировать"), () => Save(s => s.Opts.ObfKey = ObfProfile.GenerateKey(), rerender: true));
            gen.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(gen, 1);
            if (Ui.Compact)
            {
                keyPanel.ColumnDefinitions.Clear();
                keyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                keyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(gen, 0);
                Grid.SetRow(gen, 1);
                gen.HorizontalAlignment = HorizontalAlignment.Right;
                gen.Margin = new Thickness(0, 8, 0, 0);
            }
            keyPanel.Children.Add(gen);
            obf.Add(Ui.Row(L("Ключ (64 hex)"), keyPanel, stretchControl: true));
            obf.Add(Ui.Row(L("Пейсинг, мс"), Number(o.ObfTimingMs, 0, ObfProfile.TimingMax, v => Save(s => s.Opts.ObfTimingMs = v), 5), L("0 — выкл")));
        }
        page.Add(Ui.Card([.. obf]));
        if (o.ObfEnabled && !ObfProfile.IsValidKey(o.ObfKey))
            page.Add(Ui.Hint(L("Неверный формат — нужны 64 hex-символа. Ключ должен совпадать с ключом сервера."), Ui.Red));

        page.Add(Ui.Header("DNS"));
        var carrier = Ui.Switch(c.UseCarrierDns, v => Save(s => s.Client.UseCarrierDns = v), enabled: DnsList.Normalize(c.CustomDns).Length == 0);
        page.Add(Ui.Card(
            Ui.Row(L("Резолвер ядра"), Ui.Choice(c.DnsMode, [("auto", L("Авто")), ("plain", "UDP/53"), ("doh", "DoH")], v => Save(s => s.Client.DnsMode = v))),
            Ui.Row(L("DNS-серверы вручную"), Ui.Field(c.CustomDns, v => Save(s => s.Client.CustomDns = v), placeholder: "1.1.1.1, 8.8.8.8")),
            Ui.Row(L("DNS текущей сети"), carrier)));
        if (DnsList.HasInvalidTokens(c.CustomDns)) page.Add(Ui.Hint(L("Некорректные адреса будут пропущены."), Ui.Orange));

        if (relay)
        {
            page.Add(Ui.Header(L("Дополнительно")));
            var extra = new System.Collections.Generic.List<UIElement>
            {
                Ui.Row(L("Транспорт до реле"), Ui.Choice(c.UseUdp, [(false, "TCP"), (true, "UDP")], v => Save(s => s.Client.UseUdp = v))),
                Ui.Row(L("Ручной ввод капчи"), Ui.Switch(c.ManualCaptcha, v => Save(s => s.Client.ManualCaptcha = v))),
                Ui.Row(L("Альтернативный TURN-узел"), Ui.Switch(c.MagicSwitch, v => Save(s => s.Client.MagicSwitch = v, rerender: true))),
            };
            if (c.MagicSwitch) extra.Add(Ui.Row(L("Адрес TURN-узла"), Secret(c.MagicTurn, v => Save(s => s.Client.MagicTurn = v))));
            page.Add(Ui.Card([.. extra]));
        }

        page.Add(Ui.Header(L("Туннель")));
        page.Add(Ui.Card(TunnelEditor()));

        page.Add(Ui.Header(L("Маршруты")));
        var profiles = new (string? id, string title)[] { (null, L("Общий")) }.Concat(model.Routing.Profiles.Select(p => ((string?)p.Id, model.DisplayName(p)))).ToArray();
        page.Add(Ui.Card(Ui.Row(L("Профиль маршрутов"), Ui.Choice(c.RoutingProfileId, profiles, v => Save(s => s.Client.RoutingProfileId = v)))));

        page.Add(Ui.Header(L("Отладка")));
        page.Add(Ui.Card(Ui.Row(L("Подробное логирование"), Ui.Switch(c.DebugMode, v => Save(s => s.Client.DebugMode = v)))));

        var offset = (Content as ScrollViewer)?.VerticalOffset ?? 0;
        var scroll = Ui.Page([.. page]);
        if (Locked)
        {
            // Keep the explanation readable; every editor and action shares the
            // same disabled appearance, including custom numeric/key controls.
            var children = ((Panel)scroll.Content).Children;
            for (var i = 1; i < children.Count; i++)
            {
                children[i].IsEnabled = false;
                children[i].Opacity = 0.5;
            }
        }
        Content = scroll;
        scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
    }

    UIElement TunnelEditor()
    {
        var panel = new StackPanel();
        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        if (model.Settings.PrivacyMode)
        {
            panel.Children.Add(new TextBlock { Text = L("Конфиг скрыт в приватном режиме."), Opacity = 0.65 });
        }
        else
        {
            var editor = new TextBox
            {
                Text = server.Client.WireGuardConfig, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 150,
                FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            editor.TextChanged += (_, _) =>
            {
                Save(s => SetConfig(s, editor.Text));
                UpdateStatus();
            };
            panel.Children.Add(editor);
        }
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var load = Ui.Button(L("Загрузить из файла…"), () =>
        {
            var d = new OpenFileDialog { Filter = "WireGuard (*.conf)|*.conf|*.*|*.*" };
            if (d.ShowDialog() != true) return;
            Save(s => SetConfig(s, File.ReadAllText(d.FileName)), rerender: true);
        });
        DockPanel.SetDock(load, Dock.Right);
        row.Children.Add(load);
        row.Children.Add(status);
        panel.Children.Add(row);
        void UpdateStatus()
        {
            var text = server.Client.WireGuardConfig;
            if (string.IsNullOrWhiteSpace(text)) { status.Text = L("Конфиг не задан"); status.Foreground = Brushes.Gray; return; }
            try
            {
                var wg = WGConfig.Parse(text);
                status.Text = "✓ " + (wg.IsAmnezia ? "AmneziaWG" : "WireGuard");
                status.Foreground = Ui.Green;
            }
            catch (WGConfigException e)
            {
                status.Text = "✕ " + e.Error switch
                {
                    WGConfigError.MissingInterface => L("В конфиге нет секции [Interface]"),
                    WGConfigError.MissingPrivateKey => L("В конфиге нет PrivateKey"),
                    WGConfigError.MissingAddress => L("В конфиге нет Address"),
                    WGConfigError.MissingPeer => L("В конфиге нет секции [Peer]"),
                    _ => L("У пира нет PublicKey"),
                };
                status.Foreground = Ui.Red;
            }
        }
        UpdateStatus();
        return panel;
    }

    static void SetConfig(Server s, string text)
    {
        s.Client.WireGuardConfig = text;
        s.Client.TunnelTransport = string.IsNullOrWhiteSpace(text) ? TunnelTransport.None : TunnelTransport.WireGuard;
    }

    /// <summary>Число со стрелками: в WPF нет NumericUpDown.</summary>
    static UIElement Number(int value, int min, int max, Action<int> changed, int step = 1)
    {
        var current = value;
        var text = new TextBlock { Text = value.ToString(), MinWidth = 36, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        var minus = new Button { Content = "−", Width = 32, Padding = new Thickness(0, 3, 0, 4) };
        var plus = new Button { Content = "+", Width = 32, Padding = new Thickness(0, 3, 0, 4) };
        void Set(int v)
        {
            current = Math.Clamp(v, min, max);
            text.Text = current.ToString();
            minus.IsEnabled = current > min;
            plus.IsEnabled = current < max;
            changed(current);
        }
        minus.Click += (_, _) => Set(current - step);
        plus.Click += (_, _) => Set(current + step);
        minus.IsEnabled = value > min;
        plus.IsEnabled = value < max;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(minus);
        panel.Children.Add(text);
        panel.Children.Add(plus);
        return panel;
    }

    public void Dispose() => model.PropertyChanged -= OnModelChanged;
}
