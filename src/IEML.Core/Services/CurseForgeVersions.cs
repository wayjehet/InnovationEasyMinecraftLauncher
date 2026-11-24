using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// CurseForge 的「項目詳情頁」版本來源（列出所有檔案、安裝指定檔案）。需要 CurseForge API 金鑰。
public partial class CurseForgeService : IVersionSource
{
    const int RelationOptional = 2;
    static readonly string[] CfLoaderNames = { "Forge", "Fabric", "NeoForge", "Quilt", "LiteLoader", "Rift" };

    static ContentVersion ToVersion(JsonElement f)
    {
        var gv = f.TryGetProperty("gameVersions", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList() : new List<string>();
        var loaders = gv.Where(x => CfLoaderNames.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        var mcs = gv.Where(x => char.IsDigit(x[0])).ToList(); // 版本號以數字開頭（"1.20.1"、"26.1"、快照 "24w14a"）；其餘是載入器／Client／Server
        var deps = new List<VersionDep>();
        if (f.TryGetProperty("dependencies", out var da) && da.ValueKind == JsonValueKind.Array)
            foreach (var d in da.EnumerateArray())
            {
                var kind = Int(d, "relationType") switch { RelationRequired => "required", RelationOptional => "optional", _ => "other" };
                if (Int(d, "modId") is int id && kind != "other") deps.Add(new VersionDep(id.ToString(), kind));
            }
        DateTime.TryParse(Str(f, "fileDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date);
        var fileName = Str(f, "fileName") ?? "";
        return new ContentVersion(
            f.GetProperty("id").GetInt32().ToString(),
            Str(f, "displayName") ?? fileName,
            fileName,
            Int(f, "releaseType") switch { 1 => "release", 2 => "beta", 3 => "alpha", _ => "release" },
            mcs, loaders, date,
            f.TryGetProperty("downloadCount", out var dc) && dc.ValueKind == JsonValueKind.Number ? dc.GetInt64() : 0,
            null, deps);
    }

    static bool Unavailable(JsonElement f) => f.TryGetProperty("isAvailable", out var av) && av.ValueKind == JsonValueKind.False;

    public async Task<IReadOnlyList<ContentVersion>> ListVersionsAsync(ContentHit hit, InstanceConfig? target, bool matchTarget, CancellationToken ct = default)
    {
        var extra = "";
        var loaderTypes = new List<int?> { null };
        if (matchTarget && target is not null && hit.Kind != ContentKind.Modpack)
        {
            extra = "&gameVersion=" + Uri.EscapeDataString(target.McVersion);
            loaderTypes = new List<int?> { LoaderFilter(target.Loader, target.McVersion, hit.Kind) };
            if (target.Loader == LoaderType.Quilt && hit.Kind == ContentKind.Mod) loaderTypes.Add(LoaderFabric); // Quilt 可載入 Fabric 模組
        }
        var seen = new HashSet<string>();
        var list = new List<ContentVersion>();
        foreach (var lt in loaderTypes)
            for (int index = 0; index < 150; index += 50) // 最多三頁（150 個檔案）
            {
                var url = $"mods/{hit.ProjectId}/files?pageSize=50&index={index}{extra}" + (lt is int l ? $"&modLoaderType={l}" : "");
                using var doc = await Api(HttpMethod.Get, url, null, ct);
                int n = 0;
                foreach (var f in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    n++;
                    if (Unavailable(f)) continue;
                    var v = ToVersion(f);
                    if (seen.Add(v.Id)) list.Add(v);
                }
                if (n < 50) break;
            }
        return list.OrderByDescending(v => v.Date).ToList();
    }

    public async Task<string?> GetLongDescriptionAsync(ContentHit hit, CancellationToken ct = default)
    {
        using var doc = await Api(HttpMethod.Get, $"mods/{hit.ProjectId}/description", null, ct);
        return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String
            ? DescriptionText.Clean(d.GetString()) : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetProjectTitlesAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var nums = ids.Select(i => int.TryParse(i, out var n) ? n : (int?)null).Where(n => n.HasValue).Select(n => n!.Value).Distinct().ToArray();
        var map = new Dictionary<string, string>();
        if (nums.Length == 0) return map;
        using var doc = await Api(HttpMethod.Post, "mods", new { modIds = nums }, ct);
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
            map[m.GetProperty("id").GetInt32().ToString()] = Str(m, "name") ?? "";
        return map;
    }

    public async Task<IReadOnlyList<string>> InstallVersionAsync(InstanceConfig? target, ContentHit hit, ContentVersion version,
        bool installRequired, IReadOnlyCollection<string> optionalProjects, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? pinnedVersions = null)
    {
        var warnings = new List<string>();
        int projectId = int.Parse(hit.ProjectId), fileId = int.Parse(version.Id);

        if (hit.Kind == ContentKind.Modpack)
        {
            await InstallModpackAsync(hit.ProjectId, null, ct, fileId);
            return warnings;
        }

        var cfg = target ?? throw new InvalidOperationException("請先選一個實例");
        var dir = Path.GetFullPath(Path.Combine(_instances.GetInstanceDir(cfg.Id), FolderFor(hit.Kind)));
        Directory.CreateDirectory(dir);

        using var one = await Api(HttpMethod.Get, $"mods/{projectId}/files/{fileId}", null, ct);
        var f = one.RootElement.GetProperty("data");
        var installed = new List<string>();
        var seen = new HashSet<int> { projectId };

        if (installRequired && hit.Kind == ContentKind.Mod && f.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            foreach (var d in deps.EnumerateArray())
                if (Int(d, "relationType") == RelationRequired && Int(d, "modId") is int depId)
                    await InstallProjectAsync(depId, ContentKind.Mod, cfg, dir, seen, 1, installed, null, ct, pinnedVersions);

        foreach (var o in optionalProjects)
        {
            if (!int.TryParse(o, out var oid)) continue;
            try { await InstallProjectAsync(oid, ContentKind.Mod, cfg, dir, seen, 1, installed, null, ct, pinnedVersions); }
            catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"可選前置 {o} 沒有安裝：{ex.Message}"); }
        }

        var fileName = Path.GetFileName(Str(f, "fileName") ?? $"{fileId}.jar");
        var url = Str(f, "downloadUrl") ?? await GetDownloadUrl(projectId, fileId, ct)
            ?? throw new InvalidOperationException($"「{fileName}」作者不開放第三方啟動器下載，請到 CurseForge 網站手動下載後放進 {Path.GetFileName(dir)} 資料夾。");
        var dest = Path.GetFullPath(Path.Combine(dir, fileName));
        if (!dest.StartsWith(dir + Path.DirectorySeparatorChar)) throw new InvalidDataException("不安全的檔名：" + fileName);
        await DownloadVerifiedAsync(url, dest, HashOf(f), ct);

        try { await RemoveOtherFilesAsync(projectId, dir, fileName, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("新版本已裝好，但沒能清理同模組的舊版本檔案（請到資料夾確認）：" + ex.Message); }
        return warnings;
    }

    /// 同一個專案不能同時放兩個版本：取得該專案所有檔案的檔名，刪掉資料夾裡同名（含 .disabled）而不是剛裝的那個。
    /// 限制：作者改過檔名、或手動改名的舊檔找不到，需要自己清。
    async Task RemoveOtherFilesAsync(int projectId, string dir, string keepFileName, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < 150; index += 50)
        {
            using var doc = await Api(HttpMethod.Get, $"mods/{projectId}/files?pageSize=50&index={index}", null, ct);
            int n = 0;
            foreach (var f in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                n++;
                if (Str(f, "fileName") is { Length: > 0 } fn) names.Add(Path.GetFileName(fn));
            }
            if (n < 50) break;
        }
        foreach (var path in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(path);
            var real = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name;
            if (string.Equals(name, keepFileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (names.Contains(real)) File.Delete(path);
        }
    }
}
