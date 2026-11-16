using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using FreeTurn.Core;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

public sealed class HomeView : UserControl, IDisposable
{
    readonly AppModel model;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public HomeView(AppModel model)
    {
        this.model = model;
        model.PropertyChanged += OnChanged;
        model.ServersChanged += Render;
        clock.Tick += (_, _) => { if (model.Connection.State is ConnectionState.Connected) Render(); };
        clock.Start();
        Render();
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e) => Render();

    void Render()
    {
        if (model.Servers.List.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            empty.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 48, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = L("Нет серверов"), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = L("Добавьте сервер, чтобы подключиться"), Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center });
            var add = Ui.Button(L("Добавить сервер"), () => model.Section = Section.Servers, accent: true);
            add.Margin = new Thickness(0, 16, 0, 0);
            add.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(add);
            Content = empty;
            return;
        }

        var state = model.Connection.State;
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 28, 24, 24), MaxWidth = 620 };
        panel.Children.Add(PowerButton(state));
        panel.Children.Add(new TextBlock { Text = Title(state), FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(ServerPicker(state));

        if (model.CaptchaNeedsAttention)
        {
            var b = Ui.Button(L("Пройти проверку"), model.ReopenCaptcha, accent: true);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(b);
        }
        if (state is ConnectionState.Failed failed) panel.Children.Add(Failure(failed.Reason));
        if (state is ConnectionState.Connected) panel.Children.Add(Stats());
        if (model.ActiveProfile is { } profile)
            panel.Children.Add(new TextBlock { Text = $"{L("Маршруты:")} {model.DisplayName(profile)}", Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    string Title(ConnectionState state) => state switch
    {
        ConnectionState.Starting => L("Запуск…"),
        ConnectionState.Connecting => L("Подключение…"),
        ConnectionState.CaptchaWait => model.CaptchaNeedsAttention ? L("Нужна проверка CAPTCHA") : L("CAPTCHA решается…"),
        ConnectionState.Connected => L("Подключено"),
        ConnectionState.Stopping => L("Отключение…"),
        ConnectionState.Failed => L("Ошибка"),
        _ => L("Нажмите для подключения"),
    };

    UIElement PowerButton(ConnectionState state)
    {
        var (top, bottom) = state switch
        {
            ConnectionState.Connected => (Color.FromRgb(41, 199, 133), Color.FromRgb(13, 140, 115)),
            ConnectionState.Starting or ConnectionState.Connecting or ConnectionState.Stopping => (Color.FromRgb(64, 133, 245), Color.FromRgb(46, 82, 199)),
            ConnectionState.CaptchaWait => (Color.FromRgb(250, 168, 46), Color.FromRgb(230, 115, 25)),
            ConnectionState.Failed => (Color.FromRgb(237, 84, 79), Color.FromRgb(184, 46, 56)),
            _ => (Color.FromRgb(107, 107, 112), Color.FromRgb(71, 71, 76)),
        };
        var circle = new Ellipse
        {
            Width = 164, Height = 164,
            Fill = new LinearGradientBrush(top, bottom, 90),
            Stroke = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)),
            StrokeThickness = 1,
            Effect = new DropShadowEffect { Color = bottom, BlurRadius = 28, ShadowDepth = 6, Opacity = 0.45 },
        };
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 56, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Grid();
        face.Children.Add(circle);
        face.Children.Add(glyph);
        var button = new Button
        {
            Content = face, Width = 180, Height = 180, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = state is not ConnectionState.Stopping,
            Template = PlainTemplate(),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, state.IsActive ? L("Отключить") : L("Подключить"));
        button.Click += async (_, _) => await model.ToggleConnectionAsync();
        return button;
    }

    /// <summary>Кнопка без стандартной рамки: вид целиком задаёт её содержимое.</summary>
    static ControlTemplate PlainTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }

    UIElement ServerPicker(ConnectionState state)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 260, IsEnabled = !state.IsActive };
        foreach (var s in model.Servers.List) combo.Items.Add(new ComboBoxItem { Content = Summary(s, state), Tag = s.Id });
        combo.SelectedIndex = model.Servers.List.FindIndex(s => s.Id == model.Servers.ActiveId);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string id } && id != model.Servers.ActiveId) model.SetActive(id); };
        return combo;
    }

    static string Summary(Server s, ConnectionState state)
    {
        var parts = new System.Collections.Generic.List<string> { s.Name, s.Client.Provider == Provider.Direct ? L("Напрямую") : L("Через релей") };
        if (s.Client.Provider != Provider.Direct && state is ConnectionState.Connected { Total: > 0 } c) parts.Add($"{c.Streams}/{c.Total} {L("потоков")}");
        if (s.Client.Provider != Provider.Direct && state is ConnectionState.Connecting { Total: > 0 } g) parts.Add($"{g.Streams}/{g.Total} {L("потоков")}");
        return string.Join(" · ", parts);
    }

    UIElement Failure(ConnectionFailure reason)
    {
        if (reason is ConnectionFailure.DaemonNotInstalled or ConnectionFailure.DaemonVersionMismatch)
        {
            var outdated = reason is ConnectionFailure.DaemonVersionMismatch;
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = outdated ? L("Служба устарела") : L("Нужна фоновая служба"), FontWeight = FontWeights.SemiBold });
            card.Children.Add(Ui.Hint(outdated
                ? L("Установленная служба от другой версии приложения. Её нужно обновить — понадобятся права администратора.")
                : L("Служба туннеля ставится вместе с FreeTurnX. Если её удалили, установите заново — понадобятся права администратора.")));
            if (model.DaemonError is { } err) card.Children.Add(Ui.Hint(err, Ui.Red));
            var b = Ui.Button(model.InstallingDaemon ? L("Установка…") : (outdated ? L("Обновить службу") : L("Установить службу")),
                async () => await model.InstallDaemonAndConnectAsync(), accent: true);
            b.IsEnabled = !model.InstallingDaemon;
            b.HorizontalAlignment = HorizontalAlignment.Left;
            card.Children.Add(b);
            var border = Ui.Card(card);
            border.Margin = new Thickness(0, 16, 0, 0);
            return border;
        }
        return new TextBlock
        {
            Text = model.FailureText(reason), Foreground = Ui.Red, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0), MaxWidth = 520,
        };
    }

    UIElement Stats()
    {
        var s = model.Connection.Stats;
        var en = L10n.IsEnglish;
        var since = model.Connection.ConnectedSince;
        var grid = new UniformGrid3(
            Tile(L("Загрузка"), Formatters.Rate(s.RxRate, en), ""),
            Tile(L("Отдача"), Formatters.Rate(s.TxRate, en), ""),
            Tile(L("Время сессии"), Formatters.Duration(since is null ? TimeSpan.Zero : DateTime.Now - since.Value), ""),
            Tile(L("Принято"), Formatters.Bytes(s.Rx, en), ""),
            Tile(L("Отправлено"), Formatters.Bytes(s.Tx, en), ""),
            PingTile());
        grid.Margin = new Thickness(0, 18, 0, 0);
        return grid;
    }

    UIElement PingTile()
    {
        var tile = Tile(L("Пинг"), model.PingMs is { } ms ? $"{ms} {L("мс")}" : $"— {L("мс")}", "");
        tile.ToolTip = L("Задержка через туннель. Нажмите, чтобы обновить.");
        tile.Cursor = System.Windows.Input.Cursors.Hand;
        tile.MouseLeftButtonUp += async (_, _) => await model.MeasurePingAsync();
        return tile;
    }

    static Border Tile(string title, string value, string glyph)
    {
        var p = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0.65 };
        head.Children.Add(Ui.Glyph(glyph, 12));
        head.Children.Add(new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) });
        p.Children.Add(head);
        p.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var b = Ui.Card(p);
        b.Margin = new Thickness(4);
        b.MinWidth = 170;
        return b;
    }

    public void Dispose()
    {
        clock.Stop();
        model.PropertyChanged -= OnChanged;
        model.ServersChanged -= Render;
    }
}

/// <summary>Сетка 3×N для плиток статистики.</summary>
sealed class UniformGrid3 : System.Windows.Controls.Primitives.UniformGrid
{
    public UniformGrid3(params UIElement[] items)
    {
        Columns = 3;
        foreach (var i in items) Children.Add(i);
    }
}
