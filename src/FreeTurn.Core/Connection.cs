using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeTurn.Core;

public sealed record TrafficStats(long Tx = 0, long Rx = 0, long TxRate = 0, long RxRate = 0);

public enum CorePhase { Idle, Connecting, Connected, Captcha, Error }

/// <summary>События процесса freeturn-core (строки JSON в его stdout).</summary>
public abstract record CoreEvent
{
    public sealed record Hello(string Version) : CoreEvent;
    public sealed record State(CorePhase Phase, int Streams, int Total, string Err) : CoreEvent;
    public sealed record Log(LogLevel Level, string Msg, DateTime Date) : CoreEvent;
    /// <summary>Пустой Url - капчу убрать.</summary>
    public sealed record Captcha(string Url, bool Manual) : CoreEvent;
    public sealed record Stats(TrafficStats Traffic) : CoreEvent;
    public sealed record Args(string Text) : CoreEvent;
    public sealed record Error(string Cmd, string Err) : CoreEvent;
    /// <summary>Процесс ядра завершился.</summary>
    public sealed record Exited : CoreEvent;

    public static CoreEvent? Decode(string line)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(line) as JsonObject; }
        catch (JsonException) { return null; }
        if (o is null) return null;
        string Str(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        long Num(string k) => o[k] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
        bool Bool(string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        switch (Str("ev"))
        {
            case "hello": return new Hello(Str("version"));
            case "state":
                CorePhase? phase = Str("state") switch
                {
                    "idle" => CorePhase.Idle, "connecting" => CorePhase.Connecting, "connected" => CorePhase.Connected,
                    "captcha" => CorePhase.Captcha, "error" => CorePhase.Error, _ => null,
                };
                return phase is null ? null : new State(phase.Value, (int)Num("streams"), (int)Num("total"), Str("err"));
            case "log":
                var level = Str("level") switch { "debug" => LogLevel.Debug, "warn" => LogLevel.Warn, "error" => LogLevel.Error, _ => LogLevel.Info };
                return new Log(level, Str("msg"), DateTimeOffset.FromUnixTimeMilliseconds(Num("ts")).LocalDateTime);
            case "captcha": return new Captcha(Str("url"), Bool("manual"));
            case "stats": return new Stats(new TrafficStats(Num("tx"), Num("rx"), Num("txRate"), Num("rxRate")));
            case "args": return new Args(Str("args"));
            case "error": return new Error(Str("cmd"), Str("err"));
            default: return null;
        }
    }
}

/// <summary>Команды процессу freeturn-core (строки JSON в его stdin).</summary>
public abstract record CoreCommand
{
    public sealed record Start(string Config, int IfIndex) : CoreCommand;
    public sealed record Stop : CoreCommand;
    public sealed record Reconnect(int IfIndex, IReadOnlyList<string> Dns) : CoreCommand
    {
        public bool Equals(Reconnect? other) => other is not null && IfIndex == other.IfIndex && Dns.SequenceEqual(other.Dns);
        public override int GetHashCode() => IfIndex;
    }
    public sealed record Wake : CoreCommand;
    public sealed record Args(string Config) : CoreCommand;

    public string Encoded() => (this switch
    {
        Start s => new JsonObject { ["cmd"] = "start", ["config"] = s.Config, ["ifIndex"] = s.IfIndex },
        Stop => new JsonObject { ["cmd"] = "stop" },
        Reconnect r => new JsonObject { ["cmd"] = "reconnect", ["dns"] = string.Join(",", r.Dns), ["ifIndex"] = r.IfIndex },
        Wake => new JsonObject { ["cmd"] = "wake" },
        Args a => new JsonObject { ["cmd"] = "args", ["config"] = a.Config },
        _ => throw new InvalidOperationException(),
    }).ToJsonString();
}

public abstract record EngineEvent
{
    public sealed record Log(string Line) : EngineEvent;
    /// <summary>Движок умер сам: туннеля больше нет.</summary>
    public sealed record Exited(int Code, string Tail) : EngineEvent;
}

public sealed record DaemonStatus(bool Running, string Version);

public enum DaemonErrorKind { NotInstalled, Busy, VersionMismatch, Failed }

public sealed class DaemonException(DaemonErrorKind kind, string message = "") : Exception(message.Length == 0 ? kind.ToString() : message)
{
    public DaemonErrorKind Kind { get; } = kind;
}

public static class DaemonProtocol
{
    /// <summary>Должна совпадать с daemon.ProtocolVersion в helper/internal/daemon/server.go.</summary>
    public const string Version = "1";
    public const string PipeName = "freeturnd";
}

/// <summary>Процесс ядра. События приходят в потоке интерфейса.</summary>
public interface ICoreControl
{
    event Action<CoreEvent>? EventReceived;
    void Launch();
    void Send(CoreCommand command);
    void Terminate();
}

/// <summary>Служба туннеля. События приходят в потоке интерфейса.</summary>
public interface IEngineControl
{
    event Action<EngineEvent>? EventReceived;
    Task<DaemonStatus> StatusAsync();
    Task StartAsync(string config);
    Task StopAsync();
    void Close();
}

public interface INetworkWatch
{
    /// <summary>null - сети нет.</summary>
    event Action<NetworkSnapshot?>? Changed;
    NetworkSnapshot? Current();
    void Start();
    void Stop();
}
