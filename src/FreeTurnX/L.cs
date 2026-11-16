using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;

namespace FreeTurnX;

/// <summary>Ключ строки - русский текст (как в клиенте для macOS); английский перевод - Strings.en.json.</summary>
public static class L10n
{
    /// <summary>"system" | "ru" | "en" - из настроек; меняется после перезапуска.</summary>
    public static string Language { get; set; } = "system";

    public static bool IsEnglish => Language switch
    {
        "en" => true,
        "ru" => false,
        _ => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase),
    };

    static readonly Lazy<Dictionary<string, string>> English = new(() =>
    {
        using var stream = typeof(L10n).Assembly.GetManifestResourceStream("Strings.en.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    });

    public static string Text(string key) => IsEnglish && English.Value.TryGetValue(key, out var value) ? value : key;
}

/// <summary>{l:T 'Текст'} в XAML.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L10n.Text(key);
}

public static class Strings
{
    public static string L(string key) => L10n.Text(key);
    public static string F(string key, params object[] args) => string.Format(L10n.Text(key).Replace("%@", "{0}").Replace("%d", "{0}"), args);
}
