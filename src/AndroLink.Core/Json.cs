using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroLink.Core;

/// <summary>JSON helpers used for every persisted AndroLink document.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path) where T : new()
        => TryLoad<T>(path) ?? new T();

    public static T? TryLoad<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (Exception ex)
        {
            Log.Warn("json", $"could not read {path}: {ex.Message}");
            return default;
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
