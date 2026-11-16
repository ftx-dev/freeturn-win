using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FreeTurn.Core;

public enum RouteAction { Vpn, Direct, Block }

public enum RuleKind { App, Domain, DomainSuffix, DomainKeyword, Cidr }

public sealed record RoutingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    public RuleKind Kind { get; set; }
    /// <summary>app - путь к .exe или к папке с программой; cidr - адрес или подсеть.</summary>
    public string Value { get; set; } = "";
    public RouteAction Action { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed record RoutingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    public string Name { get; set; } = "";
    /// <summary>Куда идёт всё, что не попало под правила: Vpn или Direct.</summary>
    public RouteAction DefaultAction { get; set; } = RouteAction.Vpn;
    public List<RoutingRule> Rules { get; set; } = [];
    public bool BuiltIn { get; set; }

    /// <summary>Копия для правки: новые идентификаторы, не встроенная.</summary>
    public RoutingProfile EditableCopy(string name) => new()
    {
        Name = name, DefaultAction = DefaultAction,
        Rules = [.. Rules.Select(r => new RoutingRule { Kind = r.Kind, Value = r.Value, Action = r.Action, Enabled = r.Enabled })],
    };

    public RoutingProfile Copy() => this with { Rules = [.. Rules.Select(r => r with { })] };
}

public sealed class RoutingFile
{
    public List<RoutingProfile> Profiles { get; set; } = [];
    /// <summary>Профиль для серверов без собственного.</summary>
    public string SharedProfileId { get; set; } = Presets.RussiaDirect.Id;

    public static RoutingFile Initial() => new() { Profiles = [Presets.AllViaVpn, Presets.RussiaDirect], SharedProfileId = Presets.RussiaDirect.Id };

    /// <summary>Файл с диска: встроенные профили берутся из текущей версии приложения, свои сохраняются.</summary>
    public RoutingFile Normalized()
    {
        var own = (Profiles ?? []).Where(p => p is not null && !p.BuiltIn && p.Id != Presets.AllViaVpn.Id && p.Id != Presets.RussiaDirect.Id);
        List<RoutingProfile> all = [Presets.AllViaVpn, Presets.RussiaDirect, .. own];
        return new RoutingFile { Profiles = all, SharedProfileId = all.Any(p => p.Id == SharedProfileId) ? SharedProfileId : Presets.RussiaDirect.Id };
    }

    public RoutingProfile ProfileFor(Server server) =>
        Profiles.FirstOrDefault(p => p.Id == server.Client.RoutingProfileId)
        ?? Profiles.FirstOrDefault(p => p.Id == SharedProfileId)
        ?? Presets.RussiaDirect;

    public void Add(RoutingProfile profile) => Profiles.Add(profile);

    public void Remove(string id)
    {
        Profiles.RemoveAll(p => p.Id == id && !p.BuiltIn);
        if (!Profiles.Any(p => p.Id == SharedProfileId)) SharedProfileId = Presets.RussiaDirect.Id;
    }

    public RoutingProfile? Clone(string id)
    {
        var source = Profiles.FirstOrDefault(p => p.Id == id);
        if (source is null) return null;
        var copy = source.EditableCopy(source.Name + " (копия)");
        Profiles.Add(copy);
        return copy;
    }

    public void Update(RoutingProfile profile)
    {
        var i = Profiles.FindIndex(p => p.Id == profile.Id);
        if (i >= 0 && !Profiles[i].BuiltIn) Profiles[i] = profile;
    }
}

public static class Presets
{
    // Раньше профилей: статические поля инициализируются по порядку.
    internal static readonly string[] RussianSuffixes =
    [
        "ru", "su", "xn--p1ai",
        "vk.com", "vk.me", "vk-cdn.net", "userapi.com", "vkuser.net", "mycdn.me", "okcdn.ru", "ok.ru",
        "yandex.ru", "yandex.net", "yandex.com", "yastatic.net", "ya.ru", "kinopoisk.ru",
        "mail.ru", "imgsmail.ru", "rutube.ru", "dzen.ru",
        "gosuslugi.ru", "mos.ru", "nalog.ru", "rzd.ru",
        "sberbank.ru", "sber.ru", "tbank.ru", "tinkoff.ru", "alfabank.ru", "vtb.ru", "nspk.ru",
        "ozon.ru", "ozone.ru", "wildberries.ru", "wb.ru", "wbbasket.ru", "avito.ru", "avito.st",
        "megamarket.ru", "samokat.ru", "2gis.ru", "2gis.com", "hh.ru", "cian.ru",
        "mts.ru", "megafon.ru", "beeline.ru", "tele2.ru", "t2.ru",
    ];

    public static readonly RoutingProfile AllViaVpn = new() { Id = "builtin-all", Name = "Всё через VPN", DefaultAction = RouteAction.Vpn, BuiltIn = true };

    /// <summary>Российские сервисы часто недоступны с зарубежного IP. "xn--p1ai" - это .рф: в трафике домен в punycode.</summary>
    public static readonly RoutingProfile RussiaDirect = new()
    {
        Id = "builtin-ru", Name = "РФ сервисы напрямую", DefaultAction = RouteAction.Vpn, BuiltIn = true,
        Rules = [.. RussianSuffixes.Select(s => new RoutingRule { Id = "builtin-ru-" + s, Kind = RuleKind.DomainSuffix, Value = s, Action = RouteAction.Direct })],
    };
}

public sealed class ProfileTransferException(string message) : Exception(message);

/// <summary>Обмен профилями маршрутов через файл; формат общий с клиентом для macOS
/// (у правил приложений пути, конечно, свои у каждой ОС).</summary>
public static class ProfileTransfer
{
    const string Format = "freeturn-routing";
    const int FormatVersion = 1;
    public const string WrongFormat = "wrongFormat";
    public const string UnsupportedVersion = "unsupportedVersion";

    sealed record Envelope(string Format, int Version, RoutingProfile Profile);

    public static byte[] Export(RoutingProfile profile) =>
        JsonSerializer.SerializeToUtf8Bytes(new Envelope(Format, FormatVersion, profile), Store.Json);

    public static RoutingProfile Import(byte[] data)
    {
        JsonObject? header;
        try { header = JsonNode.Parse(data) as JsonObject; }
        catch (JsonException) { throw new ProfileTransferException(WrongFormat); }
        // Версию смотрим до разбора профиля: у новой версии он может не разобраться вовсе.
        if (header?["format"]?.GetValueKind() != JsonValueKind.String || (string?)header["format"] != Format ||
            header["version"]?.GetValueKind() != JsonValueKind.Number)
            throw new ProfileTransferException(WrongFormat);
        if ((int)header["version"]! > FormatVersion) throw new ProfileTransferException(UnsupportedVersion);
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(data, Store.Json); }
        catch (JsonException) { throw new ProfileTransferException(WrongFormat); }
        if (envelope?.Profile is null) throw new ProfileTransferException(WrongFormat);
        // Чужой файл не должен подменить встроенный профиль или совпасть по id с имеющимся.
        return envelope.Profile.EditableCopy(envelope.Profile.Name ?? "");
    }
}

public sealed record EngineSettings(string Stack = "mixed", string? VpnDns = null, bool Debug = false);

public sealed class InvalidRuleException(string id) : Exception($"invalid rule {id}")
{
    public string RuleId { get; } = id;
}

/// <summary>Конфиг движка (Amnezia Box, формат sing-box 1.12): tun (wintun) + WG/AWG в локальный порт ядра + правила.</summary>
public static class EngineConfig
{
    public const string InterfaceName = "FreeTurn";

    static readonly HashSet<string> AwgInts = ["jc", "jmin", "jmax", "s1", "s2", "s3", "s4"];
    static readonly Dictionary<string, string> AwgBools = new() { ["randomtrailers"] = "random_trailers", ["disablecookies"] = "disable_cookies" };
    static readonly Dictionary<string, string> AwgRenamed = new()
    {
        ["headerprotectionkey"] = "header_protection_key", ["contentpaddingaddition"] = "content_padding_addition",
        ["rekeyaftertime"] = "rekey_after_time", ["rekeytimeout"] = "rekey_timeout", ["rejectaftertime"] = "reject_after_time",
        ["keepalivetimeout"] = "keepalive_timeout", ["maxhandshakeattempts"] = "max_handshake_attempts",
    };

    public static string Build(WGConfig wg, int corePort, RoutingProfile profile, EngineSettings settings)
    {
        var hasV6 = wg.Addresses.Any(a => a.Contains(':'));
        var matchers = profile.Rules.Where(r => r.Enabled).Select(r => (rule: r, match: Matcher(r))).ToList();
        var viaVpn = profile.DefaultAction != RouteAction.Direct;

        var tun = new JsonObject
        {
            ["address"] = CoreConfig.Array(hasV6 ? ["172.19.0.1/30", "fdfe:dcba:9876::1/126"] : ["172.19.0.1/30"]),
            ["auto_route"] = true,
            ["interface_name"] = InterfaceName,
            ["mtu"] = CoreConfig.TunnelMtu,
            ["stack"] = settings.Stack,
            ["tag"] = "tun-in",
            ["type"] = "tun",
        };
        // AllowedIPs без 0.0.0.0/0 - в туннель попадают только эти сети.
        var allowed = wg.Peers.SelectMany(p => p.AllowedIPs).Distinct().Where(a => hasV6 || !a.Contains(':')).ToList();
        if (allowed.Count > 0 && !allowed.Contains("0.0.0.0/0")) tun["route_address"] = CoreConfig.Array(allowed);

        var dnsRules = new JsonArray();
        foreach (var (rule, match) in matchers)
        {
            // Домены и приложения, идущие напрямую, резолвятся местным DNS: иначе сервис отдаст зарубежный узел.
            if (rule.Kind == RuleKind.Cidr || rule.Action == RouteAction.Block) continue;
            var r = (JsonObject)match.DeepClone();
            r["server"] = rule.Action == RouteAction.Vpn ? "vpn-dns" : "local-dns";
            dnsRules.Add(r);
        }
        var dns = new JsonObject
        {
            ["final"] = viaVpn ? "vpn-dns" : "local-dns",
            ["rules"] = dnsRules,
            ["servers"] = new JsonArray(
                new JsonObject { ["detour"] = "vpn", ["server"] = settings.VpnDns ?? wg.Dns.FirstOrDefault() ?? "1.1.1.1", ["tag"] = "vpn-dns", ["type"] = "udp" },
                new JsonObject { ["tag"] = "local-dns", ["type"] = "local" }),
        };
        // Без IPv6 в туннеле AAAA-ответы уводили бы трафик мимо него.
        if (!hasV6) dns["strategy"] = "ipv4_only";

        var rules = new JsonArray(
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["action"] = "hijack-dns", ["protocol"] = "dns" },
            new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" });
        foreach (var (rule, match) in matchers)
        {
            var r = (JsonObject)match.DeepClone();
            if (rule.Action == RouteAction.Block) r["action"] = "reject";
            else r["outbound"] = rule.Action == RouteAction.Vpn ? "vpn" : "direct";
            rules.Add(r);
        }

        var config = new JsonObject
        {
            ["dns"] = dns,
            ["endpoints"] = new JsonArray(Endpoint(wg, corePort, hasV6)),
            ["inbounds"] = new JsonArray(tun),
            ["log"] = new JsonObject { ["level"] = settings.Debug ? "debug" : "info" },
            ["outbounds"] = new JsonArray(new JsonObject { ["tag"] = "direct", ["type"] = "direct" }),
            ["route"] = new JsonObject
            {
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local-dns",
                ["final"] = viaVpn ? "vpn" : "direct",
                ["rules"] = rules,
            },
        };
        return config.ToJsonString();
    }

    static JsonObject Endpoint(WGConfig wg, int corePort, bool hasV6)
    {
        var awg = wg.IsAmnezia;
        var ep = new JsonObject
        {
            ["address"] = CoreConfig.Array(wg.Addresses.Select(a => a.Contains('/') ? a : a + (a.Contains(':') ? "/128" : "/32"))),
            ["mtu"] = CoreConfig.TunnelMtu,
            ["private_key"] = wg.PrivateKey,
            // The peer is the local bridge, never the physical network adapter.
            // Explicit loopback binding disables auto_detect_interface for this socket.
            ["inet4_bind_address"] = "127.0.0.1",
            ["tag"] = "vpn",
            ["type"] = awg ? "awg" : "wireguard",
        };
        foreach (var (key, value) in wg.Amnezia)
        {
            if (AwgInts.Contains(key)) ep[key] = int.TryParse(value, out var n) ? n : 0;
            else if (AwgBools.TryGetValue(key, out var name)) ep[name] = value.ToLowerInvariant() is "true" or "1" or "yes" or "on";
            else ep[AwgRenamed.GetValueOrDefault(key, key)] = value;
        }
        var peers = new JsonArray();
        foreach (var peer in wg.Peers)
        {
            // Endpoint из конфига не нужен: пакеты уходят в локальный порт ядра.
            var p = new JsonObject
            {
                ["address"] = "127.0.0.1",
                ["allowed_ips"] = CoreConfig.Array(peer.AllowedIPs.Count == 0 ? (hasV6 ? ["0.0.0.0/0", "::/0"] : ["0.0.0.0/0"]) : peer.AllowedIPs),
                ["port"] = corePort,
                ["public_key"] = peer.PublicKey,
            };
            if (peer.PresharedKey is { } psk) p[awg ? "preshared_key" : "pre_shared_key"] = psk;
            if (peer.PersistentKeepalive is { } keepalive) p["persistent_keepalive_interval"] = keepalive;
            peers.Add(p);
        }
        ep["peers"] = peers;
        return ep;
    }

    /// <summary>Примет ли движок такое правило - для проверки при вводе.</summary>
    public static bool IsValid(RoutingRule rule)
    {
        try { Matcher(rule); return true; }
        catch (InvalidRuleException) { return false; }
    }

    static readonly Regex WindowsPath = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);

    /// <summary>Условие правила в терминах движка; бросает на значении, которое движок не примет.</summary>
    static JsonObject Matcher(RoutingRule rule)
    {
        var value = rule.Value.Trim();
        if (value.Length == 0) throw new InvalidRuleException(rule.Id);
        switch (rule.Kind)
        {
            case RuleKind.App:
                if (!WindowsPath.IsMatch(value)) throw new InvalidRuleException(rule.Id);
                if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return new JsonObject { ["process_path"] = CoreConfig.Array([value]) };
                // Папка: у программы бывает много процессов (помощники, обновлятели) - ловим всё внутри.
                return new JsonObject { ["process_path_regex"] = CoreConfig.Array(["(?i)^" + EscapeRegex(value.TrimEnd('\\')) + @"\\"]) };
            case RuleKind.Domain:
                return new JsonObject { ["domain"] = CoreConfig.Array([value.ToLowerInvariant()]) };
            case RuleKind.DomainSuffix:
                var suffix = value.ToLowerInvariant().TrimStart('.');
                if (suffix.Length == 0) throw new InvalidRuleException(rule.Id);
                return new JsonObject { ["domain_suffix"] = CoreConfig.Array([suffix]) };
            case RuleKind.DomainKeyword:
                return new JsonObject { ["domain_keyword"] = CoreConfig.Array([value.ToLowerInvariant()]) };
            default:
                if (!IsCidr(value)) throw new InvalidRuleException(rule.Id);
                return new JsonObject { ["ip_cidr"] = CoreConfig.Array([value]) };
        }
    }

    /// <summary>Экранирование для RE2 (Go): .NET Regex.Escape экранирует и пробел, а такую escape-последовательность RE2 отвергает.</summary>
    internal static string EscapeRegex(string s) =>
        string.Concat(s.Select(c => @"\.+*?()|[]{}^$".Contains(c) ? "\\" + c : c.ToString()));

    internal static bool IsCidr(string s)
    {
        var parts = s.Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip)) return false;
        // IPAddress.TryParse принимает и "10" как 0.0.0.10 - требуем полную запись.
        if (ip.AddressFamily == AddressFamily.InterNetwork && parts[0].Count(c => c == '.') != 3) return false;
        if (parts.Length == 1) return true;
        var bits = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return int.TryParse(parts[1], out var prefix) && prefix >= 0 && prefix <= bits && parts[1].All(char.IsAsciiDigit);
    }
}
