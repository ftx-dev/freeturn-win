using System;
using System.Linq;
using System.Threading;
using System.Windows;
using FreeTurn.Core;
using FreeTurnX.Services;
using FreeTurnX.Views;

namespace FreeTurnX;

public partial class App : Application
{
    SingleInstance? single;
    public AppModel Model { get; private set; } = null!;
    TrayIcon? tray;
    MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var link = e.Args.FirstOrDefault(FreeturnLink.LooksLikeLink);
        single = new SingleInstance();
        if (!single.IsFirst)
        {
            // Уже запущено: передаём ссылку (или просьбу показать окно) и выходим.
            SingleInstance.Send(link ?? "show");
            Shutdown();
            return;
        }
        var ui = SynchronizationContext.Current!;
        Model = new AppModel(ui);
        ApplyTheme();
        ApplyAnimations();
        AppIcons.UpdateStartMenu(Model.Settings.AppIcon);
        Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppModel.Settings)) AppIcons.UpdateStartMenu(Model.Settings.AppIcon);
        };
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.Invoke(ApplyAnimations);
        };
        single.Listen(message =>
        {
            ShowMainWindow();
            if (FreeturnLink.LooksLikeLink(message)) Model.OfferImport(message);
        }, ui);
        tray = new TrayIcon(Model, ShowMainWindow, Quit);
        if (!e.Args.Contains("--minimized")) ShowMainWindow();
        Model.Start();
        if (link is not null) Model.OfferImport(link);
    }

    public void ApplyTheme() => ThemeMode = Model.Settings.Theme switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    public void ApplyAnimations() => Resources["AppAnimationsEnabled"] = Motion.Allowed;

    public void ShowMainWindow()
    {
        window ??= new MainWindow(Model);
        MainWindow = window;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>«Выйти»: сначала VPN, потом приложение - туннель не должен пережить его.</summary>
    public async void Quit()
    {
        await Model.Connection.DisconnectAsync();
        tray?.Dispose();
        Model.Shutdown();
        single?.Dispose();
        Shutdown();
    }
}
