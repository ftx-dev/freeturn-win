using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FreeTurn.Core.Tests;

public class Socks5Tests
{
    [Fact]
    public void ProxyConfigUsesTcpDnsAndExcludesPhoneFromTun()
    {
        var config = T.Obj(Socks5EngineConfig.Build(new() { Address = "192.168.1.20" }, "mixed"));
        Assert.Null(config["endpoints"]);
        Assert.Equal("socks", (string?)config["outbounds"]![0]!["type"]);
        Assert.Equal("tcp", (string?)config["outbounds"]![0]!["network"]);
        Assert.Null(config["outbounds"]![0]!["username"]);
        Assert.Equal("tcp", (string?)config["dns"]!["servers"]![0]!["type"]);
        Assert.Equal("phone-proxy", (string?)config["dns"]!["servers"]![0]!["detour"]);
        Assert.Equal("192.168.1.20/32", (string?)config["inbounds"]![0]!["route_exclude_address"]![0]);
        Assert.Equal("reject", (string?)config["route"]!["rules"]![2]!["action"]);
        Assert.Equal("phone-proxy", (string?)config["route"]!["final"]);
    }

    [Fact]
    public void UdpAndAuthAreExplicitAndIpv6PhoneIsExcluded()
    {
        var config = T.Obj(Socks5EngineConfig.Build(new() { Address = "fd00::2", Username = "u", Password = "p", Udp = true }, "system"));
        Assert.Null(config["outbounds"]![0]!["network"]);
        Assert.Equal("u", (string?)config["outbounds"]![0]!["username"]);
        Assert.Equal("p", (string?)config["outbounds"]![0]!["password"]);
        Assert.Equal(2, config["route"]!["rules"]!.AsArray().Count);
        Assert.Equal("fd00::2/128", (string?)config["inbounds"]![0]!["route_exclude_address"]![0]);
    }

    [Theory]
    [InlineData("", 1080)]
    [InlineData("http://192.168.1.2", 1080)]
    [InlineData("0.0.0.0", 1080)]
    [InlineData("192.168.1.2", 0)]
    [InlineData("192.168.1.2", 65536)]
    public void InvalidEndpointDoesNotBuild(string address, int port) =>
        Assert.Throws<Socks5Exception>(() => Socks5EngineConfig.Build(new() { Address = address, Port = port }, "mixed"));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ProbeNegotiatesWithPhoneAndRejectsBadPassword(bool auth, bool accepted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var settings = new Socks5Settings { Address = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            Username = auth ? "test-user" : "", Password = auth ? "test-pass" : "" };
        var phone = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = socket.GetStream();
            var greeting = new byte[3];
            await stream.ReadExactlyAsync(greeting, timeout.Token);
            Assert.Equal(new byte[] { 5, 1, auth ? (byte)2 : (byte)0 }, greeting);
            // Split replies exercise partial TCP reads.
            await stream.WriteAsync(new byte[] { 5 }, timeout.Token);
            await stream.WriteAsync(new byte[] { greeting[2] }, timeout.Token);
            if (!auth) return;
            var expected = new byte[] { 1, 9 }.Concat(Encoding.UTF8.GetBytes("test-user")).Concat(new byte[] { 9 }).Concat(Encoding.UTF8.GetBytes("test-pass")).ToArray();
            var request = new byte[expected.Length];
            await stream.ReadExactlyAsync(request, timeout.Token);
            Assert.Equal(expected, request);
            await stream.WriteAsync(new byte[] { 1, accepted ? (byte)0 : (byte)1 }, timeout.Token);
        });
        if (accepted) await Socks5Probe.CheckAsync(settings, timeout.Token);
        else Assert.Equal(Socks5Error.Authentication, (await Assert.ThrowsAsync<Socks5Exception>(() => Socks5Probe.CheckAsync(settings, timeout.Token))).Error);
        await phone;
    }

    [Fact]
    public async Task SocksModeNeverStartsCoreAndCannotOverlapVpn()
    {
        var log = new List<string>();
        var engine = new FakeEngine(log);
        var core = new FakeCore(log);
        var controller = new ConnectionController(() => core, () => engine, new FakeNetwork(), () => 41000, (_, _) => Task.CompletedTask);
        await controller.ConnectSocks5Async(new() { Socks5 = new() { Address = "192.168.1.20" } });
        Assert.IsType<ConnectionState.Connected>(controller.State);
        Assert.True(controller.IsSocks5);
        Assert.False(core.Launched);
        Assert.Single(engine.Started);
        await controller.ConnectAsync(new Server(), Presets.AllViaVpn, new(), "");
        Assert.True(controller.IsSocks5);
        Assert.Single(engine.Started);
        await controller.DisconnectAsync();
        Assert.True(engine.Stopped);
        Assert.IsType<ConnectionState.Off>(controller.State);
    }

    [Fact]
    public async Task DisconnectDuringProxyProbeNeverInstallsRoutes()
    {
        var engine = new FakeEngine([]);
        var probe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new ConnectionController(() => new FakeCore([]), () => engine, new FakeNetwork(), () => 41000, (_, _) => probe.Task);
        var connect = controller.ConnectSocks5Async(new() { Socks5 = new() { Address = "192.168.1.20" } });
        await controller.DisconnectAsync();
        probe.SetResult();
        await connect;
        Assert.Empty(engine.Started);
        Assert.IsType<ConnectionState.Off>(controller.State);
    }
}
