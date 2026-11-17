using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FreeTurn.Core;
using FreeTurnX.Services;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

public sealed class SettingsView : UserControl, IDisposable
{
    readonly AppModel model;
    readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    string? loginItemError;

    public SettingsView(AppModel model)
    {
        this.model = model;
        Content = scroll;
        model.PropertyChanged += OnChanged;
        model.Updater.PropertyChanged += OnChanged;
        Render();
        _ = model.RefreshDaemonStatusAsync();
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppModel.DaemonStatusText) or nameof(AppModel.InstallingDaemon) or nameof(AppModel.DaemonError)
            or nameof(Updater.Phase) or nameof(AppModel.Settings)) Render();
    }

    void Set(Action<AppSettings> change)
    {
        change(model.Settings);
        model.NotifySettings();
    }

    void Render()
    {
        var offset = scroll.VerticalOffset;
        var s = model.Settings;
        var manualCaptcha = model.ActiveServer?.Client.ManualCaptcha == true;
        var page = new System.Collections.Generic.List<UIElement>
        {
            Ui.Header(L("Интерфейс")),
            Ui.Card(
                Ui.Row(L("Тема"), Ui.Choice(s.Theme, [("system", L("Системная")), ("light", L("Светлая")), ("dark", L("Тёмная"))], v =>
                {
                    Set(x => x.Theme = v);
                    ((App)Application.Current).ApplyTheme();
                })),
                Ui.Row(L("Язык"), Ui.Choice(s.Language, [("system", L("Как в системе")), ("ru", "Русский"), ("en", "English")], v => Set(x => x.Language = v)),
                    s.Language != L10n.Language ? L("Язык сменится после перезапуска приложения.") : null),
                Ui.Row(L("Иконка"), IconPicker()),
                Ui.Row(L("Отключить анимации"), Ui.Switch(s.DisableAnimations, v => Set(x => x.DisableAnimations = v))),
                Ui.Row(L("Компактный режим (экспериментальный)"), Ui.Switch(s.CompactMode, v => Set(x => x.CompactMode = v)), L("Окно 400 × 600 с навигацией сверху.")),
                Ui.Row(L("Приватный режим"), Ui.Switch(s.PrivacyMode, v => Set(x => x.PrivacyMode = v)),
                    L("Скрывает адреса, ключи и ссылку на звонок на экране и в логах."))),

            Ui.Header(L("Подключение")),
            Ui.Card(
                Ui.Row(L("Запускать при входе в систему"), Ui.Switch(LoginItem.IsEnabled, v =>
                {
                    try { LoginItem.Set(v); loginItemError = null; }
                    catch (Exception e) { loginItemError = e.Message; }
                    Render();
                }), loginItemError, loginItemError is null ? null : Ui.Red),
                Ui.Row(L("Подключаться при запуске"), Ui.Switch(s.AutoConnect, v => Set(x => x.AutoConnect = v)),
                    L("Восстанавливает подключение, если вы не выключали его сами."))),

            Ui.Header("CAPTCHA"),
            Ui.Card(
                Ui.Row(L("Открывать капчу"), Ui.Choice(s.CaptchaTarget, [("window", L("В окне приложения")), ("browser", L("В браузере"))], v => Set(x => x.CaptchaTarget = v))),
                Ui.Row(L("Продвинутое решение CAPTCHA"), Ui.Switch(s.AdvancedCaptcha, v => Set(x => x.AdvancedCaptcha = v), enabled: !manualCaptcha),
                    manualCaptcha ? L("Недоступно, пока у активного сервера включён ручной ввод капчи.")
                                  : L("Проверку в фоне проходит встроенный браузер. Если не получится, откроется ручная капча."))),

            Ui.Header(L("Обновления")),
            Ui.Card(
                Ui.Row(L("Версия"), new TextBlock { Text = $"{Updater.CurrentVersion} · {Updater.Arch}" }),
                Ui.Row(L("Проверять при запуске"), Ui.Switch(s.CheckUpdates, v => Set(x => x.CheckUpdates = v)),
                    L("Новые версии берутся из GitHub Releases, под процессор этого компьютера.")),
                Ui.Row(model.Updater.StatusText.Length == 0 ? " " : model.Updater.StatusText, UpdateButton(),
                    descriptionColor: null)),

            Ui.Header(L("Фоновая служба")),
            Ui.Card(
                Ui.Row(L("Состояние"), new TextBlock { Text = model.DaemonStatusText }),
                Ui.Row(model.DaemonError ?? " ", DaemonButtons())),

            Ui.Header(L("Отладка")),
            Ui.Card(DebugRows()),

            Ui.Header(L("Сброс")),
            Ui.Card(Ui.Row(L("Сбросить все настройки"), Ui.Button(L("Сбросить"), async () =>
            {
                if (MessageBox.Show(L("Подключение остановится, серверы, профили маршрутов и логи будут удалены."), L("Сбросить все настройки?"),
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK) await model.ResetAllAsync();
            }, danger: true))),
        };
        var updated = Ui.Page([.. page]);
        var content = updated.Content;
        updated.Content = null;
        scroll.Content = content;
        // Updating a checkbox rebuilds its rows, not the scroll container.
        scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
    }

    /// <summary>Три превью-кнопки: выбранная подсвечена.</summary>
    UIElement IconPicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (variant, title) in new[] { ("standard", "Classic"), ("black", "Black"), ("x", "X") })
        {
            var content = new StackPanel();
            content.Children.Add(new Image { Source = AppIcons.Logo(variant), Width = 44, Height = 44 });
            content.Children.Add(new TextBlock { Text = title, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            var button = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = content, IsChecked = model.Settings.AppIcon == variant, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(6, 0, 0, 0),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Click += (_, _) => Set(x => x.AppIcon = variant);
            panel.Children.Add(button);
        }
        return panel;
    }

    UIElement UpdateButton() => model.Updater.Available is { } update
        ? Ui.Button(F("Обновить до %@", update.Version), async () => await model.InstallUpdateAsync(update), accent: true)
        : Ui.Button(L("Проверить сейчас"), async () => await model.Updater.CheckAsync());

    UIElement DaemonButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, IsEnabled = !model.InstallingDaemon && !model.Connection.State.IsActive };
        panel.Children.Add(Ui.Button(L("Переустановить"), async () => await model.ReinstallDaemonAsync()));
        panel.Children.Add(Ui.Button(L("Удалить службу"), async () => await model.UninstallDaemonAsync()));
        return panel;
    }

    UIElement[] DebugRows()
    {
        var rows = new System.Collections.Generic.List<UIElement>
        {
            Ui.Row(L("Показывать отладочную информацию"), Ui.Switch(model.Settings.DebugScreen, v => Set(x => x.DebugScreen = v))),
        };
        if (!model.Settings.DebugScreen) return [.. rows];
        rows.Add(Ui.Row(L("Ядро"), new TextBlock { Text = model.Connection.CoreVersion.Length == 0 ? "v4.1.5" : model.Connection.CoreVersion }));
        rows.Add(Ui.Row(L("Движок"), new TextBlock { Text = "Amnezia Box v1.260910.0" }));
        rows.Add(Ui.Row(L("Каталог данных"), new TextBox { Text = model.Store.Directory, IsReadOnly = true, MinWidth = 320 }));
        if (model.Connection.LaunchArgs.Length > 0)
            rows.Add(new TextBox { Text = model.RedactedLaunchArgs, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11 });
        return [.. rows];
    }

    public void Dispose()
    {
        model.PropertyChanged -= OnChanged;
        model.Updater.PropertyChanged -= OnChanged;
    }
}
