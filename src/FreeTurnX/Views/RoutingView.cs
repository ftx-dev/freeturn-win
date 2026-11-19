using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeTurn.Core;
using Microsoft.Win32;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

public sealed class RoutingView : UserControl, IDisposable
{
    readonly AppModel model;
    readonly ListBox list = new() { BorderThickness = new Thickness(0) };
    readonly ContentControl editor = new();
    string selectedId;

    public RoutingView(AppModel model)
    {
        this.model = model;
        list.Background = System.Windows.Media.Brushes.Transparent;
        list.ItemContainerStyle = (Style)Application.Current.Resources["SelectionItem"];
        selectedId = model.ActiveProfile?.Id ?? model.Routing.SharedProfileId;
        var add = new Button { Content = "＋  " + L("Добавить профиль"), Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(8, 8, 8, 4) };
        var menu = new ContextMenu();
        var create = new MenuItem { Header = L("Новый профиль") };
        create.Click += (_, _) =>
        {
            var p = new RoutingProfile { Name = L("Новый профиль") };
            model.Routing.Add(p);
            selectedId = p.Id;
            model.NotifyRouting();
        };
        var import = new MenuItem { Header = L("Импорт из файла…") };
        import.Click += (_, _) => Import();
        menu.Items.Add(create);
        menu.Items.Add(import);
        add.ContextMenu = menu;
        add.Click += (_, _) => { menu.PlacementTarget = add; menu.IsOpen = true; };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: string id }) { selectedId = id; ShowEditor(); } };

        var left = new DockPanel();
        left.SetResourceReference(Panel.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        DockPanel.SetDock(add, Dock.Bottom);
        add.Margin = new Thickness(12);
        left.Children.Add(add);
        left.Children.Add(list);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 220, MaxWidth = 360 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        var line = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = System.Windows.Media.Brushes.Gray, Opacity = 0.25 };
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        Grid.SetColumn(editor, 2);
        grid.Children.Add(editor);
        Content = grid;
        if (Ui.Compact)
        {
            left.Height = 180;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            line.Visibility = Visibility.Collapsed;
            Grid.SetColumn(editor, 0);
            Grid.SetRow(editor, 1);
            editor.Height = 600;
            Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        model.RoutingChanged += RenderList;
        RenderList();
    }

    void RenderList()
    {
        list.Items.Clear();
        foreach (var p in model.Routing.Profiles)
        {
            var row = new DockPanel { Margin = new Thickness(2, 4, 2, 4) };
            var icon = Ui.Glyph(p.BuiltIn ? "" : "");
            icon.Opacity = 0.6;
            icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            if (p.Id == model.Routing.SharedProfileId)
            {
                var shared = new TextBlock { Text = L("общий"), Margin = new Thickness(12, 0, 0, 0), FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(shared, Dock.Right);
                row.Children.Add(shared);
            }
            row.Children.Add(new TextBlock { Text = model.DisplayName(p), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(new ListBoxItem { Content = row, Tag = p.Id });
        }
        if (!model.Routing.Profiles.Any(p => p.Id == selectedId)) selectedId = model.Routing.SharedProfileId;
        list.SelectedIndex = model.Routing.Profiles.FindIndex(p => p.Id == selectedId);
        ShowEditor();
    }

    void ShowEditor()
    {
        var scroll = Content as ScrollViewer;
        var offset = scroll?.VerticalOffset ?? 0;
        editor.Content = model.Routing.Profiles.FirstOrDefault(p => p.Id == selectedId) is { } profile
            ? new ProfileEditor(model, profile, id => { selectedId = id; model.NotifyRouting(); }) : null;
        scroll?.UpdateLayout();
        scroll?.ScrollToVerticalOffset(offset);
    }

    void Import()
    {
        var d = new OpenFileDialog { Filter = "JSON|*.json" };
        if (d.ShowDialog() != true) return;
        try
        {
            var profile = ProfileTransfer.Import(File.ReadAllBytes(d.FileName));
            model.Routing.Add(profile);
            selectedId = profile.Id;
            model.NotifyRouting();
        }
        catch (ProfileTransferException e)
        {
            new AppDialog(L("Не удалось"), e.Message == ProfileTransfer.UnsupportedVersion ? L("Файл создан более новой версией приложения.") : L("Это не файл профиля маршрутов FreeTurn."), "OK", "").ShowDialog();
        }
    }

    public void Dispose() => model.RoutingChanged -= RenderList;
}

sealed class ProfileEditor : UserControl
{
    readonly AppModel model;
    readonly RoutingProfile profile;
    readonly Action<string> select;

    public ProfileEditor(AppModel model, RoutingProfile profile, Action<string> select)
    {
        this.model = model;
        this.profile = profile.Copy();
        this.select = select;
        Render();
    }

    void Mutate(Action<RoutingProfile> change)
    {
        change(profile);
        model.Routing.Update(profile.Copy());
        model.NotifyRouting();
    }

    void Render()
    {
        var root = new DockPanel { Margin = new Thickness(20, 12, 20, 16) };
        var top = new StackPanel();
        if (profile.BuiltIn) top.Children.Add(Ui.Hint(L("Встроенный профиль нельзя менять. Клонируйте его, чтобы настроить под себя.")));
        else
        {
            var name = new TextBox { Text = profile.Name, Margin = new Thickness(0, 0, 0, 10) };
            name.LostFocus += (_, _) => { if (name.Text != profile.Name) Mutate(p => p.Name = name.Text); };
            top.Children.Add(name);
        }
        var mode = Ui.Choice(profile.DefaultAction, [(RouteAction.Vpn, L("Всё через VPN, кроме выбранного")), (RouteAction.Direct, L("Только выбранное через VPN"))],
            v => Mutate(p => p.DefaultAction = v), width: 320);
        mode.IsEnabled = !profile.BuiltIn;
        mode.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(mode);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
        var share = Ui.Button(L("Сделать общим"), () => { model.Routing.SharedProfileId = profile.Id; model.NotifyRouting(); });
        share.IsEnabled = model.Routing.SharedProfileId != profile.Id;
        share.ToolTip = L("Общий профиль действует для серверов без собственного.");
        actions.Children.Add(share);
        actions.Children.Add(Ui.ActionIcon(L("Клонировать"), "M6,6 L18,6 18,20 6,20 Z M3,16 L2,16 2,2 14,2 14,3", () => { if (model.Routing.Clone(profile.Id) is { } c) select(c.Id); }));
        actions.Children.Add(Ui.ActionIcon(L("Экспорт…"), "M10,14 L10,1 M5,6 L10,1 15,6 M2,12 L2,19 18,19 18,12", Export));
        if (!profile.BuiltIn)
            actions.Children.Add(Ui.ActionIcon(L("Удалить"), "M2,4 L18,4 M7,4 L7,1 13,1 13,4 M4,4 L5,20 15,20 16,4 M8,8 L8,16 M12,8 L12,16", () => { model.Routing.Remove(profile.Id); select(model.Routing.SharedProfileId); }, Ui.Red));
        top.Children.Add(actions);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var addRule = Ui.Button("＋  " + L("Добавить правило"), () => Edit(new RoutingRule { Kind = RuleKind.App, Action = profile.DefaultAction == RouteAction.Vpn ? RouteAction.Direct : RouteAction.Vpn }));
        addRule.IsEnabled = !profile.BuiltIn;
        bottom.Children.Add(addRule);
        if (Ui.Compact) DockPanel.SetDock(addRule, Dock.Top);
        var stack = Ui.Choice(model.Settings.TunStack, [("mixed", "mixed"), ("system", "system"), ("gvisor", "gvisor")], v => { model.Settings.TunStack = v; model.NotifySettings(); }, width: 120);
        var stackRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        stackRow.Children.Add(new TextBlock { Text = L("Тип туннеля"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        stackRow.Children.Add(stack);
        bottom.Children.Add(stackRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        if (model.Connection.State.IsActive)
        {
            var note = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var re = new Button { Content = L("Переподключить"), Padding = new Thickness(12, 6, 12, 6) };
            re.Click += async (_, _) =>
            {
                re.IsEnabled = false;
                re.Content = L("Переподключение...");
                try
                {
                    await model.Connection.DisconnectAsync();
                    await model.ConnectAsync();
                }
                finally
                {
                    re.Content = L("Переподключить");
                    re.IsEnabled = true;
                }
            };
            DockPanel.SetDock(re, Dock.Right);
            note.Children.Add(re);
            note.Children.Add(Ui.Hint(L("Изменения применятся при следующем подключении.")));
            DockPanel.SetDock(note, Dock.Bottom);
            root.Children.Add(note);
        }

        if (profile.Rules.Count == 0)
        {
            // Пустой список - одна заглушка на ровном фоне, без полос пустых строк.
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var g = Ui.Glyph("", 40);
            g.Opacity = 0.5;
            g.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(g);
            empty.Children.Add(new TextBlock { Text = L("Правил нет"), FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
            empty.Children.Add(new TextBlock { Text = L("Весь трафик идёт по режиму профиля."), Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center });
            root.Children.Add(empty);
        }
        else
        {
            var rules = new StackPanel();
            for (var i = 0; i < profile.Rules.Count; i++) rules.Children.Add(RuleRow(profile.Rules[i], i));
            root.Children.Add(new ScrollViewer { Content = rules, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        Content = root;
    }

    UIElement RuleRow(RoutingRule rule, int index)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var enabled = Ui.Switch(rule.Enabled, v => Mutate(p => p.Rules[index].Enabled = v), enabled: !profile.BuiltIn);
        row.Children.Add(enabled);
        var icon = Ui.Glyph(rule.Kind switch { RuleKind.App => "", RuleKind.Cidr => "", _ => "" });
        icon.Opacity = 0.6;
        icon.Margin = new Thickness(6, 0, 10, 0);
        row.Children.Add(icon);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (!EngineConfig.IsValid(rule))
        {
            var bad = Ui.Glyph("");
            bad.Foreground = Ui.Red;
            bad.Margin = new Thickness(0, 0, 8, 0);
            right.Children.Add(bad);
        }
        var action = Ui.Choice(rule.Action, ActionOptions, v => Mutate(p => p.Rules[index].Action = v), width: 150);
        action.IsEnabled = !profile.BuiltIn;
        right.Children.Add(action);
        if (!profile.BuiltIn)
        {
            right.Children.Add(Small("", L("Изменить"), () => Edit(rule)));
            right.Children.Add(Small("", L("Выше"), () => Move(index, -1), index > 0));
            right.Children.Add(Small("", L("Ниже"), () => Move(index, 1), index < profile.Rules.Count - 1));
            right.Children.Add(Small("", L("Удалить"), () => Mutate(p => p.Rules.RemoveAt(index))));
        }
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Opacity = rule.Enabled ? 1 : 0.5 };
        text.Children.Add(new TextBlock { Text = rule.Kind == RuleKind.App ? AppName(rule.Value) : rule.Value, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = rule.Kind == RuleKind.App ? rule.Value : KindTitle(rule.Kind), FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(text);
        var card = Ui.Card(row);
        card.Padding = new Thickness(12, 8, 12, 8);
        return card;
    }

    static string AppName(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(path)
        : Path.GetFileName(path.TrimEnd('\\'));

    static Button Small(string glyph, string tip, Action click, bool enabled = true)
    {
        var b = new Button { Content = Ui.Glyph(glyph, 13), ToolTip = tip, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    void Move(int index, int delta) => Mutate(p =>
    {
        var r = p.Rules[index];
        p.Rules.RemoveAt(index);
        p.Rules.Insert(index + delta, r);
    });

    void Edit(RoutingRule rule)
    {
        var window = new RuleEditorWindow(rule.Copy()) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true) return;
        var saved = window.Rule;
        Mutate(p =>
        {
            var i = p.Rules.FindIndex(r => r.Id == saved.Id);
            if (i >= 0) p.Rules[i] = saved; else p.Rules.Add(saved);
        });
    }

    void Export()
    {
        var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = model.DisplayName(profile) + ".json" };
        if (d.ShowDialog() == true) File.WriteAllBytes(d.FileName, ProfileTransfer.Export(profile));
    }

    public static readonly (RouteAction, string)[] ActionOptions = [(RouteAction.Vpn, L("Через VPN")), (RouteAction.Direct, L("Напрямую")), (RouteAction.Block, L("Блокировать"))];

    public static string KindTitle(RuleKind kind) => kind switch
    {
        RuleKind.App => L("Приложение"),
        RuleKind.Domain => L("Домен"),
        RuleKind.DomainSuffix => L("Домен и поддомены"),
        RuleKind.DomainKeyword => L("Слово в домене"),
        _ => L("IP или подсеть"),
    };
}

static class RuleCopy
{
    public static RoutingRule Copy(this RoutingRule r) => r with { };
}

/// <summary>Создание и правка одного правила.</summary>
sealed class RuleEditorWindow : Window
{
    public RoutingRule Rule { get; private set; }

    public RuleEditorWindow(RoutingRule rule)
    {
        Rule = rule;
        Title = L("Правило");
        Width = Math.Min(560, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(390, SystemParameters.WorkArea.Height - 32);
        MaxHeight = SystemParameters.WorkArea.Height - 16;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Render();
    }

    void Render()
    {
        var ok = new Button { Content = L("Сохранить"), IsDefault = true, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current.TryFindResource("SmoothAccentButton") is Style accent) ok.Style = accent;
        var error = Ui.Hint(L("Такое значение движок не примет."), Ui.Red);
        void Validate()
        {
            var valid = EngineConfig.IsValid(Rule);
            ok.IsEnabled = valid;
            error.Visibility = valid || Rule.Value.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        var kind = Ui.Choice(Rule.Kind, [.. Enum.GetValues<RuleKind>().Select(k => (k, ProfileEditor.KindTitle(k)))], k =>
        {
            // Значение одного типа для другого бессмысленно.
            Rule = Rule with { Kind = k, Value = "" };
            Render();
        });
        UIElement valueControl;
        if (Rule.Kind == RuleKind.App)
        {
            var path = new TextBlock { Text = Rule.Value.Length == 0 ? "—" : Rule.Value, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var panel = new StackPanel();
            path.HorizontalAlignment = HorizontalAlignment.Left;
            path.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(path);
            var choices = new WrapPanel();
            panel.Children.Add(choices);
            choices.Children.Add(Ui.Button(L("Запущенные…"), () =>
            {
                var picker = new AppPickerWindow { Owner = this };
                if (picker.ShowDialog() == true) { Rule = Rule with { Value = picker.Picked! }; Render(); }
            }));
            choices.Children.Add(Ui.Button(L("Файл…"), () =>
            {
                var d = new OpenFileDialog { Filter = L("Программы") + " (*.exe)|*.exe" };
                if (d.ShowDialog() == true) { Rule = Rule with { Value = d.FileName }; Render(); }
            }));
            choices.Children.Add(Ui.Button(L("Папка…"), () =>
            {
                var d = new OpenFolderDialog();
                if (d.ShowDialog() == true) { Rule = Rule with { Value = d.FolderName.TrimEnd('\\') + "\\" }; Render(); }
            }));
            valueControl = panel;
        }
        else
        {
            var placeholder = Rule.Kind switch { RuleKind.Cidr => "10.0.0.0/8", RuleKind.DomainKeyword => "google", _ => "example.com" };
            valueControl = Ui.Field(Rule.Value, v => { Rule = Rule with { Value = v }; Validate(); }, 260, placeholder);
        }
        var action = Ui.Choice(Rule.Action, ProfileEditor.ActionOptions, v => Rule = Rule with { Action = v });

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Ui.Card(
            Ui.Row(L("Что"), kind),
            Ui.Row(L("Значение"), valueControl, Rule.Kind == RuleKind.App ? L("Программа (.exe) или папка: тогда правило действует на все программы внутри.") : null, stretchControl: true),
            Ui.Row(L("Действие"), action)));
        root.Children.Add(error);
        var cancel = new Button { Content = L("Отмена"), IsCancel = true, Padding = new Thickness(14, 5, 14, 6) };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var layout = new DockPanel();
        buttons.Margin = new Thickness(20, 8, 20, 16);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        Validate();
    }
}

/// <summary>Выбор из запущенных программ: путь к .exe берётся у процесса.</summary>
sealed class AppPickerWindow : Window
{
    public string? Picked { get; private set; }

    public AppPickerWindow()
    {
        Title = L("Запущенные программы");
        Width = Math.Min(520, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(560, SystemParameters.WorkArea.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var search = new TextBox { Margin = new Thickness(12, 12, 12, 8) };
        var list = new ListBox { Margin = new Thickness(12, 0, 12, 12) };
        // Процессы других пользователей и служб не отдают путь - их пропускаем.
        var apps = Process.GetProcesses()
            .Select(p => { try { return (name: p.ProcessName, path: p.MainModule?.FileName); } catch (Exception) { return (name: p.ProcessName, path: null); } finally { p.Dispose(); } })
            .Where(a => a.path is not null && !a.path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            .DistinctBy(a => a.path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        void Fill()
        {
            list.Items.Clear();
            foreach (var a in apps.Where(a => search.Text.Length == 0 || a.name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
                row.Children.Add(new TextBlock { Text = a.name, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = a.path, FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
                list.Items.Add(new ListBoxItem { Content = row, Tag = a.path });
            }
        }
        search.TextChanged += (_, _) => Fill();
        list.MouseDoubleClick += (_, _) => Pick(list);
        var ok = new Button { Content = L("Выбрать"), IsDefault = true, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(12, 0, 12, 12), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Pick(list);
        if (Application.Current.TryFindResource("SmoothAccentButton") is Style accent) ok.Style = accent;
        var root = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(ok, Dock.Bottom);
        root.Children.Add(search);
        root.Children.Add(ok);
        root.Children.Add(list);
        Content = root;
        Fill();
    }

    void Pick(ListBox list)
    {
        if (list.SelectedItem is not ListBoxItem { Tag: string path }) return;
        Picked = path;
        DialogResult = true;
    }
}
