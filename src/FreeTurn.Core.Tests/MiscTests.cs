namespace FreeTurn.Core.Tests;

public class LogTests
{
    [Fact]
    public void Redacts()
    {
        Assert.Equal("dial x.x.x.x:• ok", Redactor.Redact("dial 1.2.3.4:56000 ok", []));
        Assert.Equal("via x:x done", Redactor.Redact("via 2001:db8::1 done", []));
        Assert.Equal("-links ••• -n 12", Redactor.Redact("-links https://vk.ru/call/join/AbC-123_x -n 12", []));
        Assert.Equal("-obf-key ••• -client-id •••", Redactor.Redact($"-obf-key {T.Hex64} -client-id {T.Hex32}", []));
        Assert.Equal("k=••• ok", Redactor.Redact("k=cHJpdmF0ZQ== ok", ["cHJpdmF0ZQ==", ""]));
        Assert.Equal("-peer 1.2.3.4:56000 -links •••", Redactor.Redact("-peer 1.2.3.4:56000 -links https://vk.ru/call/join/x", [], maskAddresses: false));
        foreach (var text in new[] { "12:34:56 started", "version=v4.1.5", "streams 3/12", "деление 10:30 и 1.2.3" })
            Assert.Equal(text, Redactor.Redact(text, ["ab"]));
    }

    [Fact]
    public void LogFileRotatesAndExports()
    {
        var dir = T.TempDir();
        var file = new LogFile(dir, 1000);
        for (var i = 0; i < 12; i++) file.Append(new string('x', 99) + i);
        Assert.True(File.Exists(Path.Combine(dir, "session.1.log")));
        Assert.InRange(new FileInfo(Path.Combine(dir, "session.log")).Length, 1, 1000);
        var out1 = Path.Combine(dir, "export.txt");
        file.Export(out1);
        var text = File.ReadAllText(out1);
        Assert.True(text.IndexOf("x10", StringComparison.Ordinal) < text.IndexOf("x11", StringComparison.Ordinal));
        file.Clear();
        Assert.False(File.Exists(Path.Combine(dir, "session.log")));
    }
}

public class CaptchaTests
{
    const string Page = "http://localhost:8765/not_robot_captcha?a=1";
    const string Page2 = "http://localhost:8765/not_robot_captcha?a=2";

    [Theory]
    [InlineData("http://127.0.0.1:8765/a", true)]
    [InlineData("http://localhost:8765/", true)]
    [InlineData("https://localhost:8765/", false)]
    [InlineData("http://localhost:8766/", false)]
    [InlineData("http://localhost/", false)]
    [InlineData("http://evil.com:8765/", false)]
    [InlineData("http://user@localhost:8765/", false)]
    [InlineData("http://localhost.evil.com:8765/", false)]
    [InlineData("", false)]
    [InlineData("file:///etc/passwd", false)]
    public void OnlyCorePageIsLocal(string url, bool expected) => Assert.Equal(expected, CaptchaUrl.IsLocal(url));

    [Fact]
    public void PresenterFlow()
    {
        var p = new CaptchaPresenter();
        Assert.Equal(CaptchaAction.None, p.ActionFor(null, "window"));
        Assert.Equal(new CaptchaAction(CaptchaActionKind.ShowWindow, Page), p.ActionFor(new(Page, true), "window"));
        Assert.True(p.NeedsAttention);
        // Ядро шлёт состояние не один раз: повтор не открывает второе окно.
        Assert.Equal(CaptchaAction.None, p.ActionFor(new(Page, true), "window"));
        Assert.Equal(new CaptchaAction(CaptchaActionKind.OpenBrowser, Page), p.ReopenAction("browser"));
        Assert.Equal(new CaptchaAction(CaptchaActionKind.LoadHidden, Page2), p.ActionFor(new(Page2, false), "window"));
        Assert.False(p.NeedsAttention);
        Assert.Equal(CaptchaAction.Dismiss, p.ActionFor(new("", false), "window"));
        Assert.Equal(CaptchaAction.None, p.ReopenAction("window"));
        // Внешний адрес не открываем ни в окне, ни в браузере.
        Assert.Equal(CaptchaAction.None, p.ActionFor(new("http://evil.com:8765/x", true), "browser"));
    }
}

public class UpdateTests
{
    static string Release(string tag = "v0.2.0", bool draft = false, bool pre = false, string[]? assets = null, string host = "github.com")
    {
        assets ??= ["FreeTurnX-0.2.0-x64-setup.exe", "FreeTurnX-0.2.0-arm64-setup.exe", "SHA256SUMS.txt"];
        var list = string.Join(",", assets.Select(a => $$"""{"name":"{{a}}","browser_download_url":"https://{{host}}/o/r/releases/download/{{tag}}/{{a}}"}"""));
        return $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"html_url":"https://github.com/o/r","body":"Что нового","assets":[{{list}}]}""";
    }

    [Fact]
    public void VersionComparison()
    {
        Assert.True(new AppVersion("0.2.0") > new AppVersion("0.1.0"));
        Assert.True(new AppVersion("v0.10.0") > new AppVersion("0.9.9"));
        Assert.Equal(0, new AppVersion("1.0").CompareTo(new AppVersion("1.0.0")));
    }

    // Установщик строго своей архитектуры: драйвер wintun в движке должен совпадать с процессором.
    [Theory]
    [InlineData("x64", "FreeTurnX-0.2.0-x64-setup.exe")]
    [InlineData("arm64", "FreeTurnX-0.2.0-arm64-setup.exe")]
    public void PicksSetupForArchitecture(string arch, string expected)
    {
        var u = UpdateCheck.Available(Release(), "0.1.0", arch)!;
        Assert.Equal("0.2.0", u.Version);
        Assert.Equal(expected, Path.GetFileName(u.SetupUrl.AbsolutePath));
        Assert.Equal("SHA256SUMS.txt", Path.GetFileName(u.ChecksumsUrl!.AbsolutePath));
    }

    [Fact]
    public void NoUpdateCases()
    {
        Assert.Null(UpdateCheck.Available(Release("v0.1.0"), "0.1.0", "x64"));
        Assert.Null(UpdateCheck.Available(Release(draft: true), "0.1.0", "x64"));
        Assert.Null(UpdateCheck.Available(Release(pre: true), "0.1.0", "x64"));
        Assert.Null(UpdateCheck.Available(Release(assets: ["FreeTurnX-0.2.0-arm64-setup.exe"]), "0.1.0", "x64"));
        Assert.Null(UpdateCheck.Available(Release(host: "evil.example"), "0.1.0", "x64"));
        Assert.ThrowsAny<Exception>(() => UpdateCheck.Available("nope", "0.1.0", "x64"));
    }

    [Fact]
    public void ChecksumAndSha()
    {
        var sums = $"{new string('a', 64)}  FreeTurnX-0.2.0-x64-setup.exe\n{new string('b', 64)}  FreeTurnX-0.2.0-arm64-setup.exe\n";
        Assert.Equal(new string('b', 64), UpdateCheck.Checksum("FreeTurnX-0.2.0-arm64-setup.exe", sums));
        Assert.Null(UpdateCheck.Checksum("missing", sums));
        var f = Path.Combine(T.TempDir(), "f");
        File.WriteAllText(f, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", UpdateCheck.Sha256(f));
    }
}

public class FormatterAndNetworkTests
{
    [Fact]
    public void Formats()
    {
        Assert.Equal("0 Б", Formatters.Bytes(0));
        Assert.Equal("1,5 КБ", Formatters.Bytes(1536));
        Assert.Equal("1.5 KB", Formatters.Bytes(1536, english: true));
        Assert.Equal("1,3 ГБ", Formatters.Bytes(1_395_864_371));
        Assert.Equal("250 МБ", Formatters.Bytes(250L * 1024 * 1024));
        Assert.Equal("4,2 МБ/с", Formatters.Rate(4_404_019));
        Assert.Equal("02:14", Formatters.Duration(TimeSpan.FromSeconds(134)));
        Assert.Equal("1:02:05", Formatters.Duration(TimeSpan.FromSeconds(3725)));
    }

    static NetworkCandidate C(string name, int index, int metric = 25, string description = "Intel Wi-Fi", bool tunnel = false, bool gateway = true) =>
        new(name, description, index, metric, true, tunnel, gateway, ["192.168.1.1"]);

    // Свой туннель и чужие VPN не годятся: сокеты ядра завернулись бы обратно в туннель.
    [Fact]
    public void PicksPhysicalNetwork()
    {
        var picked = NetworkSelect.Pick([
            C("FreeTurn", 40, 0, "Wintun Userspace Tunnel"), C("wg0", 41, 1, "WireGuard Tunnel"),
            C("Ethernet", 12, 35, "Realtek"), C("Wi-Fi", 7, 25), C("vEthernet", 30, 5, "Hyper-V", gateway: false)]);
        Assert.Equal(("Wi-Fi", 7), (picked!.Name, picked.IfIndex));
        Assert.Null(NetworkSelect.Pick([C("FreeTurn", 40, 0, "Wintun Userspace Tunnel")]));
        Assert.Null(NetworkSelect.Pick([]));
    }
}
