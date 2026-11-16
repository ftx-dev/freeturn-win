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
        Title = "FreeTurnX";
        Width = 1000;
        Height = 700;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = Services.AppIcons.Window(model.Settings.AppIcon);

        foreach (var (section, glyph, title) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
            row.Children.Add(Ui.Glyph(glyph));
            row.Children.Add(new TextBlock { Text = L(title), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            nav.Items.Add(new ListBoxItem { Content = row, Tag = section });
        }
        nav.SelectionChanged += (_, _) => { if (nav.SelectedItem is ListBoxItem { Tag: Section s }) model.Section = s; };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(nav);
        var right = new DockPanel();
        loadError.Child = new TextBlock { TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(loadError, Dock.Top);
        right.Children.Add(loadError);
        right.Children.Add(content);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        model.PropertyChanged += OnModelChanged;
        Show(model.Section);
        UpdateLoadError();
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.Section)) Show(model.Section);
        if (e.PropertyName == nameof(AppModel.LoadError)) UpdateLoadError();
        if (e.PropertyName == nameof(AppModel.Settings)) Icon = Services.AppIcons.Window(model.Settings.AppIcon);
    }

    void UpdateLoadError()
    {
        ((TextBlock)loadError.Child).Text = model.LoadError ?? "";
        loadError.Visibility = model.LoadError is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void Show(Section section)
    {
        nav.SelectedIndex = Array.FindIndex(Sections, s => s.section == section);
        (content.Content as IDisposable)?.Dispose();
        content.Content = section switch
        {
            Section.Home => new HomeView(model),
            Section.Servers => new ServersView(model),
            Section.Routing => new RoutingView(model),
            Section.Logs => new LogsView(model),
            Section.Settings => new SettingsView(model),
            _ => new AboutView(model),
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Окно можно закрыть: приложение остаётся в трее и держит подключение.
        e.Cancel = true;
        Hide();
    }
}
