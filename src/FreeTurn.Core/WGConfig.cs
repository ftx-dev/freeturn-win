namespace FreeTurn.Core;

public enum WGConfigError { MissingInterface, MissingPrivateKey, MissingAddress, MissingPeer, MissingPublicKey }

public sealed class WGConfigException(WGConfigError error, int peer = -1) : Exception(error.ToString())
{
    public WGConfigError Error { get; } = error;
    public int Peer { get; } = peer;
}

/// <summary>Конфиг wg-quick. MTU и Endpoint отбрасываются: MTU - константа туннеля, а Endpoint
/// заменяется локальным портом ядра.</summary>
public sealed class WGConfig
{
    public sealed class PeerConfig
    {
        public string PublicKey { get; set; } = "";
        public string? PresharedKey { get; set; }
        public List<string> AllowedIPs { get; } = [];
        public int? PersistentKeepalive { get; set; }
    }

    public string PrivateKey { get; private set; } = "";
    public List<string> Addresses { get; } = [];
    public List<string> Dns { get; } = [];
    /// <summary>Параметры AmneziaWG из [Interface], ключи в нижнем регистре.</summary>
    public Dictionary<string, string> Amnezia { get; } = [];
    public List<PeerConfig> Peers { get; } = [];

    public bool IsAmnezia => Amnezia.Count > 0;

    /// <summary>Зеркало amnezia-ключей парсера ядра (internal/tunnel/wgconf).</summary>
    internal static readonly HashSet<string> AmneziaKeys =
    [
        "jc", "jmin", "jmax", "s1", "s2", "s3", "s4", "h1", "h2", "h3", "h4",
        "i1", "i2", "i3", "i4", "i5", "headerprotectionkey", "contentpaddingaddition",
        "rekeyaftertime", "rekeytimeout", "rejectaftertime", "keepalivetimeout",
        "maxhandshakeattempts", "randomtrailers", "disablecookies",
    ];

    public static WGConfig Parse(string text)
    {
        var config = new WGConfig();
        string section = "";
        var sawInterface = false;
        foreach (var rawLine in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Как в ядре: комментарий начинается с первого # или ; в строке.
            var cut = rawLine.IndexOfAny(['#', ';']);
            var line = (cut >= 0 ? rawLine[..cut] : rawLine).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('['))
            {
                section = line.ToLowerInvariant();
                if (section == "[interface]") sawInterface = true;
                if (section == "[peer]") config.Peers.Add(new PeerConfig());
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            if (section == "[interface]")
            {
                switch (key)
                {
                    case "privatekey": config.PrivateKey = value; break;
                    case "address": config.Addresses.AddRange(List(value)); break;
                    case "dns": config.Dns.AddRange(List(value)); break;
                    default: if (AmneziaKeys.Contains(key)) config.Amnezia[key] = value; break;
                }
            }
            else if (section == "[peer]")
            {
                var peer = config.Peers[^1];
                switch (key)
                {
                    case "publickey": peer.PublicKey = value; break;
                    case "presharedkey": peer.PresharedKey = value; break;
                    case "allowedips": peer.AllowedIPs.AddRange(List(value)); break;
                    case "persistentkeepalive": peer.PersistentKeepalive = int.TryParse(value, out var k) ? k : null; break;
                }
            }
        }
        if (!sawInterface) throw new WGConfigException(WGConfigError.MissingInterface);
        if (config.PrivateKey.Length == 0) throw new WGConfigException(WGConfigError.MissingPrivateKey);
        if (config.Addresses.Count == 0) throw new WGConfigException(WGConfigError.MissingAddress);
        if (config.Peers.Count == 0) throw new WGConfigException(WGConfigError.MissingPeer);
        var bad = config.Peers.FindIndex(p => p.PublicKey.Length == 0);
        if (bad >= 0) throw new WGConfigException(WGConfigError.MissingPublicKey, bad);
        return config;
    }

    public static bool TryParse(string text, out WGConfig? config)
    {
        try { config = Parse(text); return true; }
        catch (WGConfigException) { config = null; return false; }
    }

    static IEnumerable<string> List(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
