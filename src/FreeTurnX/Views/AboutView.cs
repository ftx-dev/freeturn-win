using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FreeTurnX.Services;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

/// <summary>О проекте: логотип, версия, ссылки на Telegram-канал и исходный код.</summary>
public sealed class AboutView : UserControl
{
    public AboutView(AppModel model)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 40, 24, 24), MaxWidth = 520 };
        panel.Children.Add(new Image { Source = AppIcons.Logo(model.Settings.AppIcon), Width = 128, Height = 128 });
        panel.Children.Add(new TextBlock { Text = "FreeTurnX", FontSize = 28, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 2) });
        panel.Children.Add(new TextBlock { Text = F("Версия %@", Updater.CurrentVersion) + $" · {Updater.Arch}", Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = L("FreeTurnX проксирует WireGuard и AmneziaWG через TURN-реле звонков или напрямую. Открытый исходный код и никакой телеметрии: все настройки хранятся только на этом компьютере."),
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 18, 0, 22), Opacity = 0.85,
        });
        var tg = Ui.Button(L("Telegram-канал"), () => AppModel.OpenUrl(AppModel.TelegramUrl), accent: true);
        tg.HorizontalAlignment = HorizontalAlignment.Center;
        tg.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(tg);
        var code = Ui.Button(L("Исходный код на GitHub"), () => AppModel.OpenUrl(AppModel.RepositoryUrl));
        code.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(code);
        panel.Children.Add(new TextBlock { Text = L("Ядро — free-turn-proxy, туннель — Amnezia Box и драйвер Wintun от WireGuard."), Opacity = 0.55, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 22, 0, 0) });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
