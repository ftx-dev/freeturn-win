using System.Globalization;

namespace FreeTurn.Core;

public static class Formatters
{
    public static string Bytes(long count, bool english = false)
    {
        string[] units = english ? ["B", "KB", "MB", "GB", "TB"] : ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double value = Math.Max(count, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        // Байты и трёхзначные значения - без дробной части: «250 МБ», но «1,5 КБ».
        var text = unit == 0 || value >= 100
            ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", english ? "." : ",");
        return $"{text} {units[unit]}";
    }

    public static string Rate(long bytesPerSecond, bool english = false) => Bytes(bytesPerSecond, english) + (english ? "/s" : "/с");

    public static string Duration(TimeSpan interval)
    {
        var total = (long)Math.Max(interval.TotalSeconds, 0);
        var (h, m, s) = (total / 3600, total % 3600 / 60, total % 60);
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }
}

/// <summary>Физическая сеть машины: к её интерфейсу привязываются сокеты ядра, её DNS оно использует.</summary>
public sealed record NetworkSnapshot(string Name, int IfIndex, IReadOnlyList<string> Dns)
{
    public bool SameAs(NetworkSnapshot? other) => other is not null && Name == other.Name && IfIndex == other.IfIndex && Dns.SequenceEqual(other.Dns);
}

/// <summary>Кандидат на «физическую сеть»: интерфейс с маршрутом по умолчанию.</summary>
public sealed record NetworkCandidate(string Name, string Description, int IfIndex, int Metric, bool IsUp, bool IsTunnel, bool HasGateway, IReadOnlyList<string> Dns);

public static class NetworkSelect
{
    /// <summary>Лучший интерфейс без туннелей: свой адаптер FreeTurn, wintun и чужие VPN (WireGuard, TAP)
    /// отпадают, иначе сокеты ядра завернулись бы обратно в туннель.</summary>
    public static NetworkSnapshot? Pick(IEnumerable<NetworkCandidate> candidates) =>
        candidates
            .Where(c => c.IsUp && c.HasGateway && !c.IsTunnel && c.IfIndex > 0 && !LooksLikeTunnel(c))
            .OrderBy(c => c.Metric).ThenBy(c => c.IfIndex)
            .Select(c => new NetworkSnapshot(c.Name, c.IfIndex, c.Dns))
            .FirstOrDefault();

    static bool LooksLikeTunnel(NetworkCandidate c) =>
        c.Name.Equals(EngineConfig.InterfaceName, StringComparison.OrdinalIgnoreCase)
        || new[] { "wintun", "wireguard", "tap-windows", "tap adapter", "openvpn" }
            .Any(t => c.Description.Contains(t, StringComparison.OrdinalIgnoreCase));
}
