using IEML.Core.Contracts;
namespace IEML.Core.Services;

public record InstalledItem(string Path, string DisplayName, bool Enabled, long Size, ContentKind Kind)
{
    public string SizeText => Size >= 1_048_576 ? $"{Size / 1_048_576.0:0.#} MB" : $"{Math.Max(1, Size / 1024)} KB";
}

/// 列出／停用（加 .disabled）／刪除實例資料夾裡的模組、光影、資源包。
public static class InstalledContentService
{
    public static string FolderFor(ContentKind k) => k switch
    { ContentKind.Mod => "mods", ContentKind.Shader => "shaderpacks", ContentKind.ResourcePack => "resourcepacks", _ => "." };

    public static IReadOnlyList<InstalledItem> List(string instanceDir, ContentKind kind)
    {
        var dir = Path.Combine(instanceDir, FolderFor(kind));
        if (kind == ContentKind.Modpack || !Directory.Exists(dir)) return Array.Empty<InstalledItem>();
        var items = new List<InstalledItem>();
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var disabled = f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var name = Path.GetFileName(disabled ? f[..^9] : f);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext != ".jar" && ext != ".zip") continue;
            items.Add(new InstalledItem(f, name, !disabled, new FileInfo(f).Length, kind));
        }
        return items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void SetEnabled(InstalledItem item, bool enabled)
    {
        var isDisabled = item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        if (enabled == !isDisabled) return;
        File.Move(item.Path, enabled ? item.Path[..^9] : item.Path + ".disabled", true);
    }

    public static void Delete(InstalledItem item) { if (File.Exists(item.Path)) File.Delete(item.Path); }
}
