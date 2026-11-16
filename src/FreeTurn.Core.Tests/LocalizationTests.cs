using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreeTurn.Core.Tests;

/// <summary>Ключ строки - русский текст; без перевода английский интерфейс покажет его как есть.</summary>
public class LocalizationTests
{
    static readonly string App = Path.Combine(T.RepoRoot(), "src", "FreeTurnX");

    static HashSet<string> UsedKeys()
    {
        var keys = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(App, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("\\obj\\")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\b[LF]\(""((?:[^""\\]|\\.)*)""")) keys.Add(m.Groups[1].Value.Replace("\\\"", "\""));
        }
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(App, "Views", "MainWindow.cs")), @"\(Section\.\w+, ""[^""]*"", ""([^""]+)""\)"))
            keys.Add(m.Groups[1].Value);
        return keys;
    }

    static Dictionary<string, string> English() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(App, "Resources", "Strings.en.json")))!;

    [Fact]
    public void EveryUsedStringHasEnglishTranslation() => Assert.Empty(UsedKeys().Except(English().Keys).Order());

    [Fact]
    public void NoStaleOrEmptyTranslations()
    {
        var en = English();
        Assert.Empty(en.Keys.Except(UsedKeys()).Order());
        Assert.Empty(en.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key));
    }

    // %@ и %d должны совпадать в ключе и переводе, иначе подстановка сломается.
    [Fact]
    public void FormatSpecifiersMatch()
    {
        foreach (var (k, v) in English())
            Assert.Equal(Regex.Matches(k, "%[@d]").Select(m => m.Value), Regex.Matches(v, "%[@d]").Select(m => m.Value));
    }
}
