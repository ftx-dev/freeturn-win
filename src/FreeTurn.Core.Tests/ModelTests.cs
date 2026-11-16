using System.Text.Json;

namespace FreeTurn.Core.Tests;

public class ModelTests
{
    static Server Decode(string json)
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, "servers.json"), $$"""{"list":[{{json}}]}""");
        return new Store(dir).Load("servers.json", new ServersFile()).List[0];
    }

    [Fact]
    public void DecodesAndroidShape()
    {
        var s = Decode("""
            {"id":"abc","name":"RU","ssh":{"ip":"1.2.3.4"},
             "client":{"serverAddress":"1.2.3.4:56000","callLink":"L","provider":"vk","threads":10,"useUdp":true,
                       "localPort":"127.0.0.1:9000","tunnelTransport":"wireguard","wireGuardConfig":"[Interface]"},
             "proxyListen":"0.0.0.0:56000","opts":{"obfProfile":"rtpopus3","obfKey":"k","obfTimingMs":20,"kcp":{"noDelay":1}}}
            """);
        Assert.Equal("abc", s.Id);
        Assert.Equal("1.2.3.4:56000", s.Client.ServerAddress);
        Assert.Equal(10, s.Client.Threads);
        Assert.True(s.Client.UseUdp);
        Assert.True(s.Client.WireGuardActive);
        Assert.Equal("rtpopus3", s.Opts.ObfProfile);
        Assert.True(s.Opts.ObfEnabled);
    }

    [Fact]
    public void MissingFieldsUseDefaultsAndUnknownValuesFallBack()
    {
        var s = Decode("""{"id":"a","name":"n","client":{"provider":"xyz","dnsMode":"zzz"},"opts":{"obfProfile":"rot13","obfTimingMs":999}}""");
        Assert.Equal(12, s.Client.Threads);
        Assert.Equal("vk", s.Client.Provider);
        Assert.Equal("auto", s.Client.DnsMode);
        Assert.True(s.Client.UseCarrierDns);
        Assert.Equal("none", s.Opts.ObfProfile);
        Assert.Equal(60, s.Opts.ObfTimingMs);
    }

    [Fact]
    public void ObfKeyValidation()
    {
        Assert.True(ObfProfile.IsValidKey(string.Concat(Enumerable.Repeat("aF09", 16))));
        Assert.False(ObfProfile.IsValidKey(new string('a', 63)));
        Assert.False(ObfProfile.IsValidKey(new string('g', 64)));
        Assert.True(ObfProfile.IsValidKey(ObfProfile.GenerateKey()));
    }

    [Fact]
    public void StoreRoundTripAndMissingFile()
    {
        var store = new Store(T.TempDir());
        Assert.Empty(store.Load("servers.json", new ServersFile()).List);
        var server = new Server { Name = "A" };
        server.Client.RoutingProfileId = "p1";
        var file = new ServersFile();
        file.Add(server, true);
        store.Save(file, "servers.json");
        var back = store.Load("servers.json", new ServersFile());
        Assert.Equal("A", back.Active!.Name);
        Assert.Equal("p1", back.Active.Client.RoutingProfileId);
        Assert.Equal(["servers.json"], Directory.GetFiles(store.Directory).Select(Path.GetFileName));
    }

    // Битый файл не затирается дефолтом: иначе один сбой записи стирал бы все серверы.
    [Fact]
    public void CorruptFileThrowsAndIsKept()
    {
        var dir = T.TempDir();
        var path = Path.Combine(dir, "servers.json");
        File.WriteAllText(path, """{"list":[""");
        var e = Assert.Throws<StoreCorruptException>(() => new Store(dir).Load("servers.json", new ServersFile()));
        Assert.Equal("servers.json", e.FileName);
        Assert.Equal("""{"list":[""", File.ReadAllText(path));
    }

    [Fact]
    public void OwnClientIdIsStable32Hex()
    {
        var store = new Store(T.TempDir());
        var a = store.OwnClientId();
        Assert.Equal(a, store.OwnClientId());
        Assert.Matches("^[0-9a-f]{32}$", a);
    }

    [Fact]
    public void ServersFileOperations()
    {
        var file = new ServersFile();
        Server a = new() { Name = "A" }, b = new() { Name = "B" };
        file.Add(a, activate: false);
        Assert.Equal(a.Id, file.ActiveId);
        file.Add(b, activate: true);
        Assert.Equal(b.Id, file.ActiveId);
        var copy = file.Clone(a.Id)!;
        Assert.Equal("A (копия)", copy.Name);
        Assert.NotEqual(a.Id, copy.Id);
        copy.Client.Threads = 3;
        Assert.Equal(12, a.Client.Threads);
        file.Rename(a.Id, "  New ");
        Assert.Equal("New", a.Name);
        file.Rename(a.Id, "  ");
        Assert.Equal("New", a.Name);
        file.Remove(b.Id);
        Assert.Equal(a.Id, file.ActiveId);
        file.Remove(a.Id);
        file.Remove(copy.Id);
        Assert.Null(file.ActiveId);
        Assert.Null(file.Clone("missing"));
    }

    [Fact]
    public void SettingsNormalize()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{"captchaTarget":"popup","tunStack":"lwip","theme":"neon","appIcon":"rainbow"}""");
        var s = new Store(dir).Load("settings.json", new AppSettings());
        Assert.Equal("window", s.CaptchaTarget);
        Assert.Equal("mixed", s.TunStack);
        Assert.Equal("system", s.Theme);
        Assert.True(s.CheckUpdates);
        Assert.Equal("x", s.AppIcon);
        Assert.Equal("x", new AppSettings().AppIcon);
    }

    [Fact]
    public void FreshInstallDefaultsAndSavedPreferences()
    {
        var dir = T.TempDir();
        var store = new Store(dir);
        var settings = store.Load("settings.json", new AppSettings());
        Assert.Equal("x", settings.AppIcon);
        Assert.True(settings.AdvancedCaptcha);
        var server = new Server();
        Assert.Equal(Provider.Relay, server.Client.Provider);
        Assert.False(server.Client.ManualCaptcha);
        Assert.Equal(Presets.RussiaDirect.Id, RoutingFile.Initial().ProfileFor(server).Id);
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{"appIcon":"standard","advancedCaptcha":false}""");
        settings = store.Load("settings.json", new AppSettings());
        Assert.Equal("standard", settings.AppIcon);
        Assert.False(settings.AdvancedCaptcha);
        var routing = new RoutingFile { SharedProfileId = Presets.AllViaVpn.Id }.Normalized();
        Assert.Equal(Presets.AllViaVpn.Id, routing.SharedProfileId);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void AutoConnectNeedsAllThree(bool auto, bool was, bool hasServer, bool expected) =>
        Assert.Equal(expected, AutoConnect.ShouldConnect(new AppSettings { AutoConnect = auto, WasConnected = was }, hasServer));
}
