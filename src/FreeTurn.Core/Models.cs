using System.Text.Json.Serialization;

namespace FreeTurn.Core;

public static class Provider
{
    /// <summary>Через TURN-реле звонков; "vk" - wire-значение ядра.</summary>
    public const string Relay = "vk";
    public const string Direct = "direct";
    public static readonly string[] Values = [Relay, Direct];
}

public static class ObfProfile
{
    public const string None = "none";
    public static readonly string[] Values = ["none", "rtpopus", "rtpopus2", "rtpopus3"];
    public const int TimingMax = 60;

    public static bool IsValidKey(string key) => key.Length == 64 && key.All(Uri.IsHexDigit);

    public static string GenerateKey() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}

public static class TunnelTransport
{
    public const string None = "none";
    public const string WireGuard = "wireguard";
}

/// <summary>Клиентские параметры сервера. Имена ключей совпадают с Android ServerJson и macOS.</summary>
public sealed record ClientConfig
{
    public string ServerAddress { get; set; } = "";
    public string CallLink { get; set; } = "";
    public string Provider { get; set; } = Core.Provider.Relay;
    public int Threads { get; set; } = 12;
    public int StreamsPerCred { get; set; } = 12;
    public bool UseUdp { get; set; }
    public bool ManualCaptcha { get; set; }
    public bool DebugMode { get; set; }
    public bool UseCarrierDns { get; set; } = true;
    public string DnsMode { get; set; } = "auto";
    public string CustomDns { get; set; } = "";
    public bool MagicSwitch { get; set; }
    public string MagicTurn { get; set; } = "";
    public string TunnelTransport { get; set; } = Core.TunnelTransport.None;
    public string WireGuardConfig { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>Профиль маршрутов сервера; null - общий.</summary>
    public string? RoutingProfileId { get; set; }

    [JsonIgnore]
    public bool WireGuardActive => TunnelTransport == Core.TunnelTransport.WireGuard && !string.IsNullOrWhiteSpace(WireGuardConfig);

    /// <summary>Как optX у Android: непонятное значение - значение по умолчанию.</summary>
    internal void Normalize()
    {
        ServerAddress ??= ""; CallLink ??= ""; CustomDns ??= ""; MagicTurn ??= ""; WireGuardConfig ??= ""; ClientId ??= "";
        if (!Core.Provider.Values.Contains(Provider)) Provider = Core.Provider.Relay;
        if (DnsMode is not ("auto" or "plain" or "doh")) DnsMode = "auto";
        if (TunnelTransport != Core.TunnelTransport.WireGuard) TunnelTransport = Core.TunnelTransport.None;
    }
}

public sealed record ServerOpts
{
    public string ObfProfile { get; set; } = Core.ObfProfile.None;
    public string ObfKey { get; set; } = "";
    public int ObfTimingMs { get; set; }
    public string ProxyMode { get; set; } = "udp";

    [JsonIgnore]
    public bool ObfEnabled => ObfProfile != Core.ObfProfile.None;

    internal void Normalize()
    {
        ObfKey ??= "";
        if (!Core.ObfProfile.Values.Contains(ObfProfile)) ObfProfile = Core.ObfProfile.None;
        ObfTimingMs = Math.Clamp(ObfTimingMs, 0, Core.ObfProfile.TimingMax);
        ProxyMode ??= "udp";
    }
}

public sealed record Server
{
    public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    public string Name { get; set; } = "";
    public ClientConfig Client { get; set; } = new();
    public ServerOpts Opts { get; set; } = new();

    internal void Normalize()
    {
        if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString().ToUpperInvariant();
        Name ??= "";
        Client ??= new();
        Opts ??= new();
        Client.Normalize();
        Opts.Normalize();
    }

    /// <summary>Глубокая копия: записи изменяемые, а правка в форме не должна задевать сохранённый список.</summary>
    public Server Copy() => this with { Client = Client with { }, Opts = Opts with { } };
}

public sealed class ServersFile
{
    public List<Server> List { get; set; } = [];
    public string? ActiveId { get; set; }

    [JsonIgnore]
    public Server? Active => List.FirstOrDefault(s => s.Id == ActiveId);

    public void Add(Server server, bool activate)
    {
        List.Add(server);
        if (activate || Active is null) ActiveId = server.Id;
    }

    public void Remove(string id)
    {
        List.RemoveAll(s => s.Id == id);
        if (Active is null) ActiveId = List.FirstOrDefault()?.Id;
    }

    public Server? Clone(string id)
    {
        var source = List.FirstOrDefault(s => s.Id == id);
        if (source is null) return null;
        var copy = source.Copy() with { Id = Guid.NewGuid().ToString().ToUpperInvariant(), Name = source.Name + " (копия)" };
        List.Add(copy);
        return copy;
    }

    public void Rename(string id, string name)
    {
        var trimmed = name.Trim();
        var i = List.FindIndex(s => s.Id == id);
        if (trimmed.Length == 0 || i < 0) return;
        List[i].Name = trimmed;
    }

    public void Update(Server server)
    {
        var i = List.FindIndex(s => s.Id == server.Id);
        if (i >= 0) List[i] = server;
    }

    internal void Normalize()
    {
        List ??= [];
        List.RemoveAll(s => s is null);
        foreach (var s in List) s.Normalize();
    }
}

public sealed record AppSettings
{
    public string Theme { get; set; } = "system";
    public bool DisableAnimations { get; set; }
    public string Language { get; set; } = "system";
    public bool PrivacyMode { get; set; }
    public bool AdvancedCaptcha { get; set; }
    public bool AutoConnect { get; set; }
    public bool LaunchAtLogin { get; set; }
    public bool DebugScreen { get; set; }
    public string TunStack { get; set; } = "mixed";
    /// <summary>Где показывать ручную капчу: "window" - окно приложения, "browser" - браузер.</summary>
    public string CaptchaTarget { get; set; } = "window";
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Иконка приложения: standard - синяя, black - чёрная OLED, x - чёрная с буквой X.</summary>
    public string AppIcon { get; set; } = "standard";
    /// <summary>Разовое предложение подписаться на Telegram-канал уже показано.</summary>
    public bool TelegramPromptShown { get; set; }
    /// <summary>Прошлую сессию пользователь не выключал сам - основание для автоподключения.</summary>
    public bool WasConnected { get; set; }

    internal void Normalize()
    {
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (Language is not ("system" or "ru" or "en")) Language = "system";
        if (TunStack is not ("system" or "gvisor" or "mixed")) TunStack = "mixed";
        if (CaptchaTarget != "browser") CaptchaTarget = "window";
        if (AppIcon is not ("standard" or "black" or "x")) AppIcon = "standard";
    }
}

public static class AutoConnect
{
    /// <summary>Подключаться при запуске, только если прошлую сессию оборвал не пользователь.</summary>
    public static bool ShouldConnect(AppSettings settings, bool hasActiveServer) =>
        settings.AutoConnect && settings.WasConnected && hasActiveServer;
}
