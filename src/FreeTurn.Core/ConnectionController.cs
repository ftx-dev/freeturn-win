namespace FreeTurn.Core;

/// <summary>Почему подключение не состоялось или оборвалось. Текст для человека даёт интерфейс.</summary>
public abstract record ConnectionFailure
{
    public sealed record NoAddress : ConnectionFailure;
    public sealed record NoCallLink : ConnectionFailure;
    public sealed record NoWireGuard : ConnectionFailure;
    public sealed record BadWireGuard : ConnectionFailure;
    public sealed record InvalidRule : ConnectionFailure;
    public sealed record NoNetwork : ConnectionFailure;
    public sealed record DaemonNotInstalled : ConnectionFailure;
    public sealed record DaemonVersionMismatch : ConnectionFailure;
    public sealed record DaemonBusy : ConnectionFailure;
    public sealed record Core(string Message) : ConnectionFailure;
    public sealed record Engine(string Message) : ConnectionFailure;
}

public abstract record ConnectionState
{
    public sealed record Off : ConnectionState;
    public sealed record Starting : ConnectionState;
    public sealed record Connecting(int Streams, int Total) : ConnectionState;
    public sealed record CaptchaWait : ConnectionState;
    public sealed record Connected(int Streams, int Total) : ConnectionState;
    public sealed record Failed(ConnectionFailure Reason) : ConnectionState;
    public sealed record Stopping : ConnectionState;

    public bool IsActive => this is not (Off or Failed);
}

/// <summary>Ведёт одно подключение: служба -> ядро -> (после первого connected) движок.
/// До первого connected системная сеть не трогается: капча и подбор реле идут без туннеля.
/// Все методы зовутся из потока интерфейса.</summary>
public sealed class ConnectionController(Func<ICoreControl> makeCore, Func<IEngineControl> makeEngine, INetworkWatch network, Func<int> portPicker)
{
    ConnectionState state = new ConnectionState.Off();
    public ConnectionState State
    {
        get => state;
        private set { if (state != value) { state = value; StateChanged?.Invoke(value); } }
    }
    public TrafficStats Stats { get; private set; } = new();
    public DateTime? ConnectedSince { get; private set; }
    public CaptchaRequest? Captcha { get; private set; }
    public string CoreVersion { get; private set; } = "";
    /// <summary>Эквивалентная CLI-команда ядра - для экрана отладки.</summary>
    public string LaunchArgs { get; private set; } = "";
    public string? ActiveServerId { get; private set; }

    public event Action<ConnectionState>? StateChanged;
    public event Action? StatsChanged;
    public event Action<LogLine>? LogReceived;
    /// <summary>Капча появилась, сменилась или снята (null).</summary>
    public event Action<CaptchaRequest?>? CaptchaChanged;

    ICoreControl? core;
    IEngineControl? engine;
    string engineConfig = "";
    bool engineUp;
    NetworkSnapshot? lastNetwork;
    bool networkLost;
    /// <summary>Номер сессии: события прошлого ядра не должны оживлять новое подключение.</summary>
    int session;

    bool Running => State.IsActive && State is not ConnectionState.Stopping;

    public async Task ConnectAsync(Server server, RoutingProfile profile, AppSettings settings, string ownClientId)
    {
        if (State.IsActive) return;
        var client = server.Client;
        if (string.IsNullOrWhiteSpace(client.ServerAddress)) { Fail(new ConnectionFailure.NoAddress()); return; }
        if (client.Provider == Provider.Relay && string.IsNullOrWhiteSpace(client.CallLink)) { Fail(new ConnectionFailure.NoCallLink()); return; }
        if (!client.WireGuardActive) { Fail(new ConnectionFailure.NoWireGuard()); return; }
        if (!WGConfig.TryParse(client.WireGuardConfig, out var wg)) { Fail(new ConnectionFailure.BadWireGuard()); return; }
        var net = network.Current();
        if (net is null) { Fail(new ConnectionFailure.NoNetwork()); return; }

        int port;
        string builtEngineConfig;
        try
        {
            port = portPicker();
            builtEngineConfig = EngineConfig.Build(wg!, port, profile, new EngineSettings(settings.TunStack, Debug: client.DebugMode));
        }
        catch (InvalidRuleException) { Fail(new ConnectionFailure.InvalidRule()); return; }
        catch (Exception e) { Fail(new ConnectionFailure.Core(e.Message)); return; }

        var mySession = ++session;
        State = new ConnectionState.Starting();
        ActiveServerId = server.Id;

        var newEngine = makeEngine();
        try
        {
            var status = await newEngine.StatusAsync();
            if (status.Version != DaemonProtocol.Version) throw new DaemonException(DaemonErrorKind.VersionMismatch, status.Version);
        }
        catch (Exception e)
        {
            newEngine.Close();
            if (mySession != session) return;
            Fail(e is DaemonException d ? d.Kind switch
            {
                DaemonErrorKind.NotInstalled => new ConnectionFailure.DaemonNotInstalled(),
                DaemonErrorKind.VersionMismatch => new ConnectionFailure.DaemonVersionMismatch(),
                DaemonErrorKind.Busy => new ConnectionFailure.DaemonBusy(),
                _ => new ConnectionFailure.Engine(d.Message),
            } : new ConnectionFailure.Engine(e.Message));
            return;
        }
        // Пока ждали службу, пользователь мог нажать «отключить».
        if (mySession != session || State is not ConnectionState.Starting) { newEngine.Close(); return; }

        newEngine.EventReceived += ev => { if (session == mySession) _ = HandleEngineEventAsync(ev); };
        var newCore = makeCore();
        newCore.EventReceived += ev => { if (session == mySession) _ = HandleCoreEventAsync(ev); };
        engine = newEngine;
        core = newCore;
        engineConfig = builtEngineConfig;
        engineUp = false;
        lastNetwork = net;
        networkLost = false;

        try { newCore.Launch(); }
        catch (Exception e) { await TeardownAsync(new ConnectionFailure.Core(e.Message)); return; }
        var coreConfig = CoreConfig.Build(server, port, ownClientId, net.Dns, settings.AdvancedCaptcha);
        newCore.Send(new CoreCommand.Args(coreConfig));
        newCore.Send(new CoreCommand.Start(coreConfig, net.IfIndex));

        network.Changed += OnNetworkChanged;
        network.Start();
    }

    public async Task DisconnectAsync()
    {
        if (!Running) return;
        await TeardownAsync(null);
    }

    void OnNetworkChanged(NetworkSnapshot? snapshot) => NetworkChanged(snapshot);

    public async Task HandleCoreEventAsync(CoreEvent ev)
    {
        if (!Running) return;
        switch (ev)
        {
            case CoreEvent.Hello h: CoreVersion = h.Version; break;
            case CoreEvent.Args a:
                LaunchArgs = a.Text;
                Log(LogLevel.Info, LogSource.App, "Команда: " + a.Text);
                break;
            case CoreEvent.Log l: LogReceived?.Invoke(new LogLine(l.Date, l.Level, LogSource.Core, l.Msg)); break;
            case CoreEvent.Stats s: Stats = s.Traffic; StatsChanged?.Invoke(); break;
            case CoreEvent.Captcha c: SetCaptcha(c.Url.Length == 0 ? null : new CaptchaRequest(c.Url, c.Manual)); break;
            case CoreEvent.Error e:
                if (e.Cmd == "start") await TeardownAsync(new ConnectionFailure.Core(e.Err));
                else Log(LogLevel.Error, LogSource.Core, $"{e.Cmd}: {e.Err}");
                break;
            case CoreEvent.Exited: await TeardownAsync(new ConnectionFailure.Core("процесс ядра завершился")); break;
            case CoreEvent.State st:
                switch (st.Phase)
                {
                    case CorePhase.Connecting: State = new ConnectionState.Connecting(st.Streams, st.Total); break;
                    case CorePhase.Captcha: State = new ConnectionState.CaptchaWait(); break;
                    case CorePhase.Error: await TeardownAsync(new ConnectionFailure.Core(st.Err)); break;
                    // Ядро остановилось не по нашей команде.
                    case CorePhase.Idle: await TeardownAsync(new ConnectionFailure.Core(st.Err.Length == 0 ? "ядро остановилось" : st.Err)); break;
                    case CorePhase.Connected:
                        if (!engineUp)
                        {
                            engineUp = true;
                            var mySession = session;
                            try { await engine!.StartAsync(engineConfig); }
                            catch (Exception e)
                            {
                                if (mySession == session) await TeardownAsync(new ConnectionFailure.Engine(e.Message));
                                return;
                            }
                            if (mySession != session || !Running) return;
                            ConnectedSince = DateTime.Now;
                        }
                        State = new ConnectionState.Connected(st.Streams, st.Total);
                        break;
                }
                break;
        }
    }

    public async Task HandleEngineEventAsync(EngineEvent ev)
    {
        if (!Running) return;
        switch (ev)
        {
            case EngineEvent.Log l: Log(LogLevel.Info, LogSource.Engine, l.Line); break;
            case EngineEvent.Exited x: await TeardownAsync(new ConnectionFailure.Engine(x.Tail)); break;
        }
    }

    public void NetworkChanged(NetworkSnapshot? snapshot)
    {
        if (!Running || core is null) return;
        if (snapshot is null)
        {
            // Сети нет: привязывать сокеты не к чему. Запоминаем обрыв и ждём.
            networkLost = true;
            return;
        }
        // После обрыва аллокации реле протухли - переподключаемся даже в ту же сеть.
        if (!networkLost && snapshot.SameAs(lastNetwork)) return;
        networkLost = false;
        lastNetwork = snapshot;
        Log(LogLevel.Info, LogSource.App, $"Смена сети: {snapshot.Name} - переподключение");
        core.Send(new CoreCommand.Reconnect(snapshot.IfIndex, snapshot.Dns));
    }

    /// <summary>Короткий сон аллокации реле переживают; после долгого ядро надо разбудить сразу.</summary>
    public void SystemDidWake(TimeSpan slept)
    {
        if (!Running || slept.TotalSeconds < 60) return;
        Log(LogLevel.Info, LogSource.App, $"Пробуждение после сна {(int)slept.TotalSeconds} с - будим ядро");
        core?.Send(new CoreCommand.Wake());
    }

    void Log(LogLevel level, LogSource source, string text) => LogReceived?.Invoke(new LogLine(DateTime.Now, level, source, text));

    void SetCaptcha(CaptchaRequest? request)
    {
        if (request == Captcha) return;
        Captcha = request;
        CaptchaChanged?.Invoke(request);
    }

    void Fail(ConnectionFailure failure) => State = new ConnectionState.Failed(failure);

    /// <summary>Сворачивает всё. Сначала движок - чтобы система не осталась с туннелем в мёртвый порт.</summary>
    async Task TeardownAsync(ConnectionFailure? failure)
    {
        State = new ConnectionState.Stopping();
        session++;
        network.Stop();
        network.Changed -= OnNetworkChanged;
        var (oldEngine, oldCore) = (engine, core);
        engine = null;
        core = null;
        if (oldEngine is not null)
        {
            await oldEngine.StopAsync();
            oldEngine.Close();
        }
        oldCore?.Send(new CoreCommand.Stop());
        oldCore?.Terminate();
        SetCaptcha(null);
        ConnectedSince = null;
        Stats = new();
        StatsChanged?.Invoke();
        engineUp = false;
        ActiveServerId = null;
        if (failure is ConnectionFailure.Core c) Log(LogLevel.Error, LogSource.App, "Подключение остановлено: " + c.Message);
        State = failure is null ? new ConnectionState.Off() : new ConnectionState.Failed(failure);
    }
}
