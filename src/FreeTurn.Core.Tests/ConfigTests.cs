using System.Text.Json.Nodes;

namespace FreeTurn.Core.Tests;

public class CoreConfigTests
{
    static JsonObject Build(Action<Server>? edit = null, string[]? dns = null, bool advanced = false)
    {
        var s = new Server { Name = "S" };
        s.Client.ServerAddress = "1.2.3.4:56000";
        s.Client.CallLink = "L";
        edit?.Invoke(s);
        return T.Obj(CoreConfig.Build(s, 41000, T.Hex32, dns ?? ["192.168.1.1"], advanced));
    }

    [Fact]
    public void FixedFieldsAndExactKeys()
    {
        var o = Build();
        Assert.Equal("""{"config":"","mode":"none","mtu":1280}""", o["tunnel"]!.ToJsonString());
        Assert.Equal("""{"listen":"127.0.0.1:41000","mode":"udp"}""", o["proxy"]!.ToJsonString());
        Assert.Equal("desktop", (string?)o["vk"]!["platform"]);
        Assert.False((bool)o["routes"]!);
        // Декодер ядра строгий: лишний ключ - отказ, kcp вне tcp-режима - отказ.
        Assert.Equal(["clientId", "dns", "log", "obf", "peer", "provider", "proxy", "routes", "subUrl", "tunnel", "turn", "vk"], o.Select(p => p.Key).Order());
    }

    [Fact]
    public void ThreadsTransportMagicObf()
    {
        Assert.Equal(1, (int)Build(s => { s.Client.Provider = "direct"; s.Client.Threads = 10; })["turn"]!["n"]!);
        Assert.Equal(12, (int)Build(s => s.Client.Threads = 0)["turn"]!["n"]!);
        Assert.Equal("udp", (string?)Build(s => s.Client.UseUdp = true)["turn"]!["transport"]);
        Assert.Equal("", (string?)Build(s => s.Client.MagicTurn = " 9.9.9.9 ")["turn"]!["host"]);
        Assert.Equal("9.9.9.9", (string?)Build(s => { s.Client.MagicSwitch = true; s.Client.MagicTurn = " 9.9.9.9 "; })["turn"]!["host"]);
        Assert.Equal("""{"key":"","profile":"none","timingMs":0}""", Build(s => { s.Opts.ObfKey = T.Hex64; s.Opts.ObfTimingMs = 20; })["obf"]!.ToJsonString());
        Assert.Equal(T.Hex64, (string?)Build(s => { s.Opts.ObfProfile = "rtpopus3"; s.Opts.ObfKey = T.Hex64; })["obf"]!["key"]);
    }

    [Fact]
    public void DnsPriorityAndCaptchaFlags()
    {
        Assert.Equal("""["8.8.8.8","1.1.1.1"]""", Build(s => s.Client.CustomDns = "8.8.8.8, 1.1.1.1")["dns"]!["servers"]!.ToJsonString());
        Assert.Equal("""["192.168.1.1"]""", Build()["dns"]!["servers"]!.ToJsonString());
        Assert.Equal("[]", Build(s => s.Client.UseCarrierDns = false)["dns"]!["servers"]!.ToJsonString());
        Assert.True((bool)Build(advanced: true)["vk"]!["advancedCaptcha"]!);
        Assert.False((bool)Build(s => s.Client.ManualCaptcha = true, advanced: true)["vk"]!["advancedCaptcha"]!);
    }

    /// <summary>Эталоны для Go-теста helper/internal/bridge/fixture_test.go: их принимает декодер ядра.</summary>
    [Theory]
    [InlineData("direct")]
    [InlineData("relay")]
    public void FixtureMatchesHelperTestdata(string kind)
    {
        var s = new Server { Name = "fixture" };
        s.Client.ServerAddress = "1.2.3.4:56000";
        s.Client.Provider = kind == "direct" ? "direct" : "vk";
        s.Client.CallLink = kind == "direct" ? "" : "https://vk.ru/call/join/fixture";
        s.Opts.ObfProfile = "rtpopus3";
        s.Opts.ObfKey = T.Hex64;
        s.Opts.ObfTimingMs = 20;
        T.Fixture($"helper/internal/bridge/testdata/core-config-{kind}.json", CoreConfig.Build(s, 41000, T.Hex32, ["192.168.1.1"], true));
    }
}

public class EngineConfigTests
{
    static RoutingRule R(RuleKind kind, string value, RouteAction action = RouteAction.Direct, bool enabled = true) =>
        new() { Kind = kind, Value = value, Action = action, Enabled = enabled };

    static JsonObject Build(string wg = T.PlainWG, RoutingProfile? profile = null, EngineSettings? settings = null) =>
        T.Obj(EngineConfig.Build(WGConfig.Parse(wg), 41000, profile ?? Presets.AllViaVpn, settings ?? new()));

    static RoutingProfile P(RouteAction def = RouteAction.Vpn, params RoutingRule[] rules) => new() { Name = "T", DefaultAction = def, Rules = [.. rules] };

    static JsonArray UserRules(JsonObject o) => new([.. o["route"]!["rules"]!.AsArray().Skip(3).Select(n => n!.DeepClone())]);

    [Fact]
    public void WireguardEndpointAndTun()
    {
        var o = Build();
        var ep = o["endpoints"]![0]!;
        Assert.Equal("wireguard", (string?)ep["type"]);
        Assert.Equal(1280, (int)ep["mtu"]!);
        var peer = ep["peers"]![0]!;
        Assert.Equal(("127.0.0.1", 41000, 25), ((string?)peer["address"], (int)peer["port"]!, (int)peer["persistent_keepalive_interval"]!));
        Assert.NotNull(peer["pre_shared_key"]);
        var tun = o["inbounds"]![0]!;
        Assert.Equal(("tun", "FreeTurn", true), ((string?)tun["type"], (string?)tun["interface_name"], (bool)tun["auto_route"]!));
    }

    [Fact]
    public void AwgEndpoint()
    {
        var ep = Build(T.AmneziaWG)["endpoints"]![0]!;
        Assert.Equal("awg", (string?)ep["type"]);
        Assert.Equal(4, (int)ep["jc"]!);
        Assert.Equal("1", (string?)ep["h1"]);
        Assert.True((bool)ep["random_trailers"]!);
        Assert.NotNull(ep["header_protection_key"]);
        Assert.NotNull(ep["peers"]![0]!["preshared_key"]);
        Assert.Null(ep["peers"]![0]!["pre_shared_key"]);
    }

    [Fact]
    public void RuleKindsOrderAndBlock()
    {
        var rules = UserRules(Build(profile: P(RouteAction.Vpn,
            R(RuleKind.Domain, "A.com"), R(RuleKind.DomainSuffix, ".yandex.ru"), R(RuleKind.DomainKeyword, "google", RouteAction.Vpn),
            R(RuleKind.Cidr, "10.0.0.0/8"), R(RuleKind.Domain, "off.com", enabled: false), R(RuleKind.DomainSuffix, "ads.example", RouteAction.Block))));
        Assert.Equal("""[{"domain":["a.com"],"outbound":"direct"},{"domain_suffix":["yandex.ru"],"outbound":"direct"},{"domain_keyword":["google"],"outbound":"vpn"},{"ip_cidr":["10.0.0.0/8"],"outbound":"direct"},{"domain_suffix":["ads.example"],"action":"reject"}]""", rules.ToJsonString());
    }

    // Путь уходит в регулярное выражение движка (RE2): пробел не экранируем - RE2 такую escape-последовательность отвергает.
    [Fact]
    public void AppRulesForWindowsPaths()
    {
        var rules = UserRules(Build(profile: P(RouteAction.Vpn,
            R(RuleKind.App, @"C:\Program Files\Mozilla Firefox\firefox.exe"),
            R(RuleKind.App, @"C:\Program Files (x86)\Яндекс\Browser\"))));
        Assert.Equal(@"C:\Program Files\Mozilla Firefox\firefox.exe", (string?)rules[0]!["process_path"]![0]);
        Assert.Equal(@"(?i)^C:\\Program Files \(x86\)\\Яндекс\\Browser\\", (string?)rules[1]!["process_path_regex"]![0]);
    }

    [Fact]
    public void DefaultModesDnsAndIpv6()
    {
        var direct = Build(profile: P(RouteAction.Direct));
        Assert.Equal(("direct", "local-dns"), ((string?)direct["route"]!["final"], (string?)direct["dns"]!["final"]));
        Assert.Equal("ipv4_only", (string?)Build()["dns"]!["strategy"]);
        var v6 = Build("[Interface]\nPrivateKey=a\nAddress=10.0.0.2/32, fd00::2/128\n[Peer]\nPublicKey=p\nAllowedIPs=0.0.0.0/0, ::/0\n");
        Assert.Equal(2, v6["inbounds"]![0]!["address"]!.AsArray().Count);
        Assert.Null(v6["dns"]!["strategy"]);
        var partial = Build("[Interface]\nPrivateKey=a\nAddress=10.0.0.2\n[Peer]\nPublicKey=p\nAllowedIPs=10.0.0.0/8\n");
        Assert.Equal("""["10.0.0.0/8"]""", partial["inbounds"]![0]!["route_address"]!.ToJsonString());
        Assert.Equal("9.9.9.9", (string?)Build(settings: new(VpnDns: "9.9.9.9"))["dns"]!["servers"]![0]!["server"]);
    }

    [Theory]
    [InlineData(RuleKind.Domain, "  ")]
    [InlineData(RuleKind.Cidr, "10.0.0.0/33")]
    [InlineData(RuleKind.Cidr, "300.1.1.1")]
    [InlineData(RuleKind.Cidr, "10")]
    [InlineData(RuleKind.App, "firefox.exe")]
    [InlineData(RuleKind.App, "/usr/bin/curl")]
    public void InvalidRulesThrow(RuleKind kind, string value)
    {
        var rule = R(kind, value);
        Assert.False(EngineConfig.IsValid(rule));
        Assert.Equal(rule.Id, Assert.Throws<InvalidRuleException>(() => Build(profile: P(RouteAction.Vpn, rule))).RuleId);
    }

    [Fact]
    public void ValidCidrs()
    {
        Assert.True(EngineConfig.IsCidr("fd00::/8"));
        Assert.True(EngineConfig.IsCidr("8.8.8.8"));
    }

    /// <summary>Эталон для Go-теста санитайзера службы и для проверки настоящим движком (make check-engine).</summary>
    [Fact]
    public void FixtureMatchesDaemonTestdata()
    {
        var p = new RoutingProfile { Id = "fixture", Name = "fixture", Rules = [
            new() { Id = "r1", Kind = RuleKind.App, Value = @"C:\Program Files\Mozilla Firefox\firefox.exe", Action = RouteAction.Direct },
            new() { Id = "r2", Kind = RuleKind.App, Value = @"C:\Program Files (x86)\Steam\", Action = RouteAction.Direct },
            new() { Id = "r3", Kind = RuleKind.DomainSuffix, Value = "yandex.ru", Action = RouteAction.Direct },
            new() { Id = "r4", Kind = RuleKind.DomainKeyword, Value = "google", Action = RouteAction.Vpn },
            new() { Id = "r5", Kind = RuleKind.Cidr, Value = "10.0.0.0/8", Action = RouteAction.Direct },
            new() { Id = "r6", Kind = RuleKind.Domain, Value = "ads.example", Action = RouteAction.Block } ] };
        T.Fixture("helper/internal/daemon/testdata/engine.json", EngineConfig.Build(WGConfig.Parse(T.AmneziaWG), 41000, p, new()));
    }
}

public class RoutingTests
{
    [Fact]
    public void PresetsAndInitial()
    {
        Assert.All(Presets.RussiaDirect.Rules, r => Assert.Equal(RouteAction.Direct, r.Action));
        foreach (var d in new[] { "vk.com", "yandex.ru", "gosuslugi.ru", "ru", "xn--p1ai" })
            Assert.Contains(Presets.RussiaDirect.Rules, r => r.Value == d);
        var file = RoutingFile.Initial();
        Assert.Equal([Presets.AllViaVpn.Id, Presets.RussiaDirect.Id], file.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void FileOperations()
    {
        var file = RoutingFile.Initial();
        file.Remove(Presets.RussiaDirect.Id);
        Assert.Equal(2, file.Profiles.Count);
        var mine = new RoutingProfile { Name = "mine" };
        file.Add(mine);
        file.SharedProfileId = mine.Id;
        var server = new Server();
        Assert.Equal(mine, file.ProfileFor(server));
        server.Client.RoutingProfileId = Presets.RussiaDirect.Id;
        Assert.Equal(Presets.RussiaDirect, file.ProfileFor(server));
        file.Remove(mine.Id);
        Assert.Equal(Presets.AllViaVpn.Id, file.SharedProfileId);
        var copy = file.Clone(Presets.RussiaDirect.Id)!;
        Assert.False(copy.BuiltIn);
        Assert.Equal(Presets.RussiaDirect.Rules.Count, copy.Rules.Count);
        Assert.DoesNotContain(copy.Rules, r => r.Id.StartsWith("builtin"));
    }

    [Fact]
    public void NormalizedRestoresCurrentBuiltIns()
    {
        var stale = Presets.RussiaDirect.Copy();
        stale.Rules.Clear();
        var mine = new RoutingProfile { Name = "mine" };
        var file = new RoutingFile { Profiles = [mine, stale], SharedProfileId = "gone" }.Normalized();
        Assert.Equal([Presets.AllViaVpn.Id, Presets.RussiaDirect.Id, mine.Id], file.Profiles.Select(p => p.Id));
        Assert.NotEmpty(file.Profiles[1].Rules);
        Assert.Equal(Presets.AllViaVpn.Id, file.SharedProfileId);
    }

    [Fact]
    public void ProfileTransferRoundTripAndErrors()
    {
        var back = ProfileTransfer.Import(ProfileTransfer.Export(Presets.RussiaDirect));
        Assert.False(back.BuiltIn);
        Assert.NotEqual(Presets.RussiaDirect.Id, back.Id);
        Assert.Equal(Presets.RussiaDirect.Rules.Select(r => r.Value), back.Rules.Select(r => r.Value));
        Assert.Equal(ProfileTransfer.WrongFormat, Assert.Throws<ProfileTransferException>(() => ProfileTransfer.Import("""{"hello":1}"""u8.ToArray())).Message);
        Assert.Equal(ProfileTransfer.WrongFormat, Assert.Throws<ProfileTransferException>(() => ProfileTransfer.Import("nope"u8.ToArray())).Message);
        Assert.Equal(ProfileTransfer.UnsupportedVersion, Assert.Throws<ProfileTransferException>(() => ProfileTransfer.Import("""{"format":"freeturn-routing","version":2,"profile":{}}"""u8.ToArray())).Message);
    }
}
