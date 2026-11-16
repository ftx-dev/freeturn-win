using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace FreeTurn.Core;

public readonly struct AppVersion(string text) : IComparable<AppVersion>
{
    readonly int[] parts = text.Trim().TrimStart('v').Split('.')
        .Select(p => int.TryParse(new string(p.TakeWhile(char.IsAsciiDigit).ToArray()), out var n) ? n : 0).ToArray();

    public int CompareTo(AppVersion other)
    {
        var n = Math.Max(parts.Length, other.parts.Length);
        for (var i = 0; i < n; i++)
        {
            var a = i < parts.Length ? parts[i] : 0;
            var b = i < other.parts.Length ? other.parts[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
}

public sealed record AvailableUpdate(string Version, Uri SetupUrl, Uri? ChecksumsUrl, Uri? PageUrl, string Notes);

/// <summary>Обновления из GitHub Releases. Установщик собирается под каждую архитектуру отдельно:
/// драйвер wintun в движке должен совпадать с процессором. Имя: FreeTurnX-&lt;версия&gt;-&lt;x64|arm64&gt;-setup.exe.</summary>
public static class UpdateCheck
{
    public const string Repository = "huhhuh78787878-collab/freeturn-win";
    public static readonly Uri LatestUrl = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static AvailableUpdate? Available(string json, string current, string arch)
    {
        var release = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("release json");
        if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true) return null;
        var tag = (string?)release["tag_name"] ?? throw new FormatException("tag_name");
        if (!(new AppVersion(tag) > new AppVersion(current))) return null;

        Uri? Asset(Func<string, bool> match)
        {
            foreach (var a in release["assets"]?.AsArray() ?? [])
            {
                var name = (string?)a?["name"] ?? "";
                if (match(name) && Uri.TryCreate((string?)a?["browser_download_url"], UriKind.Absolute, out var url) && IsGitHub(url))
                    return url;
            }
            return null;
        }

        var setup = Asset(n => n.StartsWith("FreeTurnX-") && n.EndsWith($"-{arch}-setup.exe"));
        if (setup is null) return null;
        Uri.TryCreate((string?)release["html_url"], UriKind.Absolute, out var page);
        return new AvailableUpdate(tag.TrimStart('v'), setup, Asset(n => n == "SHA256SUMS.txt"), page, (string?)release["body"] ?? "");
    }

    /// <summary>Ссылки только с github.com: подменённый ответ не уведёт на чужой хост.</summary>
    internal static bool IsGitHub(Uri url) =>
        url.Scheme == "https" && (url.Host == "github.com" || url.Host.EndsWith(".githubusercontent.com"));

    /// <summary>Строка «&lt;sha256&gt;  &lt;имя файла&gt;» из SHA256SUMS.txt.</summary>
    public static string? Checksum(string name, string sums)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2 && fields[1].Trim().TrimStart('*') == name) return fields[0].ToLowerInvariant();
        }
        return null;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
