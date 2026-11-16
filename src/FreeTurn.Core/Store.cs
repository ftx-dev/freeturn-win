using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeTurn.Core;

/// <summary>Файл есть, но не читается как JSON нужной формы. Файл не трогаем.</summary>
public sealed class StoreCorruptException(string name) : Exception($"{name} is corrupt")
{
    public string FileName { get; } = name;
}

/// <summary>JSON-файлы приложения в одном каталоге профиля пользователя.</summary>
public sealed class Store(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeTurn");

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public T Load<T>(string name, T fallback) where T : class
    {
        var path = Path.Combine(Directory, name);
        if (!File.Exists(path)) return fallback;
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new StoreCorruptException(name);
            Normalize(value);
            return value;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or StoreCorruptException)
        {
            throw new StoreCorruptException(name);
        }
    }

    static void Normalize(object value)
    {
        switch (value)
        {
            case ServersFile f: f.Normalize(); break;
            case AppSettings s: s.Normalize(); break;
        }
    }

    public void Save<T>(T value, string name) => Write(JsonSerializer.SerializeToUtf8Bytes(value, Json), name);

    public string OwnClientId()
    {
        var path = Path.Combine(Directory, "client-id");
        if (File.Exists(path))
        {
            var saved = File.ReadAllText(path).Trim();
            if (saved.Length == 32 && saved.All(Uri.IsHexDigit)) return saved;
        }
        var id = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        Write(System.Text.Encoding.UTF8.GetBytes(id), "client-id");
        return id;
    }

    /// <summary>Запись через временный файл и замену: обрыв посреди записи не портит прежний файл.</summary>
    void Write(byte[] data, string name)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, name);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
