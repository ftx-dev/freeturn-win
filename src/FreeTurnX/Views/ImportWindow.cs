using System;
using System.Windows;
using System.Windows.Controls;
using FreeTurn.Core;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

/// <summary>Подтверждение импорта: что добавляется и чего не хватает.</summary>
public sealed class ImportWindow : Window
{
    public ImportWindow(AppModel model, ImportDraft draft)
    {
        Title = L("Импорт подключения");
        Width = Math.Min(560, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(600, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(400, Width);
        MinHeight = Math.Min(320, Height);
        MaxHeight = SystemParameters.WorkArea.Height - 16;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var name = new TextBox { Text = draft.SuggestedName, MinWidth = 280 };
        var callLink = new TextBox { Text = draft.CallLink, MinWidth = 280 };
        var access = draft.NeedsWireGuard ? L("нет конфига WireGuard")
            : WGConfig.TryParse(draft.Link.WgConf, out var wg) && wg!.IsAmnezia ? "AmneziaWG" : "WireGuard";

        var rows = new System.Collections.Generic.List<UIElement>
        {
            Ui.Row(L("Имя сервера"), name),
            Ui.Row(L("Адрес"), new TextBlock { Text = model.Masked(draft.Link.Peer), TextWrapping = TextWrapping.Wrap }, stretchControl: true),
            Ui.Row(L("Подключение"), new TextBlock { Text = draft.Link.Provider == Provider.Direct ? L("Напрямую") : L("Через релей") }),
            Ui.Row(L("Тип доступа"), new TextBlock { Text = access }),
        };
        if (draft.Link.ObfProfile.Length > 0 && draft.Link.ObfProfile != ObfProfile.None)
            rows.Add(Ui.Row(L("Обфускация"), new TextBlock { Text = draft.Link.ObfProfile }));
        if (draft.NeedsCallLink)
            rows.Add(Ui.Row(L("Ссылка на звонок"), callLink, draft.CallLink.Length == 0
                ? L("Создайте звонок, скопируйте ссылку и не завершайте его.")
                : L("Ссылка пришла вместе с доступом — можно заменить своей.")));

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Ui.Card([.. rows]));
        if (draft.NeedsWireGuard) panel.Children.Add(Ui.Hint(L("В ссылке нет конфига WireGuard. Сервер сохранится, но для подключения конфиг нужно добавить в его настройках.")));
        if (draft.DuplicateAddress) panel.Children.Add(Ui.Hint(L("Сервер с этим адресом уже есть в списке."), Ui.Orange));
        if (draft.DuplicateConf) panel.Children.Add(Ui.Hint(L("Этот конфиг WireGuard уже импортирован."), Ui.Orange));

        var ok = new Button { Content = L("Импортировать"), IsDefault = true, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current.TryFindResource("SmoothAccentButton") is Style accent) ok.Style = accent;
        var cancel = new Button { Content = L("Отмена"), IsCancel = true, Padding = new Thickness(14, 5, 14, 6) };
        void Validate() => ok.IsEnabled = !draft.NeedsCallLink || callLink.Text.Trim().Length > 0;
        callLink.TextChanged += (_, _) => Validate();
        Validate();
        ok.Click += (_, _) =>
        {
            model.ConfirmImport(draft, name.Text, callLink.Text);
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        // Keep confirmation actions visible even with long links or warnings.
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer
        {
            Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        buttons.Margin = new Thickness(20, 12, 20, 16);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        Content = layout;
    }
}
