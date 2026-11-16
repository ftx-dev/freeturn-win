using System.Text.Json.Nodes;

namespace FreeTurn.Core;

/// <summary>Конфиг ядра для <c>mobile.Start</c>. Зеркало <c>config.ClientJSON</c>: декодер ядра строгий
/// (DisallowUnknownFields), поэтому имена и набор ключей менять только вместе с ядром.</summary>
public static class CoreConfig
{
    /// <summary>WG идёт поверх TURN: дефолтные 1420 фрагментируются, 1280 живёт везде.</summary>
    public const int TunnelMtu = 1280;
    const int DefaultThreads = 12;

    public static string Build(Server server, int listenPort, string ownClientId, IReadOnlyList<string> systemDns, bool advancedCaptcha)
    {
        var c = server.Client;
        var o = server.Opts;
        var direct = c.Provider == Provider.Direct;
        var manualDns = DnsList.Normalize(c.CustomDns);
        string[] dns = manualDns.Length > 0 ? manualDns.Split(',') : c.UseCarrierDns ? [.. systemDns] : [];

        var config = new JsonObject
        {
            ["clientId"] = c.ClientId.Length == 0 ? ownClientId : c.ClientId,
            ["dns"] = new JsonObject { ["mode"] = c.DnsMode, ["servers"] = Array(dns) },
            ["log"] = new JsonObject { ["debug"] = c.DebugMode },
            ["obf"] = new JsonObject
            {
                ["key"] = o.ObfEnabled ? o.ObfKey : "",
                ["profile"] = o.ObfEnabled ? o.ObfProfile : ObfProfile.None,
                // Без профиля ядро отвергает ненулевой пейсинг.
                ["timingMs"] = o.ObfEnabled ? o.ObfTimingMs : 0,
            },
            ["peer"] = c.ServerAddress,
            ["provider"] = c.Provider,
            // Локальный порт - для WG/AWG внутри движка; своего туннеля ядро не поднимает.
            ["proxy"] = new JsonObject { ["listen"] = $"127.0.0.1:{listenPort}", ["mode"] = "udp" },
            // Мимо туннеля сокеты ядра уводит привязка к интерфейсу, а не маршруты.
            ["routes"] = false,
            ["subUrl"] = "",
            ["tunnel"] = new JsonObject { ["config"] = "", ["mode"] = "none", ["mtu"] = TunnelMtu },
            ["turn"] = new JsonObject
            {
                ["host"] = c.MagicSwitch ? c.MagicTurn.Trim() : "",
                ["n"] = direct ? 1 : (c.Threads > 0 ? c.Threads : DefaultThreads),
                ["port"] = "",
                ["transport"] = c.UseUdp ? "udp" : "tcp",
            },
            ["vk"] = new JsonObject
            {
                ["advancedCaptcha"] = advancedCaptcha && !c.ManualCaptcha,
                ["autoCaptcha"] = true,
                ["links"] = Array([c.CallLink]),
                ["manualCaptcha"] = c.ManualCaptcha,
                ["platform"] = "desktop",
                ["streamsPerCred"] = c.StreamsPerCred > 0 ? c.StreamsPerCred : DefaultThreads,
            },
        };
        return config.ToJsonString();
    }

    internal static JsonArray Array(IEnumerable<string> items) => [.. items.Select(i => (JsonNode)JsonValue.Create(i)!)];
}
