namespace FreeTurn.Core.Tests;

sealed class FakeCore(List<string> log) : ICoreControl
{
    public event Action<CoreEvent>? EventReceived;
    public List<CoreCommand> Commands { get; } = [];
    public bool Launched, Terminated;
    public void Launch() { Launched = true; log.Add("core.launch"); }
    public void Send(CoreCommand c) { Commands.Add(c); log.Add(c is CoreCommand.Stop ? "core.stop" : "core.send"); }
    public void Terminate() { Terminated = true; log.Add("core.terminate"); }
    public void Raise(CoreEvent e) => EventReceived?.Invoke(e);
}

sealed class FakeEngine(List<string> log) : IEngineControl
{
    public event Action<EngineEvent>? EventReceived;
    public Func<DaemonStatus> Status = () => new(false, DaemonProtocol.Version);
    public Exception? StartError;
    public List<string> Started { get; } = [];
    public bool Stopped, Closed;
    public Task<DaemonStatus> StatusAsync() => Task.FromResult(Status());
    public Task StartAsync(string config) { log.Add("engine.start"); if (StartError is not null) throw StartError; Started.Add(config); return Task.CompletedTask; }
    public Task StopAsync() { Stopped = true; log.Add("engine.stop"); return Task.CompletedTask; }
    public void Close() { Closed = true; log.Add("engine.close"); }
}

sealed class FakeNetwork : INetworkWatch
{
    public event Action<NetworkSnapshot?>? Changed;
    public NetworkSnapshot? Snapshot = new("Wi-Fi", 11, ["192.168.1.1"]);
    public bool Started;
    public NetworkSnapshot? Current() => Snapshot;
    public void Start() => Started = true;
    public void Stop() => Started = false;
}

public class ControllerTests
{
    readonly List<string> log = [];
    readonly FakeCore core;
    readonly FakeEngine engine;
    readonly FakeNetwork network = new();
    readonly ConnectionController c;
    readonly Server server = new() { Name = "S" };

    public ControllerTests()
    {
        core = new FakeCore(log);
        engine = new FakeEngine(log);
        c = new ConnectionController(() => core, () => engine, network, () => 41000);
        server.Client.ServerAddress = "1.2.3.4:56000";
        server.Client.CallLink = "https://vk.ru/call/join/x";
        server.Client.TunnelTransport = "wireguard";
        server.Client.WireGuardConfig = T.PlainWG;
    }

    Task Connect(RoutingProfile? p = null) => c.ConnectAsync(server, p ?? Presets.AllViaVpn, new AppSettings(), T.Hex32);
    Task Connected(int streams = 3) => c.HandleCoreEventAsync(new CoreEvent.State(CorePhase.Connected, streams, 12, ""));

    [Fact]
    public async Task StartingEventAlreadyIdentifiesTheLockedServer()
    {
        string? activeWhenStarting = null;
        c.StateChanged += state =>
        {
            if (state is ConnectionState.Starting) activeWhenStarting = c.ActiveServerId;
        };
        await Connect();
        Assert.Equal(server.Id, activeWhenStarting);
    }

    [Fact]
    public async Task EngineStartsOnlyAfterFirstConnected()
    {
        await Connect();
        Assert.True(core.Launched);
        Assert.Empty(engine.Started);
        Assert.IsType<ConnectionState.Starting>(c.State);
        await c.HandleCoreEventAsync(new CoreEvent.State(CorePhase.Connecting, 0, 12, ""));
        Assert.Equal(new ConnectionState.Connecting(0, 12), c.State);
        await Connected();
        await Connected(5);
        Assert.Single(engine.Started);
        Assert.Equal(new ConnectionState.Connected(5, 12), c.State);
        var peer = T.Obj(engine.Started[0])["endpoints"]![0]!["peers"]![0]!;
        Assert.Equal(41000, (int)peer["port"]!);
        var start = Assert.Single(core.Commands.OfType<CoreCommand.Start>());
        Assert.Equal(11, start.IfIndex);
        Assert.Equal("127.0.0.1:41000", (string?)T.Obj(start.Config)["proxy"]!["listen"]);
    }

    [Fact]
    public async Task DisconnectStopsEngineThenCore()
    {
        await Connect();
        await Connected();
        log.Clear();
        await c.DisconnectAsync();
        Assert.Equal(["engine.stop", "engine.close", "core.stop", "core.terminate"], log);
        Assert.IsType<ConnectionState.Off>(c.State);
        Assert.False(network.Started);
        Assert.Null(c.ConnectedSince);
    }

    [Fact]
    public async Task FailuresTearDown()
    {
        await Connect();
        await Connected();
        await c.HandleCoreEventAsync(new CoreEvent.State(CorePhase.Error, 0, 12, "boom"));
        Assert.True(engine.Stopped);
        Assert.True(core.Terminated);
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.Core("boom")), c.State);
    }

    [Fact]
    public async Task EngineExitAndStartFailure()
    {
        await Connect();
        await Connected();
        await c.HandleEngineEventAsync(new EngineEvent.Exited(1, "tun died"));
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.Engine("tun died")), c.State);

        engine.StartError = new DaemonException(DaemonErrorKind.Failed, "FATAL");
        await Connect();
        await Connected();
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.Engine("FATAL")), c.State);
    }

    [Fact]
    public async Task DaemonProblemsFailBeforeCoreLaunch()
    {
        engine.Status = () => throw new DaemonException(DaemonErrorKind.NotInstalled);
        await Connect();
        Assert.False(core.Launched);
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.DaemonNotInstalled()), c.State);
        engine.Status = () => new(false, "0");
        await Connect();
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.DaemonVersionMismatch()), c.State);
    }

    [Fact]
    public async Task PreconditionFailures()
    {
        server.Client.CallLink = " ";
        await Connect();
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.NoCallLink()), c.State);
        server.Client.CallLink = "L";
        server.Client.WireGuardConfig = "[Interface]\nAddress=10.0.0.2\n";
        await Connect();
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.BadWireGuard()), c.State);
        server.Client.WireGuardConfig = T.PlainWG;
        await Connect(new RoutingProfile { Rules = [new() { Kind = RuleKind.Cidr, Value = "nope" }] });
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.InvalidRule()), c.State);
        network.Snapshot = null;
        await Connect();
        Assert.Equal(new ConnectionState.Failed(new ConnectionFailure.NoNetwork()), c.State);
        Assert.False(core.Launched);
    }

    [Fact]
    public async Task CaptchaSurfacesAndClears()
    {
        var seen = new List<CaptchaRequest?>();
        c.CaptchaChanged += seen.Add;
        await Connect();
        await c.HandleCoreEventAsync(new CoreEvent.Captcha("http://localhost:8765/x", true));
        Assert.Equal(new CaptchaRequest("http://localhost:8765/x", true), c.Captcha);
        await c.DisconnectAsync();
        Assert.Equal([new CaptchaRequest("http://localhost:8765/x", true), null], seen);
    }

    // Сети нет - ничего не шлём; вернулась (даже та же) - переподключаемся: аллокации протухли.
    [Fact]
    public async Task NetworkChanges()
    {
        await Connect();
        await Connected();
        core.Commands.Clear();
        c.NetworkChanged(new NetworkSnapshot("Wi-Fi", 11, ["192.168.1.1"]));
        Assert.Empty(core.Commands);
        c.NetworkChanged(null);
        Assert.Empty(core.Commands);
        c.NetworkChanged(new NetworkSnapshot("Wi-Fi", 11, ["192.168.1.1"]));
        c.NetworkChanged(new NetworkSnapshot("Ethernet", 12, ["10.0.0.1"]));
        Assert.Equal([new CoreCommand.Reconnect(11, ["192.168.1.1"]), new CoreCommand.Reconnect(12, ["10.0.0.1"])], core.Commands);
        core.Commands.Clear();
        c.SystemDidWake(TimeSpan.FromSeconds(30));
        c.SystemDidWake(TimeSpan.FromSeconds(90));
        Assert.Equal([new CoreCommand.Wake()], core.Commands);
    }

    [Fact]
    public async Task LogsAndStats()
    {
        var lines = new List<LogLine>();
        c.LogReceived += lines.Add;
        await Connect();
        await c.HandleCoreEventAsync(new CoreEvent.Args("-peer x"));
        await c.HandleCoreEventAsync(new CoreEvent.Stats(new TrafficStats(10, 20, 1, 2)));
        Assert.Contains(lines, l => l.Source == LogSource.App && l.Text == "Команда: -peer x");
        Assert.Equal(new TrafficStats(10, 20, 1, 2), c.Stats);
        await Connect();
        Assert.Single(log, "core.launch");
    }

    [Fact]
    public void EventsDecodeAndCommandsEncode()
    {
        Assert.Equal(new CoreEvent.State(CorePhase.Connected, 3, 12, ""), CoreEvent.Decode("""{"ev":"state","state":"connected","streams":3,"total":12,"err":""}"""));
        // Go пишет & в JSON как \u0026: адрес капчи обязан прийти раскодированным.
        Assert.Equal(new CoreEvent.Captcha("http://localhost:8765/c?a=1&b=2", true), CoreEvent.Decode("""{"ev":"captcha","url":"http://localhost:8765/c?a=1\u0026b=2","manual":true}"""));
        Assert.Null(CoreEvent.Decode("panic: x"));
        Assert.Null(CoreEvent.Decode("""{"ev":"state","state":"weird"}"""));
        Assert.Equal("""{"cmd":"start","config":"{\u0022a\u0022:1}","ifIndex":14}""", new CoreCommand.Start("""{"a":1}""", 14).Encoded());
        Assert.Equal("""{"cmd":"reconnect","dns":"1.1.1.1,8.8.8.8","ifIndex":7}""", new CoreCommand.Reconnect(7, ["1.1.1.1", "8.8.8.8"]).Encoded());
    }
}
