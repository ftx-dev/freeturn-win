using System.Text.RegularExpressions;

namespace FreeTurn.Core;

public enum LogLevel { Debug, Info, Warn, Error }
public enum LogSource { Core, Engine, App }

public sealed record LogLine(DateTime Date, LogLevel Level, LogSource Source, string Text)
{
    public long Id { get; init; }
}

/// <summary>Прячет секреты в строках лога: ссылки на звонок и ключи - всегда, адреса - по флагу.</summary>
public static class Redactor
{
    static readonly Regex CallLink = new(@"https?://\S*/call/\S*", RegexOptions.Compiled);
    /// <summary>Ключ обфускации (64 hex) и Client ID (32 hex).</summary>
    static readonly Regex HexKey = new(@"\b[0-9a-fA-F]{32,}\b", RegexOptions.Compiled);
    static readonly Regex Ipv6Bracketed = new(@"\[[0-9a-fA-F:]{3,39}\]:\d{1,5}", RegexOptions.Compiled);
    /// <summary>С «::» или полные 8 групп. Три группы (время 12:34:56) сюда не попадают.</summary>
    static readonly Regex Ipv6 = new(@"\b[0-9a-fA-F]{1,4}(?::[0-9a-fA-F]{1,4})*::(?:[0-9a-fA-F]{1,4}(?::[0-9a-fA-F]{1,4})*)?|\b[0-9a-fA-F]{1,4}(?::[0-9a-fA-F]{1,4}){7}\b", RegexOptions.Compiled);
    static readonly Regex Ipv4Port = new(@"\b(?:\d{1,3}\.){3}\d{1,3}:\d{1,5}\b", RegexOptions.Compiled);
    static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);

    public static string Redact(string text, IEnumerable<string> secrets, bool maskAddresses = true)
    {
        var output = text;
        // Короткие «секреты» (пустые поля) заменили бы полстроки.
        foreach (var secret in secrets.Where(s => s.Length >= 4)) output = output.Replace(secret, "•••");
        output = CallLink.Replace(output, "•••");
        output = HexKey.Replace(output, "•••");
        if (!maskAddresses) return output;
        output = Ipv6Bracketed.Replace(output, "[x:x]:•");
        output = Ipv6.Replace(output, "x:x");
        output = Ipv4Port.Replace(output, "x.x.x.x:•");
        return Ipv4.Replace(output, "x.x.x.x");
    }
}

/// <summary>Лог сессии на диске: экранный лог чистится при подключении, а разбирать сбой приходится
/// после перезапуска. Два файла по maxBytes: текущий и предыдущий.</summary>
public sealed class LogFile(string directory, long maxBytes = 1_000_000)
{
    readonly object gate = new();
    string Current => Path.Combine(directory, "session.log");
    string Previous => Path.Combine(directory, "session.1.log");

    public void Append(string line)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var data = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine);
                var size = File.Exists(Current) ? new FileInfo(Current).Length : 0;
                if (size > 0 && size + data.Length > maxBytes) File.Move(Current, Previous, overwrite: true);
                using var stream = new FileStream(Current, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(data);
            }
            catch (IOException) { } // лог не должен ронять приложение
        }
    }

    public void Export(string target)
    {
        lock (gate)
        {
            using var output = File.Create(target);
            foreach (var path in new[] { Previous, Current })
                if (File.Exists(path)) output.Write(File.ReadAllBytes(path));
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            File.Delete(Current);
            File.Delete(Previous);
        }
    }
}
