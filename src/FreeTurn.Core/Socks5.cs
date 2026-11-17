using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace FreeTurn.Core;

public sealed record Socks5Settings
{
    public string Address { get; set; } = "";
    public int Port { get; set; } = 1080;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Udp { get; set; }

    public bool IsValid => IPAddress.TryParse(Address.Trim(), out var ip)
        && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any)
        && !ip.IsIPv6Multicast && (ip.AddressFamily != AddressFamily.InterNetwork || ip.GetAddressBytes()[0] < 224)
        && Port is >= 1 and <= 65535
        && Encoding.UTF8.GetByteCount(Username) <= 255 && Encoding.UTF8.GetByteCount(Password) <= 255
        && (Username.Length > 0 || Password.Length == 0);

    internal void Normalize()
    {
        Address = (Address ?? "").Trim();
        Username ??= "";
        Password ??= "";
    }
}

public enum Socks5Error { InvalidSettings, Unreachable, Authentication, Protocol, NetworkChanged }
public sealed class Socks5Exception(Socks5Error error) : Exception(error.ToString())
{
    public Socks5Error Error { get; } = error;
}

public static class Socks5Probe
{
    // Authenticate before installing system routes, so a wrong address/password
    // cannot leave the computer routed into an unusable proxy.
    public static async Task CheckAsync(Socks5Settings settings, CancellationToken cancellation)
    {
        if (!settings.IsValid) throw new Socks5Exception(Socks5Error.InvalidSettings);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(settings.Address), settings.Port, timeout.Token);
            using var stream = client.GetStream();
            var authenticated = settings.Username.Length > 0;
            await stream.WriteAsync(new byte[] { 5, 1, authenticated ? (byte)2 : (byte)0 }, timeout.Token);
            var reply = new byte[2];
            await stream.ReadExactlyAsync(reply, timeout.Token);
            if (reply[0] != 5) throw new Socks5Exception(Socks5Error.Protocol);
            if (reply[1] != (authenticated ? 2 : 0)) throw new Socks5Exception(Socks5Error.Authentication);
            if (!authenticated) return;
            var user = Encoding.UTF8.GetBytes(settings.Username);
            var password = Encoding.UTF8.GetBytes(settings.Password);
            await stream.WriteAsync(new byte[] { 1, (byte)user.Length }.Concat(user).Concat(new byte[] { (byte)password.Length }).Concat(password).ToArray(), timeout.Token);
            await stream.ReadExactlyAsync(reply, timeout.Token);
            if (reply[0] != 1 || reply[1] != 0) throw new Socks5Exception(Socks5Error.Authentication);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new Socks5Exception(Socks5Error.Unreachable); }
        catch (Exception e) when (e is SocketException or IOException) { throw new Socks5Exception(Socks5Error.Unreachable); }
    }
}

public static class Socks5EngineConfig
{
    public static string Build(Socks5Settings settings, string stack)
    {
        if (!settings.IsValid) throw new Socks5Exception(Socks5Error.InvalidSettings);
        if (stack is not ("mixed" or "system" or "gvisor")) throw new ArgumentException("Invalid TUN stack");
        var ip = IPAddress.Parse(settings.Address);
        var proxy = new JsonObject
        {
            ["type"] = "socks", ["tag"] = "phone-proxy", ["server"] = ip.ToString(),
            ["server_port"] = settings.Port, ["version"] = "5", ["connect_timeout"] = "10s",
        };
        if (!settings.Udp) proxy["network"] = "tcp";
        if (settings.Username.Length > 0)
        {
            proxy["username"] = settings.Username;
            proxy["password"] = settings.Password;
        }
        var rules = new JsonArray(
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["action"] = "hijack-dns", ["protocol"] = "dns" });
        if (!settings.Udp) rules.Add(new JsonObject { ["network"] = "udp", ["action"] = "reject" });
        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info" },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray(new JsonObject { ["type"] = "tcp", ["tag"] = "proxy-dns", ["server"] = "1.1.1.1", ["detour"] = "phone-proxy" }),
                ["final"] = "proxy-dns", ["strategy"] = "ipv4_only",
            },
            ["inbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "tun", ["tag"] = "tun-in", ["interface_name"] = EngineConfig.InterfaceName,
                ["address"] = CoreConfig.Array(["172.19.0.1/30", "fdfe:dcba:9876::1/126"]),
                ["auto_route"] = true, ["strict_route"] = true, ["stack"] = stack, ["mtu"] = CoreConfig.TunnelMtu,
                ["route_exclude_address"] = CoreConfig.Array([ip + (ip.AddressFamily == AddressFamily.InterNetwork ? "/32" : "/128")]),
            }),
            ["outbounds"] = new JsonArray(proxy),
            ["route"] = new JsonObject { ["auto_detect_interface"] = true, ["final"] = "phone-proxy", ["rules"] = rules },
        }.ToJsonString();
    }
}
