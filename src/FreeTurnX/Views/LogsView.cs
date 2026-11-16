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
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 8) };
        bar.Children.Add(Ui.Choice(minLevel, [(LogLevel.Debug, L("Все")), (LogLevel.Info, L("Инфо и выше")), (LogLevel.Warn, L("Предупреждения и ошибки")), (LogLevel.Error, L("Только ошибки"))],
            v => { minLevel = v; Fill(); }, 180));
        var src = Ui.Choice(source, [(null, L("Все источники")), (LogSource.Core, L("Ядро")), (LogSource.Engine, L("Движок")), (LogSource.App, L("Приложение"))],
            v => { source = v; Fill(); }, 160);
        src.Margin = new Thickness(8, 0, 8, 0);
        bar.Children.Add(src);
        var search = new TextBox { MinWidth = 200, ToolTip = L("Поиск") };
        search.TextChanged += (_, _) => { query = search.Text; Fill(); };
        bar.Children.Add(search);
        var copy = Ui.Button(L("Копировать"), () => Clipboard.SetText(string.Join(Environment.NewLine, Lines().Select(Render))));
        copy.Margin = new Thickness(8, 0, 8, 0);
        bar.Children.Add(copy);
        bar.Children.Add(Ui.Button(L("Сохранить…"), Save));
        bar.Children.Add(Ui.Button(L("Очистить"), () => model.Logs.Clear()));
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
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
