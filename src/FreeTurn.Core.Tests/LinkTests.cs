namespace FreeTurn.Core.Tests;

public class LinkTests
{
    [Fact]
    public void GoldenMinimal()
    {
        var link = FreeturnLink.Parse(T.GoldenMinimal);
        Assert.Equal("vk", link.Provider);
        Assert.Equal("1.2.3.4:56000", link.Peer);
        Assert.Equal("", link.WgConf);
    }

    [Fact]
    public void AllFields()
    {
        var link = FreeturnLink.Parse(T.B64Link($$$"""
            {"v":1,"provider":"direct","peer":"5.6.7.8:56001","transport":"udp","obf":"rtpopus3","key":"{{{T.Hex64}}}",
             "timing":20,"n":8,"spc":6,"cid":"{{{T.Hex32}}}","dns":"doh","dnss":"1.1.1.1,8.8.8.8","mcap":true,
             "name":"RU","vk":"https://vk.ru/call/join/x","wg":"[Interface]\nPrivateKey = a\n","zzz":{"a":1}}
            """));
        Assert.Equal(("direct", "5.6.7.8:56001", "udp", "rtpopus3", T.Hex64, 20), (link.Provider, link.Peer, link.Transport, link.ObfProfile, link.ObfKey, link.ObfTimingMs));
        Assert.Equal((8, 6, T.Hex32, "doh", "1.1.1.1,8.8.8.8", true), (link.N, link.StreamsPerCred, link.ClientId, link.DnsMode, link.DnsServers, link.ManualCaptcha));
        Assert.Equal(("RU", "https://vk.ru/call/join/x", "[Interface]\nPrivateKey = a\n"), (link.Name, link.CallLink, link.WgConf));
    }

    // Ссылки приходят из мессенджеров: пробелы, перевод строки, чужой регистр схемы, padding.
    [Fact]
    public void TolerantInput() =>
        Assert.Equal(FreeturnLink.Parse(T.GoldenMinimal), FreeturnLink.Parse("  FREETURN://" + T.GoldenMinimal["freeturn://".Length..] + "==\n"));

    [Theory]
    [InlineData("http://x", LinkError.InvalidScheme)]
    [InlineData("freeturn://", LinkError.EmptyPayload)]
    [InlineData("freeturn://!!!", LinkError.BadEncoding)]
    public void Errors(string raw, LinkError expected) =>
        Assert.Equal(expected, Assert.Throws<LinkException>(() => FreeturnLink.Parse(raw)).Error);

    [Theory]
    [InlineData("not json", LinkError.BadEncoding)]
    [InlineData("""{"v":2,"provider":"vk","peer":"x"}""", LinkError.UnsupportedVersion)]
    [InlineData("""{"provider":"vk","peer":"x"}""", LinkError.UnsupportedVersion)]
    [InlineData("""{"v":1,"peer":"x"}""", LinkError.MissingProvider)]
    [InlineData("""{"v":1,"provider":"vk"}""", LinkError.MissingPeer)]
    public void PayloadErrors(string json, LinkError expected) =>
        Assert.Equal(expected, Assert.Throws<LinkException>(() => FreeturnLink.Parse(T.B64Link(json))).Error);

    [Fact]
    public void DnsListNormalize()
    {
        Assert.Equal("1.1.1.1,8.8.8.8", DnsList.Normalize("1.1.1.1; bad!token 8.8.8.8"));
        Assert.True(DnsList.HasInvalidTokens("1.1.1.1 bad!token"));
        Assert.False(DnsList.HasInvalidTokens("1.1.1.1, 2001:db8::1"));
    }

    static FreeturnLink L(string extra = "", string provider = "vk", string peer = "1.2.3.4:56000") =>
        FreeturnLink.Parse(T.B64Link($$"""{"v":1,"provider":"{{provider}}","peer":"{{peer}}"{{extra}}}"""));

    [Fact]
    public void ImportRejects()
    {
        Assert.Equal(ImportError.TcpModeUnsupported, Assert.Throws<ImportException>(() => ImportPlanner.Draft(L(""","mode":"tcp" """), [])).Error);
        Assert.Equal(ImportError.UnknownProvider, Assert.Throws<ImportException>(() => ImportPlanner.Draft(L(provider: "zzz"), [])).Error);
        Assert.Equal(ImportError.BadObf, Assert.Throws<ImportException>(() => ImportPlanner.Draft(L(""","obf":"rtpopus3","key":"0123" """), [])).Error);
    }

    [Fact]
    public void ImportDraftFlags()
    {
        var existing = new Server();
        existing.Client.ServerAddress = "HOST.example:56000";
        existing.Client.WireGuardConfig = "[Interface]\nPrivateKey = a";
        var d = ImportPlanner.Draft(L(""","wg":"  [Interface]\nPrivateKey = a\n","vk":" https://vk.ru/call/join/x " """, peer: "host.EXAMPLE:56000"), [existing]);
        Assert.True(d.DuplicateAddress);
        Assert.True(d.DuplicateConf);
        Assert.Equal("https://vk.ru/call/join/x", d.CallLink);
        Assert.True(d.NeedsCallLink);
        Assert.False(d.NeedsWireGuard);
        Assert.Equal("host.EXAMPLE", d.SuggestedName);
        Assert.False(ImportPlanner.Draft(L(provider: "direct"), []).NeedsCallLink);
    }

    [Fact]
    public void MakeServerMapsFields()
    {
        var link = L($$""","n":0,"timing":999,"transport":"udp","obf":"rtpopus3","key":"{{T.Hex64}}","cid":" {{T.Hex32}} ","dns":"zzz","dnss":"1.1.1.1; bad!token 8.8.8.8","mcap":true,"wg":" [Interface] " """);
        var s = ImportPlanner.MakeServer(ImportPlanner.Draft(link, []), "  Мой  ", " L ");
        Assert.Equal(("Мой", "L", 12, true, true, T.Hex32), (s.Name, s.Client.CallLink, s.Client.Threads, s.Client.UseUdp, s.Client.ManualCaptcha, s.Client.ClientId));
        Assert.Equal(("auto", "1.1.1.1,8.8.8.8", "wireguard", "[Interface]"), (s.Client.DnsMode, s.Client.CustomDns, s.Client.TunnelTransport, s.Client.WireGuardConfig));
        Assert.Equal(("rtpopus3", 60), (s.Opts.ObfProfile, s.Opts.ObfTimingMs));
        Assert.Equal("1.2.3.4", ImportPlanner.MakeServer(ImportPlanner.Draft(L(), []), " ", "").Name);
    }
}
