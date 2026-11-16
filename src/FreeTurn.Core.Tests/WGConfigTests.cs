namespace FreeTurn.Core.Tests;

public class WGConfigTests
{
    [Fact]
    public void PlainWireGuard()
    {
        var c = WGConfig.Parse(T.PlainWG);
        Assert.False(c.IsAmnezia);
        Assert.Equal(["10.13.13.2/32"], c.Addresses);
        Assert.Equal(["1.1.1.1", "8.8.8.8"], c.Dns);
        Assert.Equal(["0.0.0.0/0", "::/0"], c.Peers[0].AllowedIPs);
        Assert.Equal(25, c.Peers[0].PersistentKeepalive);
    }

    [Fact]
    public void AmneziaKeys()
    {
        var c = WGConfig.Parse("[Interface]\nPrivateKey = a\nAddress = 10.0.0.2/32\nJc = 4\nH1 = 12345\nI1 = <b 0xc7000000010>\n[Peer]\nPublicKey = b\n");
        Assert.True(c.IsAmnezia);
        Assert.Equal("4", c.Amnezia["jc"]);
        Assert.Equal("<b 0xc7000000010>", c.Amnezia["i1"]);
        Assert.Null(c.Peers[0].PresharedKey);
    }

    // Конфиги копируют из панелей и мессенджеров: CRLF, комментарии, чужой регистр, лишние пробелы.
    [Fact]
    public void MessyRealWorldInput()
    {
        var c = WGConfig.Parse("# сверху\r\n[interface]\r\n; коммент\r\nprivatekey=abc=\r\nADDRESS = 10.0.0.2/32 , fd00::2/128  # хвост\r\n\r\n[PEER]\r\nPublicKey   =   pk  ; хвост\r\nallowedips=0.0.0.0/0\r\n");
        Assert.Equal("abc=", c.PrivateKey);
        Assert.Equal(["10.0.0.2/32", "fd00::2/128"], c.Addresses);
        Assert.Equal("pk", c.Peers[0].PublicKey);
    }

    [Theory]
    [InlineData("[Peer]\nPublicKey=p\n", WGConfigError.MissingInterface)]
    [InlineData("", WGConfigError.MissingInterface)]
    [InlineData("[Interface]\nAddress=10.0.0.2\n[Peer]\nPublicKey=p\n", WGConfigError.MissingPrivateKey)]
    [InlineData("[Interface]\nPrivateKey=a\n[Peer]\nPublicKey=p\n", WGConfigError.MissingAddress)]
    [InlineData("[Interface]\nPrivateKey=a\nAddress=10.0.0.2\n", WGConfigError.MissingPeer)]
    [InlineData("[Interface]\nPrivateKey=a\nAddress=10.0.0.2\n[Peer]\nPublicKey=p\n[Peer]\nAllowedIPs=0.0.0.0/0\n", WGConfigError.MissingPublicKey)]
    public void Errors(string text, WGConfigError expected) =>
        Assert.Equal(expected, Assert.Throws<WGConfigException>(() => WGConfig.Parse(text)).Error);
}
