using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

public partial class ModrinthService : IModrinthService
{
    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        // Modrinth 要求可辨識的 User-Agent（含聯絡方式）：這裡用專案網址。
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1-beta (https://github.com/wayjehet/InnovationEasyMinecraftLauncher)");
        return c;
    }

    readonly IInstanceService _instances;
    public ModrinthService(IInstanceService instances) => _instances = instances;

    static string TypeName(ContentKind k) => k switch
    { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.Shader => "shader", _ => "modpack" };
    static string FolderFor(ContentKind k) => k switch
    { ContentKind.Mod => "mods", ContentKind.ResourcePack => "resourcepacks", ContentKind.Shader => "shaderpacks", _ => "." };
    static string LoaderName(LoaderType l) => l.ToString().ToLowerInvariant();

    static async Task<JsonDocument> GetJson(string url, CancellationToken ct)
    {
        using var res = await Http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    public async Task<IReadOnlyList<ContentHit>> SearchAsync(string query, ContentKind kind, string mcVersion, LoaderType? loader)
    {
        var ld = loader is not null && loader != LoaderType.Vanilla && (kind == ContentKind.Mod || kind == ContentKind.Modpack)
            ? LoaderName(loader.Value) : null;
        return (await SearchAsync(new ModrinthQuery(query, kind, mcVersion, ld))).Hits;
    }

    public async Task<ContentPage> SearchAsync(ModrinthQuery q, CancellationToken ct = default)
    {
        var facets = new List<string[]> { new[] { $"project_type:{TypeName(q.Kind)}" } };
        if (!string.IsNullOrWhiteSpace(q.McVersion)) facets.Add(new[] { $"versions:{q.McVersion.Trim()}" });
        if (!string.IsNullOrEmpty(q.Loader)) facets.Add(new[] { $"categories:{q.Loader}" });
        if (!string.IsNullOrEmpty(q.Category)) facets.Add(new[] { $"categories:{q.Category}" });
        if (q.Kind is ContentKind.Mod or ContentKind.Modpack)
        {
            if (q.Environment == "client") facets.Add(new[] { "client_side:required", "client_side:optional" });
            else if (q.Environment == "server") facets.Add(new[] { "server_side:required", "server_side:optional" });
        }
        var sort = q.Sort is "downloads" or "follows" or "newest" or "updated" ? q.Sort : "relevance";
        var url = $"search?query={Uri.EscapeDataString(q.Text ?? "")}&limit={q.Limit}&offset={q.Offset}&index={sort}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        using var doc = await GetJson(url, ct);
        var list = new List<ContentHit>();
        foreach (var h in doc.RootElement.GetProperty("hits").EnumerateArray())
            list.Add(new ContentHit(h.GetProperty("project_id").GetString()!, h.GetProperty("title").GetString() ?? "",
                h.GetProperty("description").GetString() ?? "",
                h.TryGetProperty("icon_url", out var ic) && ic.ValueKind == JsonValueKind.String ? ic.GetString() : null, q.Kind,
                h.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Number ? dl.GetInt64() : 0,
                h.TryGetProperty("author", out var au) && au.ValueKind == JsonValueKind.String ? au.GetString() : null));
        var total = doc.RootElement.TryGetProperty("total_hits", out var th) ? th.GetInt32() : list.Count;
        return new ContentPage(list, total);
    }

    // ---------- 篩選選項（分類／載入器）----------
    static readonly Dictionary<string, string> CatZh = new()
    {
        ["adventure"] = "冒險", ["cursed"] = "詭異", ["decoration"] = "裝飾", ["economy"] = "經濟", ["equipment"] = "裝備",
        ["food"] = "食物", ["game-mechanics"] = "遊戲機制", ["library"] = "前置庫", ["magic"] = "魔法", ["management"] = "管理",
        ["minigame"] = "小遊戲", ["mobs"] = "生物", ["optimization"] = "優化", ["social"] = "社交", ["storage"] = "儲存",
        ["technology"] = "科技", ["transportation"] = "運輸", ["utility"] = "實用", ["worldgen"] = "世界生成",
        ["challenging"] = "高難度", ["combat"] = "戰鬥", ["kitchen-sink"] = "大雜燴", ["lightweight"] = "輕量",
        ["multiplayer"] = "多人", ["quests"] = "任務",
        ["cartoon"] = "卡通", ["fantasy"] = "奇幻", ["realistic"] = "寫實", ["semi-realistic"] = "半寫實", ["vanilla-like"] = "原版風",
        ["atmosphere"] = "氛圍", ["bloom"] = "泛光", ["colored-lighting"] = "彩色光源", ["foliage"] = "植被", ["path-tracing"] = "路徑追蹤",
        ["pbr"] = "PBR", ["reflections"] = "反射", ["shadows"] = "陰影", ["potato"] = "低配", ["low"] = "低效能需求",
        ["medium"] = "中效能需求", ["high"] = "高效能需求", ["screenshot"] = "截圖用",
        ["blocks"] = "方塊", ["core-shaders"] = "核心著色器", ["entities"] = "實體", ["environment"] = "環境", ["gui"] = "介面",
        ["items"] = "物品", ["locale"] = "語言", ["modded"] = "模組適配", ["audio"] = "音效", ["fonts"] = "字型", ["models"] = "模型",
        ["simplistic"] = "簡約", ["themed"] = "主題", ["tweaks"] = "微調", ["vanilla"] = "原版",
        ["fabric"] = "Fabric", ["forge"] = "Forge", ["neoforge"] = "NeoForge", ["quilt"] = "Quilt",
        ["iris"] = "Iris", ["optifine"] = "OptiFine", ["canvas"] = "Canvas",
    };
    static string Zh(string id) => CatZh.TryGetValue(id, out var z) ? z : id.Replace('-', ' ');

    static JsonDocument? _catCache, _loaderCache;

    public async Task<IReadOnlyList<FilterOption>> GetCategoriesAsync(ContentKind kind, CancellationToken ct = default)
    {
        try
        {
            _catCache ??= await GetJson("tag/category", ct);
            var type = TypeName(kind);
            return _catCache.RootElement.EnumerateArray()
                .Where(c => c.GetProperty("project_type").GetString() == type)
                // header 有 "categories"（主分類）、"resolutions"、"features"、"performance impact" 等；全部都能用 categories: 篩選
                .Select(c => c.GetProperty("name").GetString()!)
                .Distinct().Select(n => new FilterOption(n, Zh(n))).OrderBy(o => o.Name).ToList();
        }
        catch { return Array.Empty<FilterOption>(); } // 取不到就不顯示分類，不影響搜尋
    }

    public async Task<IReadOnlyList<FilterOption>> GetLoadersAsync(ContentKind kind, CancellationToken ct = default)
    {
        string[] allow = kind switch
        {
            ContentKind.Mod or ContentKind.Modpack => new[] { "fabric", "forge", "neoforge", "quilt" },
            ContentKind.Shader => new[] { "iris", "optifine", "canvas", "vanilla" },
            _ => Array.Empty<string>()
        };
        if (allow.Length == 0) return Array.Empty<FilterOption>();
        try
        {
            _loaderCache ??= await GetJson("tag/loader", ct);
            var type = TypeName(kind);
            var ok = _loaderCache.RootElement.EnumerateArray()
                .Where(l => l.GetProperty("supported_project_types").EnumerateArray().Any(t => t.GetString() == type))
                .Select(l => l.GetProperty("name").GetString()!).ToHashSet();
            return allow.Where(ok.Contains).Select(n => new FilterOption(n, Zh(n))).ToList();
        }
        catch { return allow.Select(n => new FilterOption(n, Zh(n))).ToList(); }
    }

    public async Task InstallAsync(Guid instanceId, string projectId, ContentKind kind, CancellationToken ct = default)
    {
        if (kind == ContentKind.Modpack) { await InstallModpackAsync(projectId, ct); return; }
        var cfg = await _instances.GetAsync(instanceId) ?? throw new InvalidOperationException("找不到實例");
        var loader = kind == ContentKind.Mod && cfg.Loader != LoaderType.Vanilla ? LoaderName(cfg.Loader) : null;
        var dir = Path.Combine(_instances.GetInstanceDir(instanceId), FolderFor(kind));
        Directory.CreateDirectory(dir);
        await InstallProject(projectId, cfg.McVersion, loader, dir, new HashSet<string>(), 0, ct);
    }

    async Task InstallProject(string projectId, string mc, string? loader, string dir, HashSet<string> seen, int depth, CancellationToken ct)
    {
        if (!seen.Add(projectId) || depth > 5) return;
        var q = $"project/{projectId}/version?game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mc }))}";
        if (loader != null) q += $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}";
        using var doc = await GetJson(q, ct);
        if (doc.RootElement.GetArrayLength() == 0) throw new InvalidOperationException($"找不到適用於 {mc} 的版本（{projectId}）");
        var ver = PickVersion(doc.RootElement);

        foreach (var dep in ver.GetProperty("dependencies").EnumerateArray())
            if (dep.GetProperty("dependency_type").GetString() == "required" &&
                dep.TryGetProperty("project_id", out var pid) && pid.ValueKind == JsonValueKind.String)
                await InstallProject(pid.GetString()!, mc, loader, dir, seen, depth + 1, ct);

        var files = ver.GetProperty("files");
        var file = files.EnumerateArray().FirstOrDefault(f => f.GetProperty("primary").GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = files[0];
        var name = Path.GetFileName(file.GetProperty("filename").GetString()!);
        var sha1 = file.GetProperty("hashes").TryGetProperty("sha1", out var h) ? h.GetString() : null;
        await DownloadAsync(file.GetProperty("url").GetString()!, Path.Combine(dir, name), sha1, ct);
    }

    /// 優先挑正式版（release），沒有才退回最新的 beta/alpha。
    static JsonElement PickVersion(JsonElement versions)
    {
        foreach (var v in versions.EnumerateArray())
            if (v.TryGetProperty("version_type", out var t) && t.GetString() == "release") return v;
        return versions[0];
    }

    /// 強制驗證的下載：https 網域白名單（含轉址後）＋ 必須有 SHA-1 ＋ 不符就刪檔並失敗。先寫 .part，驗證通過才改名。
    static async Task DownloadAsync(string url, string dest, string? sha1, CancellationToken ct)
    {
        var name = Path.GetFileName(dest);
        if (string.IsNullOrEmpty(sha1)) throw new InvalidDataException($"{name} 沒有檢查碼可驗證，已拒絕下載。");
        var uri = new Uri(url); DownloadPolicy.Require(uri);
        sha1 = sha1.ToLowerInvariant();
        if (File.Exists(dest) && Sha1Of(dest) == sha1) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var part = dest + ".part";
        try
        {
            using var res = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            DownloadPolicy.Require(res.RequestMessage?.RequestUri ?? uri);
            await using (var fs = File.Create(part)) await res.Content.CopyToAsync(fs, ct);
            if (Sha1Of(part) != sha1) throw new InvalidDataException($"檢查碼不符，已刪除：{name}");
            File.Move(part, dest, true);
        }
        catch { try { if (File.Exists(part)) File.Delete(part); } catch { } throw; }
    }

    static string Sha1Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }

    // ---------- 已安裝內容更新 ----------
    static readonly string[] ContentExt = { ".jar", ".zip" };

    public async Task<IReadOnlyList<UpdateInfo>> CheckUpdatesAsync(Guid instanceId, ContentKind kind, CancellationToken ct = default)
    {
        if (kind == ContentKind.Modpack) return Array.Empty<UpdateInfo>();
        var cfg = await _instances.GetAsync(instanceId) ?? throw new InvalidOperationException("找不到實例");
        var dir = Path.Combine(_instances.GetInstanceDir(instanceId), FolderFor(kind));
        if (!Directory.Exists(dir)) return Array.Empty<UpdateInfo>();

        // 路徑 <- sha1（含已停用的 .disabled）
        var byHash = new Dictionary<string, string>();
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var name = f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? f[..^9] : f;
            if (!ContentExt.Contains(Path.GetExtension(name).ToLowerInvariant())) continue;
            await Task.Run(() => byHash[Sha1Of(f)] = f, ct);
        }
        if (byHash.Count == 0) return Array.Empty<UpdateInfo>();

        var body = new Dictionary<string, object?>
        {
            ["hashes"] = byHash.Keys.ToArray(), ["algorithm"] = "sha1",
            ["game_versions"] = new[] { cfg.McVersion },
            ["loaders"] = kind == ContentKind.Mod && cfg.Loader != LoaderType.Vanilla ? new[] { LoaderName(cfg.Loader) } : null,
        };
        if (body["loaders"] is null) body.Remove("loaders");
        using var res = await Http.PostAsync("version_files/update",
            new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"), ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

        var list = new List<UpdateInfo>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!byHash.TryGetValue(prop.Name, out var oldPath)) continue;
            var ver = prop.Value;
            var files = ver.GetProperty("files");
            var f = files.EnumerateArray().FirstOrDefault(x => x.GetProperty("primary").GetBoolean());
            if (f.ValueKind == JsonValueKind.Undefined) f = files[0];
            var sha1 = f.GetProperty("hashes").TryGetProperty("sha1", out var h) ? h.GetString() : null;
            if (sha1 == null || sha1 == prop.Name) continue; // 已是最新
            var vt = ver.TryGetProperty("version_type", out var vtt) ? vtt.GetString() : "release";
            if (vt != "release") continue;                    // 不主動推 beta/alpha
            list.Add(new UpdateInfo(oldPath, Path.GetFileName(f.GetProperty("filename").GetString()!),
                f.GetProperty("url").GetString()!, sha1, ver.GetProperty("version_number").GetString() ?? ""));
        }
        return list;
    }

    public async Task ApplyUpdateAsync(UpdateInfo u, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(u.OldPath)!;
        var wasDisabled = u.OldPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var dest = Path.Combine(dir, Path.GetFileName(u.NewFileName));
        await DownloadAsync(u.Url, dest, u.Sha1, ct);          // 先下載並驗證，成功才刪舊檔
        if (!string.Equals(dest, u.OldPath, StringComparison.OrdinalIgnoreCase) && File.Exists(u.OldPath)) File.Delete(u.OldPath);
        if (wasDisabled) File.Move(dest, dest + ".disabled", true);
    }

    public async Task<Guid> InstallModpackAsync(string projectId, CancellationToken ct = default)
    {
        string title; string packUrl;
        using (var proj = await GetJson($"project/{projectId}", ct)) title = proj.RootElement.GetProperty("title").GetString() ?? "Modpack";
        string? packSha1 = null;
        using (var vers = await GetJson($"project/{projectId}/version", ct))
        {
            if (vers.RootElement.GetArrayLength() == 0) throw new InvalidOperationException("此整合包沒有可下載的版本");
            var files = PickVersion(vers.RootElement).GetProperty("files");
            var f = files.EnumerateArray().FirstOrDefault(x => x.GetProperty("primary").GetBoolean());
            if (f.ValueKind == JsonValueKind.Undefined) f = files[0];
            packUrl = f.GetProperty("url").GetString()!;
            packSha1 = f.GetProperty("hashes").TryGetProperty("sha1", out var ph) ? ph.GetString() : null;
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"ieml_{Guid.NewGuid():N}.mrpack");
        try
        {
            await DownloadAsync(packUrl, tmp, packSha1, ct);
            return await ImportMrpackAsync(tmp, title, ct);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    /// 從本機 .mrpack 建立新實例（線上安裝與「匯入本機整合包」共用）。失敗會刪掉半成品實例。
    public async Task<Guid> ImportMrpackAsync(string mrpackPath, string fallbackName, CancellationToken ct = default)
    {
        Guid? createdId = null;
        try
        {
            using var zip = ZipFile.OpenRead(mrpackPath);
            var idxEntry = zip.GetEntry("modrinth.index.json") ?? throw new InvalidDataException("不是有效的 .mrpack");
            using var idx = JsonDocument.Parse(idxEntry.Open());
            var root = idx.RootElement;

            var deps = root.GetProperty("dependencies");
            string mc = deps.GetProperty("minecraft").GetString()!;
            var loader = LoaderType.Vanilla; string? loaderVer = null;
            foreach (var (key, lt) in new[] { ("fabric-loader", LoaderType.Fabric), ("quilt-loader", LoaderType.Quilt),
                                              ("neoforge", LoaderType.NeoForge), ("forge", LoaderType.Forge) })
                if (deps.TryGetProperty(key, out var lv)) { loader = lt; loaderVer = lv.GetString(); break; }

            var packName = root.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nm.GetString()) ? nm.GetString()! : fallbackName;
            var cfg = await _instances.CreateAsync(new InstanceConfig { Name = packName, McVersion = mc, Loader = loader, LoaderVersion = loaderVer });
            createdId = cfg.Id;
            var dir = Path.GetFullPath(_instances.GetInstanceDir(cfg.Id));

            string Safe(string rel)
            {
                var full = Path.GetFullPath(Path.Combine(dir, rel));
                if (!full.StartsWith(dir + Path.DirectorySeparatorChar)) throw new InvalidDataException("整合包含有不安全的路徑：" + rel);
                return full;
            }

            // overrides first, then downloaded files
            var blocked = new List<string>(); var unlistedJars = new List<string>();
            foreach (var e in zip.Entries)
            {
                foreach (var prefix in new[] { "overrides/", "client-overrides/" })
                    if (e.FullName.StartsWith(prefix) && !e.FullName.EndsWith('/'))
                    {
                        var rel = e.FullName[prefix.Length..];
                        if (DownloadPolicy.IsBlockedFile(rel)) { blocked.Add(rel); continue; }          // 可執行檔／腳本：不安裝
                        if (rel.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) unlistedJars.Add(rel);   // 自帶 jar：只警告
                        var dest = Safe(rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        e.ExtractToFile(dest, true);
                    }
            }
            DownloadPolicy.ReportPack(packName, blocked, unlistedJars);

            var gate = new SemaphoreSlim(6);
            var tasks = new List<Task>();
            foreach (var f in root.GetProperty("files").EnumerateArray())
            {
                if (f.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var cl) && cl.GetString() == "unsupported") continue;
                var dest = Safe(f.GetProperty("path").GetString()!);
                var url = f.GetProperty("downloads")[0].GetString()!;
                DownloadPolicy.RequireMrpack(new Uri(url));          // .mrpack 規格只允許 Modrinth／GitHub／GitLab
                var sha1 = f.GetProperty("hashes").TryGetProperty("sha1", out var h) ? h.GetString() : null;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct);
                    try { await DownloadAsync(url, dest, sha1, ct); } finally { gate.Release(); }
                }, ct));
            }
            await Task.WhenAll(tasks);
            createdId = null; // 成功，不要清掉
            return cfg.Id;
        }
        catch
        {
            // 失敗或取消：不要留下半成品實例
            if (createdId is Guid id) try { await _instances.DeleteAsync(id); } catch { }
            throw;
        }
    }
}
