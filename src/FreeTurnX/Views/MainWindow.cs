using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

/// <summary>Главное окно: разделы слева, содержимое справа. Закрытие прячет окно в трей - подключение остаётся.</summary>
public sealed class MainWindow : Window
{
    readonly AppModel model;
    readonly ListBox nav = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(8, 8, 0, 8) };
    readonly ContentControl content = new();
    readonly Grid layout = new();
    readonly DockPanel sidebar = new() { Margin = new Thickness(4, 8, 12, 8) };
    readonly ComboBox compactNav = new() { Margin = new Thickness(12, 8, 12, 8), MinHeight = 36 };
    bool compact;
    bool closePromptOpen;
    readonly Border loadError = new() { Padding = new Thickness(12, 8, 12, 8), Background = new SolidColorBrush(Color.FromArgb(60, 255, 200, 0)), Visibility = Visibility.Collapsed };

    static readonly (Section section, string glyph, string title)[] Sections =
    [
        (Section.Home, "", "Главная"),
        (Section.Servers, "", "Серверы"),
        (Section.Routing, "", "Маршруты"),
        (Section.Logs, "", "Логи"),
        (Section.Settings, "", "Настройки"),
        (Section.About, "", "О проекте"),
    ];

    public MainWindow(AppModel model)
    {
        this.model = model;
        Title = "FreeTurnX " + Services.Updater.CurrentVersion
            + (Environment.GetEnvironmentVariable("FREETURN_DATA_DIR") is { Length: > 0 } ? " · Test" : "");
        Width = 1000;
        Height = 700;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Services.AppIcons.Window(model.Settings.AppIcon);
        nav.ItemContainerStyle = (Style)Application.Current.Resources["NavigationItem"];

        foreach (var (section, glyph, title) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
            row.Children.Add(Ui.Glyph(glyph));
            row.Children.Add(new TextBlock { Text = L(title), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = row, Tag = section };
            System.Windows.Automation.AutomationProperties.SetName(item, L(title));
            nav.Items.Add(item);
        }
        nav.SelectionChanged += (_, _) => { if (nav.SelectedItem is ListBoxItem { Tag: Section s }) model.Section = s; };

        var grid = layout;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var brand = new TextBlock { Text = "FreeTurnX", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(22, 12, 12, 24) };
        DockPanel.SetDock(brand, Dock.Top);
        sidebar.Children.Add(brand);
        sidebar.Children.Add(nav);
        grid.Children.Add(sidebar);
        Grid.SetRow(sidebar, 1);
        foreach (var (_, _, title) in Sections) compactNav.Items.Add(L(title));
        compactNav.SelectionChanged += (_, _) =>
        {
            if (compactNav.SelectedIndex >= 0) model.Section = Sections[compactNav.SelectedIndex].section;
        };
        Grid.SetColumnSpan(compactNav, 2);
        grid.Children.Add(compactNav);
        var right = new DockPanel();
        loadError.Child = new TextBlock { TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(loadError, Dock.Top);
        right.Children.Add(loadError);
        var surface = new Border { CornerRadius = new CornerRadius(12, 0, 0, 0), BorderThickness = new Thickness(1, 1, 0, 0), Child = content };
        surface.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        right.Children.Add(surface);
        Grid.SetColumn(right, 1);
        Grid.SetRow(right, 1);
        grid.Children.Add(right);
        Content = grid;

        model.PropertyChanged += OnModelChanged;
        ApplyLayout();
        Show(model.Section);
        UpdateLoadError();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.Section)) Show(model.Section);
        if (e.PropertyName == nameof(AppModel.LoadError)) UpdateLoadError();
        if (e.PropertyName == nameof(AppModel.Settings))
        {
            Icon = Services.AppIcons.Window(model.Settings.AppIcon);
            if (compact != model.Settings.CompactMode)
            {
                foreach (var page in pages.Values) (page as IDisposable)?.Dispose();
                pages.Clear();
                ApplyLayout();
                Show(model.Section);
            }
        }
    }

    void ApplyLayout()
    {
        compact = model.Settings.CompactMode;
        WindowState = WindowState.Normal;
        MinWidth = compact ? 400 : 900;
        MinHeight = compact ? 600 : 580;
        Width = compact ? 400 : 1000;
        Height = compact ? 600 : 700;
        layout.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 210);
        sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        compactNav.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateLoadError()
    {
        ((TextBlock)loadError.Child).Text = model.LoadError ?? "";
        loadError.Visibility = model.LoadError is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// Раздел собирается один раз и дальше только показывается: пересборка форм на каждом
    /// переключении давала заметную задержку.
    readonly System.Collections.Generic.Dictionary<Section, UIElement> pages = [];

    void Show(Section section)
    {
        nav.SelectedIndex = Array.FindIndex(Sections, s => s.section == section);
        compactNav.SelectedIndex = nav.SelectedIndex;
        if (!pages.TryGetValue(section, out var page)) pages[section] = page = Build(section);
        content.Content = page;
        Ui.FadeIn(content);
    }

    UIElement Build(Section section) =>
        section switch
        {
            Section.Home => new HomeView(model),
            Section.Servers => new ServersView(model),
            Section.Routing => new RoutingView(model),
            Section.Logs => new LogsView(model),
            Section.Settings => new SettingsView(model),
            _ => new AboutView(model),
        };

    protected override void OnClosing(CancelEventArgs e)
    {
        if (((App)Application.Current).IsQuitting) return;
        // Окно можно закрыть: приложение остаётся в трее и держит подключение.
        e.Cancel = true;
          if (closePromptOpen) return;
        if (model.Settings.CloseAction == "hide") { Hide(); return; }
        if (model.Settings.CloseAction == "quit") { ((App)Application.Current).Quit(); return; }
        var dialog = new AppDialog(L("Скрыть или выйти из приложения?"), "", L("Скрыть"), L("Выйти"), remember: true);
          bool? result;
          closePromptOpen = true;
          try { result = dialog.ShowDialog(); }
          finally { closePromptOpen = false; }
        if (!dialog.HasChoice) return;
        if (dialog.Remember) { model.Settings.CloseAction = result == true ? "hide" : "quit"; model.Persist(); }
        if (result == true) Hide();
        else ((App)Application.Current).Quit();
    }
}
