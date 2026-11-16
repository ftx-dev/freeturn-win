using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FreeTurn.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

/// <summary>WebView2 для страницы капчи ядра: отдельный профиль на каждую проверку и никаких переходов наружу.</summary>
static class CaptchaWeb
{
    public static WebView2 Create(string url)
    {
        // Чистый профиль: куки не переживают проверку, как на Android.
        var dataDir = Path.Combine(Path.GetTempPath(), "FreeTurnX-captcha-" + Guid.NewGuid().ToString("N"));
        var web = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = dataDir } };
        web.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) return;
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // Ядро переписывает страницы провайдера так, что всё идёт через его локальный порт.
            // Любой другой адрес - попытка увести браузер наружу.
            core.NavigationStarting += (_, n) => { if (!CaptchaUrl.IsLocal(n.Uri) && n.Uri != "about:blank") n.Cancel = true; };
            core.NewWindowRequested += (_, n) =>
            {
                n.Handled = true;
                if (CaptchaUrl.IsLocal(n.Uri)) core.Navigate(n.Uri);
            };
            if (CaptchaUrl.IsLocal(url)) core.Navigate(url);
        };
        _ = web.EnsureCoreWebView2Async();
        web.Unloaded += (_, _) =>
        {
            web.Dispose();
            try { Directory.Delete(dataDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        };
        return web;
    }
}

/// <summary>Окно ручной капчи: поверх остальных окон, открывается и закрывается само.</summary>
public sealed class CaptchaWindow : Window
{
    bool closingQuietly;

    public CaptchaWindow(string url)
    {
        Title = L("Проверка на робота");
        Width = 460;
        Height = 680;
        MinWidth = 400;
        MinHeight = 520;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var hint = new TextBlock { Text = L("Пройдите проверку для продолжения. Окно закроется само."), Margin = new Thickness(14, 10, 14, 10), Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(hint);
        var web = CaptchaWeb.Create(url);
        Grid.SetRow(web, 1);
        grid.Children.Add(web);
        Content = grid;
    }

    /// <summary>Капча решена - закрываем мы, а не пользователь.</summary>
    public void CloseQuietly()
    {
        closingQuietly = true;
        Close();
    }

    public bool ClosedByUser => !closingQuietly;
}

/// <summary>Автоматическая («продвинутая») капча: страницу грузит настоящий браузерный движок в окне
/// за пределами экрана. Живёт только на время проверки.</summary>
public sealed class AutoCaptchaHost
{
    Window? window;

    public void Show(string url)
    {
        Clear();
        // WebView2 нужно окно; без него проверка не выполнится.
        window = new Window
        {
            Left = -10000, Top = -10000, Width = 460, Height = 680, ShowInTaskbar = false, ShowActivated = false,
            WindowStyle = WindowStyle.None, Content = CaptchaWeb.Create(url),
        };
        window.Show();
    }

    public void Clear()
    {
        window?.Close();
        window = null;
    }
}
