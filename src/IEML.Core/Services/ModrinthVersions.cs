using System.Globalization;
using System.Text.Json;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// 前置：Kind = required / optional / incompatible / embedded（Modrinth 的 dependency_type）。
public record VersionDep(string? ProjectId, string Kind);

/// Modrinth 專案的一個版本（供「項目詳情頁」列出與選擇）。
public record ContentVersion(string Id, string Number, string Name, string Type,
    IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders, DateTime Date, long Downloads,
    string? Changelog, IReadOnlyList<VersionDep> Deps)
{
    public string TypeText => Type switch { "release" => "正式版", "beta" => "測試版", "alpha" => "早期版", _ => Type };
    public string McText => GameVersions.Count == 0 ? "" :
        GameVersions.Count <= 3 ? string.Join("、", GameVersions) : $"{GameVersions[0]}…{GameVersions[^1]}（共 {GameVersions.Count} 個版本）";
    public string LoadersText => string.Join("／", Loaders);
    public string Sub => string.Join(" · ", new[] { TypeText, McText, LoadersText, Date.ToString("yyyy-MM-dd"), $"{Downloads:N0} 下載" }
        .Where(x => !string.IsNullOrEmpty(x)));
}

// 新方法放在這個 partial 檔，不動 IModrinthService / Contracts.cs。
public partial class ModrinthService : IVersionSource
{
    public Task<IReadOnlyList<ContentVersion>> ListVersionsAsync(ContentHit hit, InstanceConfig? target, bool matchTarget, CancellationToken ct = default)
    {
        string? mc = null, loader = null;
        if (matchTarget && target is not null && hit.Kind != ContentKind.Modpack)
        {
            mc = target.McVersion;
            if (hit.Kind == ContentKind.Mod && target.Loader != LoaderType.Vanilla) loader = LoaderName(target.Loader);
        }
        return GetVersionsAsync(hit.ProjectId, mc, loader, ct);
    }

    public Task<IReadOnlyList<string>> InstallVersionAsync(InstanceConfig? target, ContentHit hit, ContentVersion version,
        bool installRequired, IReadOnlyCollection<string> optionalProjects, CancellationToken ct = default) =>
        InstallVersionAsync(target?.Id ?? Guid.Empty, hit.Kind, hit.ProjectId, version.Id, installRequired, optionalProjects, ct);

    static IReadOnlyList<string> StrList(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
            : new List<string>();

    static JsonElement PrimaryFileOf(JsonElement version)
    {
        var files = version.GetProperty("files");
        if (files.GetArrayLength() == 0) throw new InvalidOperationException("此版本沒有可下載的檔案");
        var f = files.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("primary", out var p) && p.GetBoolean());
        return f.ValueKind == JsonValueKind.Undefined ? files[0] : f;
    }

    /// 列出專案的版本（新→舊）。mcVersion／loader 為 null 就不篩選。
    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(string projectId, string? mcVersion, string? loader, CancellationToken ct = default)
    {
        var q = $"project/{Uri.EscapeDataString(projectId)}/version";
        var ps = new List<string>();
        if (!string.IsNullOrWhiteSpace(mcVersion)) ps.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion.Trim() })));
        if (!string.IsNullOrWhiteSpace(loader)) ps.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.Trim() })));
        if (ps.Count > 0) q += "?" + string.Join("&", ps);
        using var doc = await GetJson(q, ct);
        var list = new List<ContentVersion>();
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            DateTime.TryParse(v.TryGetProperty("date_published", out var dp) ? dp.GetString() : null,
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date);
            var deps = new List<VersionDep>();
            if (v.TryGetProperty("dependencies", out var da) && da.ValueKind == JsonValueKind.Array)
                foreach (var d in da.EnumerateArray())
                    deps.Add(new VersionDep(
                        d.TryGetProperty("project_id", out var pid) && pid.ValueKind == JsonValueKind.String ? pid.GetString() : null,
                        d.TryGetProperty("dependency_type", out var dt) ? dt.GetString() ?? "" : ""));
            list.Add(new ContentVersion(
                v.GetProperty("id").GetString()!,
                v.TryGetProperty("version_number", out var vn) ? vn.GetString() ?? "" : "",
                v.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                v.TryGetProperty("version_type", out var vt) ? vt.GetString() ?? "release" : "release",
                StrList(v, "game_versions"), StrList(v, "loaders"), date,
                v.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Number ? dl.GetInt64() : 0,
                v.TryGetProperty("changelog", out var cl) && cl.ValueKind == JsonValueKind.String ? cl.GetString() : null,
                deps));
        }
        return list;
    }

    /// 專案 id → 名稱（顯示前置模組用）。
    public async Task<IReadOnlyDictionary<string, string>> GetProjectTitlesAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var arr = ids.Distinct().ToArray();
        var map = new Dictionary<string, string>();
        if (arr.Length == 0) return map;
        using var doc = await GetJson("projects?ids=" + Uri.EscapeDataString(JsonSerializer.Serialize(arr)), ct);
        foreach (var p in doc.RootElement.EnumerateArray())
            map[p.GetProperty("id").GetString()!] = p.GetProperty("title").GetString() ?? "";
        return map;
    }

    /// 安裝指定版本。回傳警告清單（例如某個可選前置裝不起來）；必要前置或本體失敗會直接丟例外。
    /// 整合包：下載該版本的 .mrpack 並建立新實例（instanceId 不使用）。
    /// 其他：先裝前置（必要的看 installRequired，可選的看使用者勾選），再下載本體，最後移除同專案的其他舊版本檔案。
    public async Task<IReadOnlyList<string>> InstallVersionAsync(Guid instanceId, ContentKind kind, string projectId, string versionId,
        bool installRequired, IReadOnlyCollection<string>? optionalProjects, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        using var vdoc = await GetJson($"version/{Uri.EscapeDataString(versionId)}", ct);
        var ver = vdoc.RootElement;
        var file = PrimaryFileOf(ver);
        var fname = Path.GetFileName(file.GetProperty("filename").GetString()!);
        var url = file.GetProperty("url").GetString()!;
        var sha1 = file.GetProperty("hashes").TryGetProperty("sha1", out var h) ? h.GetString() : null;

        if (kind == ContentKind.Modpack)
        {
            string title;
            using (var proj = await GetJson($"project/{Uri.EscapeDataString(projectId)}", ct))
                title = proj.RootElement.GetProperty("title").GetString() ?? "Modpack";
            var tmp = Path.Combine(Path.GetTempPath(), $"ieml_{Guid.NewGuid():N}.mrpack");
            try { await DownloadAsync(url, tmp, sha1, ct); await ImportMrpackAsync(tmp, title, ct); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            return warnings;
        }

        var cfg = await _instances.GetAsync(instanceId) ?? throw new InvalidOperationException("找不到實例");
        var loader = kind == ContentKind.Mod && cfg.Loader != LoaderType.Vanilla ? LoaderName(cfg.Loader) : null;
        var dir = Path.Combine(_instances.GetInstanceDir(instanceId), FolderFor(kind));
        Directory.CreateDirectory(dir);
        var seen = new HashSet<string> { projectId };

        if (installRequired && ver.TryGetProperty("dependencies", out var deps))
            foreach (var d in deps.EnumerateArray())
                if (d.GetProperty("dependency_type").GetString() == "required" &&
                    d.TryGetProperty("project_id", out var pid) && pid.ValueKind == JsonValueKind.String)
                    await InstallProject(pid.GetString()!, cfg.McVersion, loader, dir, seen, 1, ct);

        foreach (var opt in optionalProjects ?? Array.Empty<string>())
        {
            try { await InstallProject(opt, cfg.McVersion, loader, dir, seen, 1, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add($"可選前置 {opt} 沒有安裝：{ex.Message}"); }
        }

        var dest = Path.Combine(dir, fname);
        await DownloadAsync(url, dest, sha1, ct);
        try { await RemoveOtherVersionsAsync(dir, projectId, dest, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("新版本已裝好，但沒能清理同模組的舊版本檔案（請到資料夾確認）：" + ex.Message); }
        return warnings;
    }

    /// 同一個專案不能同時放兩個版本：用 sha1 向 Modrinth 查資料夾裡每個檔案屬於哪個專案，刪掉同專案、不是 keepPath 的檔案。
    async Task RemoveOtherVersionsAsync(string dir, string projectId, string keepPath, CancellationToken ct)
    {
        var byHash = new Dictionary<string, string>();
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (string.Equals(f, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
            var real = f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? f[..^9] : f;
            if (!ContentExt.Contains(Path.GetExtension(real).ToLowerInvariant())) continue;
            await Task.Run(() => byHash[Sha1Of(f)] = f, ct);
        }
        if (byHash.Count == 0) return;
        var body = JsonSerializer.Serialize(new { hashes = byHash.Keys.ToArray(), algorithm = "sha1" });
        using var res = await Http.PostAsync("version_files", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        foreach (var prop in doc.RootElement.EnumerateObject())
            if (byHash.TryGetValue(prop.Name, out var path) &&
                prop.Value.TryGetProperty("project_id", out var pid) && pid.GetString() == projectId && File.Exists(path))
                File.Delete(path);
    }
}
