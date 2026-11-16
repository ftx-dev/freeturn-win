using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using FreeTurn.Core;

namespace FreeTurnX.Services;

/// <summary>Физическая сеть и её смена. Событий при одной смене приходит несколько - сообщаем,
/// когда они стихнут.</summary>
public sealed class NetworkWatcher(SynchronizationContext ui) : INetworkWatch
{
    public event Action<NetworkSnapshot?>? Changed;
    Timer? debounce;
    bool watching;

    public NetworkSnapshot? Current() => NetworkSelect.Pick(Candidates());

    public static NetworkCandidate[] Candidates() =>
        [.. NetworkInterface.GetAllNetworkInterfaces().Select(n =>
        {
            IPInterfaceProperties? props = null;
            try { props = n.GetIPProperties(); } catch (NetworkInformationException) { }
            var index = 0;
            try { index = props?.GetIPv4Properties()?.Index ?? 0; } catch (NetworkInformationException) { }
            var gateway = props?.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) ?? false;
            var dns = props?.DnsAddresses.Where(a => !a.IsIPv6SiteLocal).Select(a => a.ToString()).ToArray() ?? [];
            // shortcut: метрику интерфейса .NET не отдаёт - проводная сеть предпочтительнее Wi-Fi, как у Windows по умолчанию;
            // если понадобится точный выбор - GetIpInterfaceEntry из iphlpapi.
            var metric = n.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 10,
                NetworkInterfaceType.Wireless80211 => 20,
                _ => 30,
            };
            return new NetworkCandidate(n.Name, n.Description, index, metric, n.OperationalStatus == OperationalStatus.Up,
                n.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Loopback or NetworkInterfaceType.Ppp,
                gateway, dns);
        })];

    public void Start()
    {
        if (watching) return;
        watching = true;
        NetworkChange.NetworkAddressChanged += OnChange;
        NetworkChange.NetworkAvailabilityChanged += OnChange;
    }

    public void Stop()
    {
        watching = false;
        NetworkChange.NetworkAddressChanged -= OnChange;
        NetworkChange.NetworkAvailabilityChanged -= OnChange;
        debounce?.Dispose();
        debounce = null;
    }

    void OnChange(object? sender, EventArgs e)
    {
        debounce?.Dispose();
        debounce = new Timer(_ =>
        {
            var snapshot = Current();
            ui.Post(_ => { if (watching) Changed?.Invoke(snapshot); }, null);
        }, null, 1000, Timeout.Infinite);
    }

    /// <summary>Свободный UDP-порт на loopback для ядра. Между выбором и стартом порт могут занять -
    /// тогда ядро вернёт ошибку старта, и подключение завершится с ней.</summary>
    public static int FreeUdpPort()
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)s.LocalEndPoint!).Port;
    }
}
