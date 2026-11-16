global using Xunit;
global using FreeTurn.Core;
using System.Text;
using System.Text.Json.Nodes;

namespace FreeTurn.Core.Tests;

static class T
{
    public static readonly string Hex64 = string.Concat(Enumerable.Repeat("ab12", 16));
    public static readonly string Hex32 = string.Concat(Enumerable.Repeat("cd34", 8));
    public const string GoldenMinimal = "freeturn://eyJ2IjoxLCJwcm92aWRlciI6InZrIiwicGVlciI6IjEuMi4zLjQ6NTYwMDAifQ";

    public static string B64Link(string json) =>
        "freeturn://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public const string PlainWG = """
        [Interface]
        PrivateKey = cHJpdmF0ZWtleXByaXZhdGVrZXlwcml2YXRla2V5MDA=
        Address = 10.13.13.2/32
        DNS = 1.1.1.1, 8.8.8.8
        MTU = 1420

        [Peer]
        PublicKey = cHVibGlja2V5cHVibGlja2V5cHVibGlja2V5cHViMDA=
        PresharedKey = cHNrcHNrcHNrcHNrcHNrcHNrcHNrcHNrcHNrcHNrMDA=
        AllowedIPs = 0.0.0.0/0, ::/0
        Endpoint = 1.2.3.4:51820
        PersistentKeepalive = 25
        """;

    public const string AmneziaWG = """
        [Interface]
        PrivateKey = cHJpdmF0ZWtleXByaXZhdGVrZXlwcml2YXRla2V5MDA=
        Address = 10.8.1.2/32
        DNS = 9.9.9.9
        Jc = 4
        Jmin = 40
        Jmax = 70
        S1 = 15
        S2 = 20
        S3 = 12
        S4 = 12
        H1 = 1
        H2 = 2
        H3 = 3
        H4 = 4
        HeaderProtectionKey = aGVhZGVycHJvdGVjdGlvbmtleWhlYWRlcnByb3RlYzA=
        RandomTrailers = true

        [Peer]
        PublicKey = cHVibGlja2V5cHVibGlja2V5cHVibGlja2V5cHViMDA=
        PresharedKey = cHNrcHNrcHNrcHNrcHNrcHNrcHNrcHNrcHNrcHNrMDA=
        AllowedIPs = 0.0.0.0/0
        """;

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ft-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    public static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>Корень репозитория - для общих эталонов с Go-тестами.</summary>
    public static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    /// <summary>Эталон: FT_WRITE_FIXTURES=1 перезаписывает, иначе сверяет.</summary>
    public static void Fixture(string relative, string content)
    {
        var path = Path.Combine(RepoRoot(), relative);
        if (Environment.GetEnvironmentVariable("FT_WRITE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        Assert.Equal(File.ReadAllText(path), content);
    }
}
