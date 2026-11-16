using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeTurn.Core;
using FreeTurnX.Services;
using Microsoft.Win32;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

public sealed class ServersView : UserControl, IDisposable
{
    readonly AppModel model;
    readonly ListBox list = new() { BorderThickness = new Thickness(0) };
    readonly ContentControl detail = new();
    string? selectedId;

    public ServersView(AppModel model)
    {
        this.model = model;
        selectedId = model.Servers.ActiveId;

        var add = new Button { Content = "＋  " + L("Добавить сервер"), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(8, 8, 8, 4) };
        var addMenu = new ContextMenu();
        addMenu.Items.Add(Item(L("Вставить из буфера"), PasteFromClipboard));
        addMenu.Items.Add(Item(L("QR-код из файла…"), QrFromFile));
        addMenu.Items.Add(Item(L("QR-код с экрана"), async () => Offer(await QrImport.FromScreenAsync())));
        addMenu.Items.Add(new Separator());
        addMenu.Items.Add(Item(L("Ручная настройка"), AddManual));
        add.ContextMenu = addMenu;
        add.Click += (_, _) => { addMenu.PlacementTarget = add; addMenu.IsOpen = true; };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string id }) { selectedId = id; ShowDetail(); }
        };

        var left = new DockPanel { Width = 260 };
        DockPanel.SetDock(add, Dock.Top);
        left.Children.Add(add);
        left.Children.Add(list);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        var line = new Border { Background = System.Windows.Media.Brushes.Gray, Opacity = 0.25 };
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);
        Content = grid;

        model.ServersChanged += RenderList;
        RenderList();
    }

    static MenuItem Item(string title, Action click)
    {
        var m = new MenuItem { Header = title };
        m.Click += (_, _) => click();
        return m;
    }

    void RenderList()
    {
        list.Items.Clear();
        foreach (var s in model.Servers.List)
        {
            var row = new Grid { Margin = new Thickness(2, 4, 2, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Glyph(s.Client.Provider == Provider.Direct ? "" : "");
            icon.Opacity = 0.6;
            icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = s.Name.Length == 0 ? L("Без названия") : s.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = s.Client.ServerAddress.Length == 0 ? "—" : model.Masked(s.Client.ServerAddress), FontSize = 12, Opacity = 0.65 });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (s.Id == model.Servers.ActiveId)
            {
                var mark = Ui.Glyph("");
                mark.Foreground = Ui.Green;
                mark.ToolTip = L("Активный");
                Grid.SetColumn(mark, 2);
                row.Children.Add(mark);
            }
            var item = new ListBoxItem { Content = row, Tag = s.Id, ContextMenu = RowMenu(s) };
            list.Items.Add(item);
        }
        if (!model.Servers.List.Any(s => s.Id == selectedId)) selectedId = model.Servers.List.FirstOrDefault()?.Id;
        list.SelectedIndex = model.Servers.List.FindIndex(s => s.Id == selectedId);
        ShowDetail();
    }

    ContextMenu RowMenu(Server s)
    {
        var busy = model.Connection.State.IsActive;
        var menu = new ContextMenu();
        var active = Item(L("Сделать активным"), () => model.SetActive(s.Id));
        active.IsEnabled = !busy && s.Id != model.Servers.ActiveId;
        menu.Items.Add(active);
        menu.Items.Add(Item(L("Переименовать"), () =>
        {
            if (Prompt.Ask(L("Переименовать сервер"), L("Имя сервера"), s.Name) is { } name) { model.Servers.Rename(s.Id, name); model.NotifyServers(); }
        }));
        menu.Items.Add(Item(L("Клонировать"), () => { model.Servers.Clone(s.Id); model.NotifyServers(); }));
        menu.Items.Add(new Separator());
        var delete = Item(L("Удалить"), () =>
        {
            if (MessageBox.Show(F("Сервер «%@» и все его настройки будут удалены из приложения.", s.Name), L("Удалить сервер?"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            model.Servers.Remove(s.Id);
            model.NotifyServers();
        });
        // Сервер под живым подключением удалять нельзя.
        delete.IsEnabled = !(busy && s.Id == model.Connection.ActiveServerId);
        menu.Items.Add(delete);
        return menu;
    }

    void ShowDetail()
    {
        (detail.Content as IDisposable)?.Dispose();
        var server = model.Servers.List.FirstOrDefault(s => s.Id == selectedId);
        detail.Content = server is null
            ? new TextBlock { Text = model.Servers.List.Count == 0 ? L("Вставьте ссылку freeturn:// или добавьте сервер вручную") : L("Выберите сервер"), Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) }
            : new ServerDetailView(model, server.Id);
    }

    void PasteFromClipboard()
    {
        if (Clipboard.ContainsText() && FreeturnLink.LooksLikeLink(Clipboard.GetText())) { model.OfferImport(Clipboard.GetText()); return; }
        if (QrImport.FromClipboardImage() is { } link) { model.OfferImport(link); return; }
        AppModel.ShowImportError(L("В буфере обмена нет ссылки freeturn:// или картинки с QR-кодом."));
    }

    void QrFromFile()
    {
        var dialog = new OpenFileDialog { Filter = L("Изображения") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog() != true) return;
        try { Offer(QrImport.FromFile(dialog.FileName)); }
        catch (Exception) { Offer(null); }
    }

    void Offer(string? link)
    {
        if (link is null) AppModel.ShowImportError(L("На картинке нет QR-кода со ссылкой freeturn://."));
        else model.OfferImport(link);
    }

    void AddManual()
    {
        var server = new Server { Name = L("Новый сервер") };
        model.Servers.Add(server, activate: false);
        selectedId = server.Id;
        model.NotifyServers();
    }

    public void Dispose()
    {
        model.ServersChanged -= RenderList;
        (detail.Content as IDisposable)?.Dispose();
    }
}

/// <summary>Маленький диалог «введите значение».</summary>
static class Prompt
{
    public static string? Ask(string title, string label, string value)
    {
        var box = new TextBox { Text = value, MinWidth = 320, Margin = new Thickness(0, 6, 0, 12) };
        var ok = new Button { Content = L("Сохранить"), IsDefault = true, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = L("Отмена"), IsCancel = true, Padding = new Thickness(14, 5, 14, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var window = new Window
        {
            Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ShowInTaskbar = false,
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? box.Text : null;
    }
}
