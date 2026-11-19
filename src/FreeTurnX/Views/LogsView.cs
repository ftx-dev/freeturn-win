using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeTurn.Core;
using Microsoft.Win32;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

public sealed class LogsView : UserControl, IDisposable
{
    readonly AppModel model;
    readonly ListBox list = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12, BorderThickness = new Thickness(0) };
    LogLevel minLevel = LogLevel.Debug;
    LogSource? source;
    string query = "";

    public LogsView(AppModel model)
    {
        this.model = model;
        var toolbar = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.Children.Add(bar);
        var level = Ui.Choice(minLevel, [(LogLevel.Debug, L("Все")), (LogLevel.Info, L("Инфо и выше")), (LogLevel.Warn, L("Предупреждения и ошибки")), (LogLevel.Error, L("Только ошибки"))],
            v => { minLevel = v; Fill(); }, 180);
        level.MinWidth = 0;
        level.Height = 36;
        level.Margin = new Thickness(0, 0, 4, 0);
        bar.Children.Add(level);
        var src = Ui.Choice(source, [(null, L("Все источники")), (LogSource.Core, L("Ядро")), (LogSource.Engine, L("Движок")), (LogSource.App, L("Приложение"))],
            v => { source = v; Fill(); }, 160);
        src.MinWidth = 0;
        src.Height = 36;
        src.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(src, 1);
        bar.Children.Add(src);
        if (!Ui.Compact)
        {
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.Width = 380;
        }
        var commands = new DockPanel { LastChildFill = true };
        toolbar.Children.Add(commands);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        commands.Children.Add(actions);
        var search = new TextBox { MinWidth = 120, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = L("Поиск") };
        search.TextChanged += (_, _) => { query = search.Text; Fill(); };
        commands.Children.Add(search);
        var copy = Ui.ActionIcon(L("Копировать"), "M6,6 L18,6 18,20 6,20 Z M3,16 L2,16 2,2 14,2 14,3", () => Clipboard.SetText(string.Join(Environment.NewLine, Lines().Select(Render))));
        actions.Children.Add(copy);
        actions.Children.Add(Ui.ActionIcon(L("Сохранить…"), "M2,2 L16,2 20,6 20,20 2,20 Z M6,2 L6,8 15,8 15,2 M6,20 L6,13 16,13 16,20", Save));
        var clear = Ui.ActionIcon(L("Очистить"), "M2,4 L18,4 M7,4 L7,1 13,1 13,4 M4,4 L5,20 15,20 16,4", () => model.Logs.Clear(), Ui.Red);
        clear.Margin = new Thickness(0);
        actions.Children.Add(clear);
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(list);
        Content = root;
        model.Logs.CollectionChanged += OnLogs;
        Fill();
    }

    System.Collections.Generic.IEnumerable<LogLine> Lines() => model.Logs.Where(l =>
        l.Level >= minLevel && (source is null || l.Source == source) && (query.Length == 0 || l.Text.Contains(query, StringComparison.OrdinalIgnoreCase)));

    static string Render(LogLine l) => $"{l.Date:HH:mm:ss} [{l.Source.ToString().ToLowerInvariant()}] {l.Text}";

    void OnLogs(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { } items)
        {
            foreach (LogLine l in items) if (Lines().Contains(l)) Add(l);
            // Новые строки внизу: держим их на виду.
            if (list.Items.Count > 0) list.ScrollIntoView(list.Items[^1]);
        }
        else Fill();
    }

    void Fill()
    {
        list.Items.Clear();
        foreach (var l in Lines()) Add(l);
        if (list.Items.Count > 0) list.ScrollIntoView(list.Items[^1]);
    }

    void Add(LogLine l)
    {
        var text = new TextBlock { Text = Render(l), TextWrapping = TextWrapping.Wrap };
        if (l.Level == LogLevel.Error) text.Foreground = Ui.Red;
        else if (l.Level == LogLevel.Warn) text.Foreground = Ui.Orange;
        else if (l.Level == LogLevel.Debug) text.Opacity = 0.6;
        list.Items.Add(text);
    }

    /// <summary>Файл сессии целиком: он длиннее экрана и переживает переподключения.</summary>
    void Save()
    {
        var d = new SaveFileDialog { FileName = "freeturnx-log.txt", Filter = "Text|*.txt" };
        if (d.ShowDialog() == true) model.LogFile.Export(d.FileName);
    }

    public void Dispose() => model.Logs.CollectionChanged -= OnLogs;
}
