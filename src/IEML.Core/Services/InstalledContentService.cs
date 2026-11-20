using System.IO.Compression;
using IEML.Core.Contracts;
namespace IEML.Core.Services;

public record InstalledItem(string Path, string DisplayName, bool Enabled, long Size, ContentKind Kind)
{
    public string SizeText => Size >= 1_048_576 ? $"{Size / 1_048_576.0:0.#} MB" : $"{Math.Max(1, Size / 1024)} KB";
}

public record ImportResult(bool Ok, string Message);

/// 列出／停用（加 .disabled）／刪除／匯入（拖進來的檔案）實例資料夾裡的模組、光影、資源包。
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

    /// 把使用者拖進來的檔案複製到實例資料夾。只收 .jar（模組）或 .zip（資源包／光影）；先確認是有效的 zip，
    /// 資源包要有根目錄的 pack.mcmeta、光影要有根目錄的 shaders/（Minecraft 只認這種結構；多包一層資料夾的 zip 遊戲讀不到）。
    /// 不覆蓋同名檔案（自動加 (1)、(2)…）；先寫成 .part 再改名，中途失敗不會留下半個檔案。
    public static ImportResult Import(string instanceDir, ContentKind kind, string srcPath)
    {
        var name = Path.GetFileName(srcPath);
        try
        {
            if (kind == ContentKind.Modpack) return new(false, "整合包請用「搜尋與安裝」或匯入 .mrpack");
            if (Directory.Exists(srcPath)) return new(false, $"{name}：不支援資料夾，請拖 .zip／.jar 檔");
            if (!File.Exists(srcPath)) return new(false, $"{name}：找不到檔案");
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (kind == ContentKind.Mod ? ext != ".jar" : ext != ".zip")
                return new(false, $"{name}：{(kind == ContentKind.Mod ? "模組要是 .jar" : "資源包／光影要是 .zip")}");

            using (var z = ZipFile.OpenRead(srcPath))
            {
                bool Has(Func<string, bool> p) => z.Entries.Any(e => p(e.FullName.Replace('\\', '/')));
                if (kind == ContentKind.ResourcePack && !Has(n => n.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)))
                    return new(false, $"{name}：zip 根目錄沒有 pack.mcmeta，遊戲讀不到。常見原因是多包了一層資料夾，請解開後把裡面的內容重新壓縮。");
                if (kind == ContentKind.Shader && !Has(n => n.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)))
                    return new(false, $"{name}：zip 根目錄沒有 shaders 資料夾，不像光影包（多包一層資料夾也會讀不到）。");
            }

            var dir = Path.Combine(instanceDir, FolderFor(kind));
            Directory.CreateDirectory(dir);
            var stem = Path.GetFileNameWithoutExtension(name);
            var dest = Path.Combine(dir, name);
            for (int i = 1; File.Exists(dest) || File.Exists(dest + ".disabled"); i++) dest = Path.Combine(dir, $"{stem} ({i}){ext}");
            var part = dest + ".part";
            File.Copy(srcPath, part, true);
            File.Move(part, dest);
            return new(true, $"已加入：{Path.GetFileName(dest)}");
        }
        catch (InvalidDataException) { return new(false, $"{name}：不是有效的 zip／jar 檔（可能下載不完整）"); }
        catch (Exception ex) { return new(false, $"{name}：{ex.Message}"); }
    }

    public static void Delete(InstalledItem item) { if (File.Exists(item.Path)) File.Delete(item.Path); }
}
