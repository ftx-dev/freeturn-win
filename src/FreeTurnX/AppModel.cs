using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FreeTurn.Core;
using FreeTurnX.Services;
using FreeTurnX.Views;
using static FreeTurnX.Strings;

namespace FreeTurnX;

public enum Section { Home, Servers, Routing, Logs, Settings, About }

/// <summary>Состояние приложения: данные на диске и текущее подключение. Живёт в потоке интерфейса.</summary>
public sealed class AppModel : INotifyPropertyChanged
{
    public const string TelegramUrl = "https://t.me/+ZVdbpZB0w_Q1MTdi";
    public const string RepositoryUrl = "https://github.com/ftx-dev/freeturn-win";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ServersChanged;
    public event Action? RoutingChanged;

    public Store Store { get; }
    public ConnectionController Connection { get; }
    public Updater Updater { get; } = new();
    public LogFile LogFile { get; }
    public ObservableCollection<LogLine> Logs { get; } = [];
    const int LogCapacity = 5000;
    long nextLogId;

    public ServersFile Servers { get; private set; } = new();
    public RoutingFile Routing { get; private set; } = RoutingFile.Initial();
    public AppSettings Settings { get; private set; } = new();

    Section section = Section.Home;
    public Section Section { get => section; set { section = value; Changed(); } }

    /// <summary>Сообщение о повреждённом файле данных; пока оно есть, этот файл не перезаписывается.</summary>
    public string? LoadError { get; private set; }
    readonly HashSet<string> readOnlyFiles = [];

    public bool CaptchaNeedsAttention { get; private set; }
    readonly CaptchaPresenter captchaPresenter = new();
    CaptchaWindow? captchaWindow;
    readonly AutoCaptchaHost autoCaptcha = new();

    public bool InstallingDaemon { get; private set; }
    public string? DaemonError { get; private set; }
    public string DaemonStatusText { get; private set; } = "…";
    public int? PingMs { get; private set; }
    readonly SleepWatcher sleepWatcher;

    public AppModel(SynchronizationContext ui, string? dataDirectory = null)
    {
        Store = new Store(dataDirectory ?? Environment.GetEnvironmentVariable("FREETURN_DATA_DIR") ?? Store.DefaultDirectory);
        LogFile = new LogFile(Path.Combine(Store.Directory, "logs"));
        var stateDir = Path.Combine(Store.Directory, "core-state");
        var network = new NetworkWatcher(ui);
        Connection = new ConnectionController(() => new CoreProcess(Paths.CoreExe, stateDir, ui), () => new DaemonClient(ui), network, NetworkWatcher.FreeUdpPort);

        Servers = Load("servers.json", Servers);
        Routing = Load("routing.json", Routing).Normalized();
        Settings = Load("settings.json", Settings);
        L10n.Language = Settings.Language;
        foreach (var server in Servers.List) StreamLimits.Apply(server.Client, Settings.UnsafeStreamLimits);

        Connection.StateChanged += StateChanged;
        Connection.StatsChanged += () => Changed(nameof(Connection));
        Connection.LogReceived += Log;
        Connection.CaptchaChanged += HandleCaptcha;
        sleepWatcher = new SleepWatcher(ui, slept => Connection.SystemDidWake(slept));
        Log(new LogLine(DateTime.Now, LogLevel.Info, LogSource.App,
            $"FreeTurnX {Updater.CurrentVersion} запущен, ядро v4.1.5, сеть {network.Current()?.Name ?? "нет"}"));
    }

    public void Start()
    {
        if (Settings.CheckUpdates && !PackageChannel.IsPackaged) _ = CheckUpdatesOnLaunchAsync();
        if (AutoConnect.ShouldConnect(Settings, HasConnectionTarget)) _ = ConnectAsync();
    }

    T Load<T>(string name, T fallback) where T : class
    {
        try { return Store.Load(name, fallback); }
        catch (StoreCorruptException)
        {
            readOnlyFiles.Add(name);
            LoadError = F("Файл %@ повреждён. Изменения не сохраняются, чтобы не затереть его. Исправьте или удалите файл и перезапустите приложение.", name);
            return fallback;
        }
    }

    public void Persist()
    {
        Save(Servers, "servers.json");
        Save(Routing, "routing.json");
        Save(Settings, "settings.json");
    }

    void Save<T>(T value, string name)
    {
        if (readOnlyFiles.Contains(name)) return;
        try { Store.Save(value, name); } catch (IOException) { }
    }

    public void NotifyServers() { foreach (var server in Servers.List) StreamLimits.Apply(server.Client, Settings.UnsafeStreamLimits); Persist(); ServersChanged?.Invoke(); Changed(nameof(ActiveServer)); }
    public void NotifyRouting() { Persist(); RoutingChanged?.Invoke(); Changed(nameof(ActiveProfile)); }
    public void NotifySettings()
    {
        foreach (var server in Servers.List)
        {
            StreamLimits.Apply(server.Client, Settings.UnsafeStreamLimits);
        }
        Persist();
        ((App)Application.Current).ApplyAnimations();
        Changed(nameof(Settings));
    }

    public Server? ActiveServer => Servers.Active;
    public bool Socks5Mode => Connection.State.IsActive && Connection.IsSocks5;
    public bool HasConnectionTarget => Socks5Mode || ActiveServer is not null;
    public RoutingProfile? ActiveProfile => ActiveServer is { } s ? Routing.ProfileFor(s) : null;

    public void SetActive(string id)
    {
        if (Connection.State.IsActive) return;
        Servers.ActiveId = id;
        NotifyServers();
    }

    public async Task ToggleConnectionAsync()
    {
        if (Connection.State.IsActive)
        {
            // Выключил сам - при следующем запуске не восстанавливаем.
            Settings.WasConnected = false;
            Persist();
            await Connection.DisconnectAsync();
        }
        else await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (Connection.State.IsActive) return;
        if (ActiveServer is not { } server) return;
        // Экранный лог - про текущую сессию; файл на диске хранит и прошлые.
        Logs.Clear();
        await Connection.ConnectAsync(server, Routing.ProfileFor(server), Settings, Store.OwnClientId());
    }

    public async Task ConnectSocks5Async()
    {
        if (Connection.State.IsActive) return;
        Logs.Clear();
        await Connection.ConnectSocks5Async(Settings);
    }

    void StateChanged(ConnectionState state)
    {
        switch (state)
        {
            case ConnectionState.Connected when !Settings.WasConnected:
                Settings.WasConnected = true;
                Persist();
                if (!Connection.IsSocks5) _ = MeasurePingAsync();
                MaybeOfferTelegram();
                break;
            case ConnectionState.Failed:
                // Сбой сам не пройдёт: автоподключение при запуске упёрлось бы в него же.
                Settings.WasConnected = false;
                Persist();
                PingMs = null;
                break;
            case ConnectionState.Off:
                PingMs = null;
                break;
        }
        Changed(nameof(Connection));
        Changed(nameof(PingMs));
    }

    public async Task MeasurePingAsync()
    {
        PingMs = await PingMeter.MeasureAsync();
        Changed(nameof(PingMs));
    }

    // ---- Логи

    /// <summary>Строка лога: на экран и в файл. Секреты прячутся до записи, а не при показе.</summary>
    public void Log(LogLine line)
    {
        var text = line.Text;
        if (Settings.Socks5.Password.Length > 0)
            text = Redactor.Redact(text, [Settings.Socks5.Password], maskAddresses: false);
        if (Settings.PrivacyMode) text = Redactor.Redact(text, Secrets);
        // Команду запуска часто копируют в чаты: ссылку на звонок и ключи прячем всегда.
        else if (text.StartsWith("Команда:")) text = Redactor.Redact(text, Secrets, maskAddresses: false);
        var stored = line with { Text = text, Id = ++nextLogId };
        Logs.Add(stored);
        while (Logs.Count > LogCapacity) Logs.RemoveAt(0);
        LogFile.Append($"{stored.Date:yyyy-MM-dd HH:mm:ss} [{stored.Source.ToString().ToLowerInvariant()}] [{stored.Level.ToString().ToLowerInvariant()}] {text}");
    }

    /// <summary>Секреты активного сервера, которые не ловятся по виду: ключи WireGuard, ссылка на звонок.</summary>
    IEnumerable<string> Secrets
    {
        get
        {
            if (ActiveServer is not { } s) yield break;
            yield return s.Client.CallLink;
            yield return s.Opts.ObfKey;
            yield return s.Client.ClientId;
            if (WGConfig.TryParse(s.Client.WireGuardConfig, out var wg))
            {
                yield return wg!.PrivateKey;
                foreach (var p in wg.Peers) if (p.PresharedKey is { } psk) yield return psk;
            }
        }
    }

    public string RedactedLaunchArgs => Redactor.Redact(Connection.LaunchArgs, Secrets, Settings.PrivacyMode);

    public string Masked(string address) => Settings.PrivacyMode ? "•••••••" : address;

    // ---- Капча

    void HandleCaptcha(CaptchaRequest? request)
    {
        Perform(captchaPresenter.ActionFor(request, Settings.CaptchaTarget));
        CaptchaNeedsAttention = captchaPresenter.NeedsAttention;
        Changed(nameof(CaptchaNeedsAttention));
    }

    /// <summary>Открыть ручную капчу снова - после того как окно закрыли, не пройдя её.</summary>
    public void ReopenCaptcha() => Perform(captchaPresenter.ReopenAction(Settings.CaptchaTarget));

    void Perform(CaptchaAction action)
    {
        switch (action.Kind)
        {
            case CaptchaActionKind.ShowWindow:
                autoCaptcha.Clear();
                captchaWindow?.CloseQuietly();
                captchaWindow = new CaptchaWindow(action.Url);
                captchaWindow.Show();
                captchaWindow.Activate();
                break;
            case CaptchaActionKind.OpenBrowser:
                autoCaptcha.Clear();
                captchaWindow?.CloseQuietly();
                Process.Start(new ProcessStartInfo(action.Url) { UseShellExecute = true });
                break;
            case CaptchaActionKind.LoadHidden:
                captchaWindow?.CloseQuietly();
                autoCaptcha.Show(action.Url);
                break;
            case CaptchaActionKind.Dismiss:
                captchaWindow?.CloseQuietly();
                captchaWindow = null;
                autoCaptcha.Clear();
                break;
        }
    }

    // ---- Служба

    public async Task RefreshDaemonStatusAsync()
    {
        if (Connection.State.IsActive)
        {
            // Служба обслуживает одного клиента - наше же подключение.
            DaemonStatusText = L("Работает");
        }
        else
        {
            var client = new DaemonClient(SynchronizationContext.Current!);
            try
            {
                var status = await client.StatusAsync();
                DaemonStatusText = status.Version == DaemonProtocol.Version ? L("Установлена") : L("Установлена другая версия — нужна переустановка");
            }
            catch (DaemonException e) when (e.Kind == DaemonErrorKind.NotInstalled) { DaemonStatusText = L("Не установлена"); }
            catch (DaemonException e) when (e.Kind == DaemonErrorKind.Busy) { DaemonStatusText = L("Занята другим приложением"); }
            catch (Exception) { DaemonStatusText = L("Не отвечает"); }
            finally { client.Close(); }
        }
        Changed(nameof(DaemonStatusText));
    }

    public Task ReinstallDaemonAsync() => RunDaemonTaskAsync(DaemonInstaller.InstallAsync);
    public Task UninstallDaemonAsync() => RunDaemonTaskAsync(DaemonInstaller.UninstallAsync);

    /// <summary>Ставит службу и, если получилось, сразу продолжает подключение.</summary>
    public async Task InstallDaemonAndConnectAsync()
    {
        await RunDaemonTaskAsync(DaemonInstaller.InstallAsync);
        if (DaemonError is null) await ConnectAsync();
    }

    async Task RunDaemonTaskAsync(Func<Task> work)
    {
        if (InstallingDaemon) return;
        InstallingDaemon = true;
        DaemonError = null;
        Changed(nameof(InstallingDaemon));
        try { await work(); }
        catch (OperationCanceledException) { } // отказал в UAC - не ошибка
        catch (Exception e) { DaemonError = F("Не удалось установить службу: %@", e.Message); }
        InstallingDaemon = false;
        Changed(nameof(InstallingDaemon));
        Changed(nameof(DaemonError));
        await RefreshDaemonStatusAsync();
    }

    // ---- Обновления и Telegram

    async Task CheckUpdatesOnLaunchAsync()
    {
        await Updater.CheckAsync();
        if (Updater.Available is not { } update) return;
        var notes = update.Notes.Length == 0 ? L("Приложение перезапустится. VPN на время обновления отключится.") : update.Notes;
        if (new AppDialog(F("Доступна версия %@", update.Version), notes, L("Обновить"), L("Отмена")).ShowDialog() == true)
            await InstallUpdateAsync(update);
    }

    public async Task InstallUpdateAsync(AvailableUpdate update)
    {
        if (Updater.Phase is UpdatePhase.Downloading or UpdatePhase.Installing) return;
        var status = new System.Windows.Controls.TextBlock { Text = L("Загрузка…"), Margin = new Thickness(0, 0, 0, 12) };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(status);
        var progress = new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Height = 6, Maximum = 100 };
        panel.Children.Add(progress);
        using var cancellation = new CancellationTokenSource();
        var cancel = Ui.Button(L("Отмена"), () => cancellation.Cancel());
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(cancel);
        var window = new Window { Title = F("Обновить до %@", update.Version), Width = 380, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel };
        bool finished = false;
        window.Closing += (_, e) => { if (!finished) { e.Cancel = true; cancellation.Cancel(); } };
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            status.Text = Updater.StatusText;
            progress.IsIndeterminate = Updater.Phase != UpdatePhase.Downloading || Updater.Download.Total is not > 0;
            if (!progress.IsIndeterminate) progress.Value = 100.0 * Updater.Download.Received / Updater.Download.Total!.Value;
        }
        Updater.PropertyChanged += Changed;
        window.Show();
        try { await Updater.InstallAsync(update, () => Connection.DisconnectAsync(),
            () => Application.Current.Dispatcher.BeginInvoke(new Action(((App)Application.Current).Quit)), cancellation.Token); }
        finally { finished = true; Updater.PropertyChanged -= Changed; window.Close(); }
        if (Updater.Phase == UpdatePhase.Failed)
            new AppDialog(L("Не удалось"), Updater.StatusText, "OK", "").ShowDialog();
    }

    /// <summary>Как на Android: один раз предложить подписаться на канал - после первого удачного подключения.</summary>
    void MaybeOfferTelegram()
    {
        if (Settings.TelegramPromptShown) return;
        Settings.TelegramPromptShown = true;
        Persist();
        var answer = new AppDialog(L("Следите за обновлениями"), L("Подпишитесь на Telegram-канал, чтобы первыми узнавать о новых версиях."), L("Открыть"), L("Отмена")).ShowDialog();
        if (answer == true) OpenUrl(TelegramUrl);
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---- Импорт

    public void OfferImport(string raw)
    {
        ImportDraft draft;
        try { draft = ImportPlanner.Draft(FreeturnLink.Parse(raw), Servers.List); }
        catch (ImportException e) when (e.Error == ImportError.TcpModeUnsupported)
        {
            ShowImportError(L("Этот сервер работает в TCP-режиме для Xray, FreeTurnX его не поддерживает."));
            return;
        }
        catch (Exception e) when (e is LinkException or ImportException)
        {
            ShowImportError(L("Некорректная ссылка или неподдерживаемая версия приложения."));
            return;
        }
        Section = Section.Servers;
        var window = new ImportWindow(this, draft) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    public void ConfirmImport(ImportDraft draft, string name, string callLink)
    {
        Servers.Add(ImportPlanner.MakeServer(draft, name, callLink), activate: !Connection.State.IsActive);
        NotifyServers();
    }

    public static void ShowImportError(string message) =>
        new AppDialog(L("Ошибка импорта"), message, "OK", "").ShowDialog();

    // ---- Прочее

    public string DisplayName(RoutingProfile profile) => profile.Id switch
    {
        "builtin-all" => L("Всё через VPN"),
        "builtin-ru" => L("РФ сервисы напрямую"),
        _ => profile.Name,
    };

    public string FailureText(ConnectionFailure failure) => failure switch
    {
        ConnectionFailure.NoAddress => L("У сервера не указан адрес"),
        ConnectionFailure.NoCallLink => L("Для Relay нужна ссылка на звонок"),
        ConnectionFailure.NoWireGuard => L("Добавьте конфиг WireGuard, чтобы подключиться"),
        ConnectionFailure.BadWireGuard => L("Не удалось разобрать конфиг WireGuard"),
        ConnectionFailure.InvalidRule => L("В профиле маршрутов есть некорректное правило"),
        ConnectionFailure.NoNetwork => L("Нет сети"),
        ConnectionFailure.Socks5 s => s.Error switch
        {
            Socks5Error.InvalidSettings => L("Проверьте IP-адрес, порт и авторизацию SOCKS5 в настройках."),
            Socks5Error.Authentication => L("SOCKS5: сервер отклонил авторизацию. Проверьте логин и пароль."),
            Socks5Error.Protocol => L("Сервер не отвечает по протоколу SOCKS5."),
            Socks5Error.NetworkChanged => L("Сеть изменилась. Проверьте адрес телефона и подключитесь снова."),
            _ => L("SOCKS5 недоступен. Включите раздачу на Android и подключите устройства к одной сети."),
        },
        ConnectionFailure.DaemonNotInstalled => L("Нужна фоновая служба"),
        ConnectionFailure.DaemonVersionMismatch => L("Служба устарела"),
        ConnectionFailure.DaemonBusy => L("Службой сейчас управляет другое приложение"),
        ConnectionFailure.Core c => F("Ядро: %@", c.Message),
        ConnectionFailure.Engine e => F("Туннель: %@", e.Message),
        _ => L("Ошибка"),
    };

    /// <summary>Удаляет данные приложения. Только свои файлы поимённо: каталог может быть задан снаружи.</summary>
    public async Task ResetAllAsync()
    {
        await Connection.DisconnectAsync();
        foreach (var name in new[] { "servers.json", "routing.json", "settings.json", "client-id" })
            File.Delete(Path.Combine(Store.Directory, name));
        foreach (var dir in new[] { "logs", "core-state" })
        {
            var path = Path.Combine(Store.Directory, dir);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        readOnlyFiles.Clear();
        LoadError = null;
        Servers = new();
        Routing = RoutingFile.Initial();
        Settings = new();
        Logs.Clear();
        Section = Section.Home;
        ServersChanged?.Invoke();
        RoutingChanged?.Invoke();
        Changed(nameof(Settings));
        Changed(nameof(LoadError));
    }

    public void Shutdown() => sleepWatcher.Dispose();

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
