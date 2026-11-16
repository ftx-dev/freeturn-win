using System.Text;
using System.Text.Json.Nodes;

namespace FreeTurn.Core;

public enum LinkError { InvalidScheme, EmptyPayload, BadEncoding, UnsupportedVersion, MissingProvider, MissingPeer }

public sealed class LinkException(LinkError error) : Exception(error.ToString())
{
    public LinkError Error { get; } = error;
}

/// <summary>Share-ссылка <c>freeturn://base64url(JSON)</c> - формат ядра (docs/uri.md) и Android-клиента.</summary>
public sealed record FreeturnLink
{
    public string Provider { get; init; } = "";
    public string Peer { get; init; } = "";
    public string Transport { get; init; } = "";
    public string Mode { get; init; } = "";
    public bool Bond { get; init; }
    public string ObfProfile { get; init; } = "";
    public string ObfKey { get; init; } = "";
    public int ObfTimingMs { get; init; }
    public int N { get; init; }
    public int StreamsPerCred { get; init; }
    public string ClientId { get; init; } = "";
    public string DnsMode { get; init; } = "";
    public string DnsServers { get; init; } = "";
    public bool ManualCaptcha { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Ссылка на звонок владельца - опционально.</summary>
    public string CallLink { get; init; } = "";
    public string WgConf { get; init; } = "";

    public const string Scheme = "freeturn://";
    public const int Version = 1;

    public static bool LooksLikeLink(string raw) => raw.Trim().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static FreeturnLink Parse(string raw)
    {
        var trimmed = raw.Trim();
        if (!LooksLikeLink(trimmed)) throw new LinkException(LinkError.InvalidScheme);
        var payload = trimmed[Scheme.Length..].TrimEnd('=');
        if (payload.Length == 0) throw new LinkException(LinkError.EmptyPayload);

        JsonObject o;
        try
        {
            var b64 = payload.Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            o = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64))) as JsonObject
                ?? throw new LinkException(LinkError.BadEncoding);
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
        {
            throw new LinkException(LinkError.BadEncoding);
        }

        if (Int(o, "v") != Version || o["v"] is null) throw new LinkException(LinkError.UnsupportedVersion);
        var provider = Str(o, "provider");
        if (provider.Length == 0) throw new LinkException(LinkError.MissingProvider);
        var peer = Str(o, "peer");
        if (peer.Length == 0) throw new LinkException(LinkError.MissingPeer);
        return new FreeturnLink
        {
            Provider = provider, Peer = peer, Transport = Str(o, "transport"), Mode = Str(o, "mode"), Bond = Bool(o, "bond"),
            ObfProfile = Str(o, "obf"), ObfKey = Str(o, "key"), ObfTimingMs = Int(o, "timing"), N = Int(o, "n"),
            StreamsPerCred = Int(o, "spc"), ClientId = Str(o, "cid"), DnsMode = Str(o, "dns"), DnsServers = Str(o, "dnss"),
            ManualCaptcha = Bool(o, "mcap"), Name = Str(o, "name"), CallLink = Str(o, "vk"), WgConf = Str(o, "wg"),
        };
    }

    static string Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static int Int(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
    static bool Bool(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

/// <summary>Разбор ручного DNS-списка - те же правила, что у Android DnsList.</summary>
public static class DnsList
{
    static IEnumerable<string> Tokens(string raw) =>
        raw.Split([',', ';', ' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);

    static bool IsValid(string token) => token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or ':' or '_' or '-');

    /// <summary>Строка валидных токенов через запятую; пусто - список не задан.</summary>
    public static string Normalize(string raw) => string.Join(",", Tokens(raw).Where(IsValid));

    public static bool HasInvalidTokens(string raw) => Tokens(raw).Any(t => !IsValid(t));
}

public enum ImportError { UnknownProvider, TcpModeUnsupported, BadObf }

public sealed class ImportException(ImportError error) : Exception(error.ToString())
{
    public ImportError Error { get; } = error;
}

/// <summary>Ссылка, проверенная и сверенная с уже сохранёнными серверами - то, что видит окно импорта.</summary>
public sealed record ImportDraft(FreeturnLink Link, string SuggestedName, string CallLink, bool DuplicateAddress,
    bool DuplicateConf, bool NeedsCallLink, bool NeedsWireGuard);

public static class ImportPlanner
{
    public static ImportDraft Draft(FreeturnLink link, IReadOnlyList<Server> existing)
    {
        if (!Provider.Values.Contains(link.Provider)) throw new ImportException(ImportError.UnknownProvider);
        // TCP-режим для Xray работает только через локальный порт, которого в клиенте нет.
        if (link.Mode == "tcp" || link.Bond) throw new ImportException(ImportError.TcpModeUnsupported);
        if (link.ObfProfile.Length > 0 && link.ObfProfile != ObfProfile.None &&
            (!ObfProfile.Values.Contains(link.ObfProfile) || !ObfProfile.IsValidKey(link.ObfKey)))
            throw new ImportException(ImportError.BadObf);

        var conf = link.WgConf.Trim();
        var host = link.Peer.Split(':')[0];
        return new ImportDraft(
            link,
            link.Name.Length == 0 ? host : link.Name,
            link.CallLink.Trim(),
            existing.Any(s => string.Equals(s.Client.ServerAddress, link.Peer, StringComparison.OrdinalIgnoreCase)),
            conf.Length > 0 && existing.Any(s => s.Client.WireGuardConfig.Trim() == conf),
            link.Provider == Provider.Relay,
            conf.Length == 0);
    }

    public static Server MakeServer(ImportDraft draft, string name, string callLink)
    {
        var link = draft.Link;
        var conf = link.WgConf.Trim();
        var client = new ClientConfig
        {
            ServerAddress = link.Peer,
            CallLink = callLink.Trim(),
            Provider = link.Provider,
            UseUdp = link.Transport == "udp",
            TunnelTransport = conf.Length == 0 ? TunnelTransport.None : TunnelTransport.WireGuard,
            WireGuardConfig = conf,
            ClientId = link.ClientId.Trim(),
            CustomDns = DnsList.Normalize(link.DnsServers),
            ManualCaptcha = link.ManualCaptcha,
        };
        if (link.N > 0) client.Threads = link.N;
        if (link.StreamsPerCred > 0) client.StreamsPerCred = link.StreamsPerCred;
        if (link.DnsMode is "auto" or "plain" or "doh") client.DnsMode = link.DnsMode;

        var opts = new ServerOpts
        {
            ObfProfile = link.ObfProfile.Length == 0 ? ObfProfile.None : link.ObfProfile,
            ObfKey = link.ObfKey,
            ObfTimingMs = Math.Clamp(link.ObfTimingMs, 0, ObfProfile.TimingMax),
        };
        var trimmed = name.Trim();
        return new Server { Name = trimmed.Length == 0 ? draft.SuggestedName : trimmed, Client = client, Opts = opts };
    }
}
