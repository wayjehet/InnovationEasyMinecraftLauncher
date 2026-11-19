using System.Text.Json;
namespace IEML.Core.Services;
public static class JsonStore
{
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public static async Task<T> ReadAsync<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path)) return fallback();
        await using var s = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(s, Opt) ?? fallback();
    }
    public static async Task WriteAsync<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var s = File.Create(tmp)) await JsonSerializer.SerializeAsync(s, data, Opt);
        File.Move(tmp, path, overwrite: true);
    }
}
