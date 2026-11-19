using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// CurseForge API 金鑰。優先順序：
///   1. 使用者自己填的（DPAPI 加密存在 %AppData%\InnovationEasyLauncher\curseforge.json）
///   2. 打包時內建的（BuiltInSecrets.CurseForgeKey，官方發布版才有；原始碼裡是空的）
/// 都沒有時，CurseForge 功能提示使用者申請；Modrinth 與本機匯入 .mrpack 不需要金鑰。
public static class CurseForgeSettings
{
    static string FilePath => Path.Combine(AppPaths.Root, "curseforge.json");
    class Store { public string? ProtectedKey { get; set; } }

    public static bool HasUserKey => !string.IsNullOrEmpty(GetUserKey());
    public static bool HasBuiltInKey => !string.IsNullOrWhiteSpace(BuiltInSecrets.CurseForgeKey);
    public static bool HasKey => HasUserKey || HasBuiltInKey;

    /// 給介面顯示：目前用的是哪一種金鑰。
    public static string SourceText =>
        HasUserKey ? "使用你自己的金鑰"
        : HasBuiltInKey ? "使用內建金鑰（也可以填自己的覆蓋）"
        : "尚未設定金鑰：到 console.curseforge.com 免費申請後填入，或改用 Modrinth";

    /// 實際用來呼叫 API 的金鑰（使用者的優先）。
    public static string? GetKey() => GetUserKey() ?? (HasBuiltInKey ? BuiltInSecrets.CurseForgeKey.Trim() : null);

    static string? GetUserKey()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var s = JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath));
            if (string.IsNullOrEmpty(s?.ProtectedKey)) return null;
            var k = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.ProtectedKey), null, DataProtectionScope.CurrentUser));
            return string.IsNullOrWhiteSpace(k) ? null : k;
        }
        catch { return null; }
    }

    public static void SetKey(string key)
    {
        key = key.Trim();
        var s = new Store
        {
            ProtectedKey = key.Length == 0 ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser))
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
    }
}

public record CfInstallResult(Guid InstanceId, string PackName, int Installed, IReadOnlyList<string> ManualFiles);
public record CfFileResult(string Title, IReadOnlyList<string> Files);
public record MapHit(string ProjectId, string Title, string Description, string? IconUrl, long Downloads)
{
    public string DownloadsText => Downloads >= 1_000_000 ? $"{Downloads / 1_000_000.0:0.#}M 下載"
        : Downloads >= 1_000 ? $"{Downloads / 1_000.0:0.#}K 下載" : $"{Downloads} 下載";
}
public record MapDownload(string Title, string FileName, string Url);

/// CurseForge：搜尋（模組／資源包／光影／整合包）、單檔安裝（含必要相依）、整合包線上安裝與本機匯入。
/// 需要使用者自己的 API 金鑰（本機匯入 .mrpack 不需要）。未編譯、未實測；欄位名稱依 CurseForge Core API 記憶撰寫。
/// 作者不開放第三方下載的檔案（API 不給 downloadUrl）不會繞過：整合包列在「需手動下載」清單並寫入 manual-downloads.txt，單檔則直接提示。
public class CurseForgeService
{
    const int GameMinecraft = 432, ClassModpacks = 4471, ClassMods = 6, ClassResourcePacks = 12, ClassShaders = 6552, ClassWorlds = 17;
    const int RelationRequired = 3;                                   // dependencies[].relationType：必要相依
    const int LoaderForge = 1, LoaderFabric = 4, LoaderQuilt = 5, LoaderNeoForge = 6; // modLoaderType

    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { BaseAddress = new Uri("https://api.curseforge.com/v1/"), Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        return c;
    }

    readonly IInstanceService _instances;
    public CurseForgeService(IInstanceService instances) => _instances = instances;

    static async Task<JsonDocument> Api(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var key = CurseForgeSettings.GetKey() ?? throw new InvalidOperationException("尚未設定 CurseForge API 金鑰");
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Add("x-api-key", key);
        if (body != null) req.Content = JsonContent.Create(body);
        using var res = await Http.SendAsync(req, ct);
        if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("CurseForge 拒絕了請求（金鑰錯誤或沒有權限）");
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    // ---------- 分類／載入器對應 ----------
    static int ClassFor(ContentKind k) => k switch
    {
        ContentKind.Mod => ClassMods, ContentKind.ResourcePack => ClassResourcePacks,
        ContentKind.Shader => ClassShaders, _ => ClassModpacks
    };

    static string FolderFor(ContentKind k) => k switch
    {
        ContentKind.ResourcePack => "resourcepacks", ContentKind.Shader => "shaderpacks", _ => "mods"
    };

    /// 只有模組需要載入器過濾；資源包／光影不分載入器。
    /// 1.20.1 的 NeoForge 還是舊命名、吃 Forge 的模組，所以用 Forge 的篩選。
    static int? LoaderFilter(LoaderType? l, string mc, ContentKind kind)
    {
        if (kind != ContentKind.Mod || l is null) return null;
        return l.Value switch
        {
            LoaderType.Forge => LoaderForge,
            LoaderType.Fabric => LoaderFabric,
            LoaderType.Quilt => LoaderQuilt,
            LoaderType.NeoForge => mc == "1.20.1" ? LoaderForge : LoaderNeoForge,
            _ => null
        };
    }

    // ---------- 搜尋 ----------
    public async Task<IReadOnlyList<ContentHit>> SearchAsync(string query, ContentKind kind, string mcVersion, LoaderType? loader, CancellationToken ct = default)
    {
        var url = $"mods/search?gameId={GameMinecraft}&classId={ClassFor(kind)}&sortField=2&sortOrder=desc&pageSize=20";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&searchFilter={Uri.EscapeDataString(query.Trim())}";
        if (kind != ContentKind.Modpack && !string.IsNullOrEmpty(mcVersion)) url += $"&gameVersion={Uri.EscapeDataString(mcVersion)}";
        if (LoaderFilter(loader, mcVersion, kind) is int lt) url += $"&modLoaderType={lt}";

        using var doc = await Api(HttpMethod.Get, url, null, ct);
        var list = new List<ContentHit>();
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            string? icon = m.TryGetProperty("logo", out var lg) ? Str(lg, "thumbnailUrl") : null;
            list.Add(new ContentHit(m.GetProperty("id").GetInt32().ToString(), Str(m, "name") ?? "", Str(m, "summary") ?? "", icon, kind));
        }
        return list;
    }

    public Task<IReadOnlyList<ContentHit>> SearchModpacksAsync(string query, CancellationToken ct = default) =>
        SearchAsync(query, ContentKind.Modpack, "", null, ct);

    // ---------- 單檔安裝（模組／資源包／光影） ----------
    /// 安裝到指定實例；模組會一併安裝必要相依。回傳實際寫入的檔名。
    public async Task<CfFileResult> InstallFileAsync(Guid instanceId, string projectId, ContentKind kind,
        IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default)
    {
        if (kind == ContentKind.Modpack) throw new InvalidOperationException("整合包請用 InstallModpackAsync");
        var cfg = await _instances.GetAsync(instanceId) ?? throw new InvalidOperationException("找不到實例");
        var dir = Path.GetFullPath(Path.Combine(_instances.GetInstanceDir(instanceId), FolderFor(kind)));
        Directory.CreateDirectory(dir);

        var installed = new List<string>();
        string title = $"專案 {projectId}";
        using (var info = await Api(HttpMethod.Get, $"mods/{projectId}", null, ct))
            title = Str(info.RootElement.GetProperty("data"), "name") ?? title;

        await InstallProjectAsync(int.Parse(projectId), kind, cfg, dir, new HashSet<int>(), 0, installed, progress, ct);
        progress?.Report(("完成", 100));
        return new CfFileResult(title, installed);
    }

    async Task InstallProjectAsync(int projectId, ContentKind kind, InstanceConfig cfg, string dir, HashSet<int> seen, int depth,
        List<string> installed, IProgress<(string Stage, double Percent)>? progress, CancellationToken ct)
    {
        if (!seen.Add(projectId) || depth > 5) return;
        progress?.Report(($"查詢檔案（{projectId}）", 0));

        var file = await PickFileAsync(projectId, cfg, kind, ct)
                   ?? throw new InvalidOperationException($"找不到適用於 {cfg.McVersion}{(kind == ContentKind.Mod ? " / " + cfg.Loader : "")} 的檔案（專案 {projectId}）");
        using var keep = file.Doc;
        var f = file.File;

        // 必要相依（只對模組；相依一律當模組處理）
        if (kind == ContentKind.Mod && f.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            foreach (var d in deps.EnumerateArray())
                if (Int(d, "relationType") == RelationRequired && Int(d, "modId") is int depId)
                    await InstallProjectAsync(depId, ContentKind.Mod, cfg, dir, seen, depth + 1, installed, progress, ct);

        int fileId = f.GetProperty("id").GetInt32();
        var fileName = Path.GetFileName(Str(f, "fileName") ?? $"{fileId}.jar");
        var url = Str(f, "downloadUrl") ?? await GetDownloadUrl(projectId, fileId, ct);
        if (url is null)
        {
            string site = "";
            try { using var i = await Api(HttpMethod.Get, $"mods/{projectId}", null, ct); site = i.RootElement.GetProperty("data").TryGetProperty("links", out var lk) ? Str(lk, "websiteUrl") ?? "" : ""; } catch { }
            throw new InvalidOperationException($"「{fileName}」作者不開放第三方啟動器下載，請到 CurseForge 網站手動下載後放進 {Path.GetFileName(dir)} 資料夾。{site}");
        }

        var dest = Path.GetFullPath(Path.Combine(dir, fileName));
        if (!dest.StartsWith(dir + Path.DirectorySeparatorChar)) throw new InvalidDataException("不安全的檔名：" + fileName);
        progress?.Report(($"下載 {fileName}", 0));
        await DownloadAsync(url, dest, Sha1Of(f), ct);
        installed.Add(fileName);
    }

    record PickedFile(JsonDocument Doc, JsonElement File);

    /// 取得適合此實例的最新檔案：先挑正式版，再 beta、alpha；同等級取日期最新。
    async Task<PickedFile?> PickFileAsync(int projectId, InstanceConfig cfg, ContentKind kind, CancellationToken ct)
    {
        var loaders = new List<int?> { LoaderFilter(cfg.Loader, cfg.McVersion, kind) };
        if (cfg.Loader == LoaderType.Quilt && kind == ContentKind.Mod) loaders.Add(LoaderFabric); // Quilt 可載入 Fabric 模組

        foreach (var lt in loaders)
        {
            var url = $"mods/{projectId}/files?pageSize=50&gameVersion={Uri.EscapeDataString(cfg.McVersion)}";
            if (lt is int l) url += $"&modLoaderType={l}";
            var doc = await Api(HttpMethod.Get, url, null, ct);
            var best = doc.RootElement.GetProperty("data").EnumerateArray()
                .Where(x => !(x.TryGetProperty("isAvailable", out var av) && av.ValueKind == JsonValueKind.False))
                .OrderBy(x => Int(x, "releaseType") ?? 9)                    // 1 release、2 beta、3 alpha
                .ThenByDescending(x => DateTime.TryParse(Str(x, "fileDate"), out var dt) ? dt : DateTime.MinValue)
                .Select(x => (JsonElement?)x).FirstOrDefault();
            if (best is JsonElement b) return new PickedFile(doc, b);
            doc.Dispose();
        }
        return null;
    }

    static string? Sha1Of(JsonElement f)
    {
        if (f.TryGetProperty("hashes", out var hs) && hs.ValueKind == JsonValueKind.Array)
            foreach (var h in hs.EnumerateArray())
                if (Int(h, "algo") == 1) return Str(h, "value");
        return null;
    }

    // ---------- 整合包：線上安裝 ----------
    public async Task<CfInstallResult> InstallModpackAsync(string projectId, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default)
    {
        // 1. 找整合包的最新檔案
        progress?.Report(("取得整合包資訊", 0));
        string packName; int packFileId; string? packUrl; string packFileName;
        using (var info = await Api(HttpMethod.Get, $"mods/{projectId}", null, ct))
            packName = Str(info.RootElement.GetProperty("data"), "name") ?? "CurseForge 整合包";
        using (var files = await Api(HttpMethod.Get, $"mods/{projectId}/files?pageSize=20", null, ct))
        {
            var arr = files.RootElement.GetProperty("data").EnumerateArray().ToList();
            if (arr.Count == 0) throw new InvalidOperationException("此整合包沒有可下載的檔案");
            var f = arr.FirstOrDefault(x => Int(x, "releaseType") == 1);
            if (f.ValueKind == JsonValueKind.Undefined) f = arr[0];
            packFileId = f.GetProperty("id").GetInt32();
            packFileName = Str(f, "fileName") ?? "pack.zip";
            packUrl = Str(f, "downloadUrl");
        }
        packUrl ??= await GetDownloadUrl(int.Parse(projectId), packFileId, ct)
            ?? throw new InvalidOperationException("此整合包作者不開放第三方啟動器下載，請到 CurseForge 網站手動下載後，用「匯入本機整合包」匯入。");

        var tmp = Path.Combine(Path.GetTempPath(), $"ieml_cf_{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report(($"下載整合包 {packFileName}", 0));
            await DownloadAsync(packUrl, tmp, null, ct);
            return await InstallFromZipAsync(tmp, packName, progress, ct);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    // ---------- 整合包：本機匯入（CurseForge .zip 或 Modrinth .mrpack） ----------
    /// 自動判斷格式：有 manifest.json → CurseForge 整合包（需要金鑰以取得模組下載資訊）；有 modrinth.index.json → .mrpack（不需要金鑰）。
    public async Task<CfInstallResult> ImportLocalAsync(string path, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到檔案", path);
        bool isCf, isMr;
        using (var zip = ZipFile.OpenRead(path))
        {
            isCf = zip.GetEntry("manifest.json") != null;
            isMr = zip.GetEntry("modrinth.index.json") != null;
        }
        var baseName = Path.GetFileNameWithoutExtension(path);

        if (isMr)
        {
            progress?.Report(("匯入 Modrinth 整合包", 0));
            var id = await new ModrinthService(_instances).ImportMrpackAsync(path, baseName, ct);
            var cfg = await _instances.GetAsync(id);
            progress?.Report(("完成", 100));
            return new CfInstallResult(id, cfg?.Name ?? baseName, 0, Array.Empty<string>());
        }
        if (isCf) return await InstallFromZipAsync(path, baseName, progress, ct);
        throw new InvalidDataException("不是支援的整合包（需要 CurseForge 的 manifest.json 或 Modrinth 的 modrinth.index.json）");
    }

    // ---------- 共用：從 CurseForge 整合包 zip 建立實例 ----------
    async Task<CfInstallResult> InstallFromZipAsync(string zipPath, string fallbackName, IProgress<(string Stage, double Percent)>? progress, CancellationToken ct)
    {
        Guid? createdId = null;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var mEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("不是有效的 CurseForge 整合包（沒有 manifest.json）");
            using var manifest = JsonDocument.Parse(mEntry.Open());
            var root = manifest.RootElement;
            var mc = root.GetProperty("minecraft");
            string mcVersion = mc.GetProperty("version").GetString()!;
            var (loader, loaderVer) = ParseLoader(mc);
            string overrides = Str(root, "overrides") ?? "overrides";
            string name = Str(root, "name") is { Length: > 0 } n ? n : fallbackName;

            var cfg = await _instances.CreateAsync(new InstanceConfig { Name = name, McVersion = mcVersion, Loader = loader, LoaderVersion = loaderVer });
            createdId = cfg.Id;
            var dir = Path.GetFullPath(_instances.GetInstanceDir(cfg.Id));
            string Safe(string rel)
            {
                var full = Path.GetFullPath(Path.Combine(dir, rel));
                if (!full.StartsWith(dir + Path.DirectorySeparatorChar)) throw new InvalidDataException("整合包含有不安全的路徑：" + rel);
                return full;
            }

            // overrides
            progress?.Report(("解壓縮設定檔", 0));
            var prefix = overrides.TrimEnd('/') + "/";
            foreach (var e in zip.Entries)
                if (e.FullName.StartsWith(prefix) && !e.FullName.EndsWith('/'))
                {
                    var dest = Safe(e.FullName[prefix.Length..]);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }

            // 檔案清單 -> 下載資訊與所屬分類
            var refs = root.TryGetProperty("files", out var fl) && fl.ValueKind == JsonValueKind.Array
                ? fl.EnumerateArray().Select(f => (ProjectId: f.GetProperty("projectID").GetInt32(), FileId: f.GetProperty("fileID").GetInt32())).ToList()
                : new List<(int ProjectId, int FileId)>();
            if (refs.Count > 0 && !CurseForgeSettings.HasKey)
                throw new InvalidOperationException("這個整合包需要 CurseForge API 金鑰才能取得模組下載資訊，請先在視窗上方填入金鑰。");

            progress?.Report(("取得模組資訊", 0));
            var fileInfo = new Dictionary<int, JsonElement>();   // fileId -> file json
            var modInfo = new Dictionary<int, JsonElement>();    // modId -> mod json
            var keep = new List<JsonDocument>();                  // 讓 JsonElement 在下載期間保持有效
            try
            {
                foreach (var chunk in refs.Select(r => r.FileId).Distinct().Chunk(100))
                {
                    var d = await Api(HttpMethod.Post, "mods/files", new { fileIds = chunk }, ct); keep.Add(d);
                    foreach (var f in d.RootElement.GetProperty("data").EnumerateArray()) fileInfo[f.GetProperty("id").GetInt32()] = f;
                }
                foreach (var chunk in refs.Select(r => r.ProjectId).Distinct().Chunk(50))
                {
                    var d = await Api(HttpMethod.Post, "mods", new { modIds = chunk }, ct); keep.Add(d);
                    foreach (var m in d.RootElement.GetProperty("data").EnumerateArray()) modInfo[m.GetProperty("id").GetInt32()] = m;
                }

                // 下載
                var manual = new List<string>();
                var gate = new SemaphoreSlim(6);
                int done = 0, total = Math.Max(refs.Count, 1), installed = 0;
                var tasks = refs.Select(r => Task.Run(async () =>
                {
                    string label = $"模組 {r.ProjectId}";
                    try
                    {
                        modInfo.TryGetValue(r.ProjectId, out var mod);
                        if (mod.ValueKind == JsonValueKind.Object) label = Str(mod, "name") ?? label;
                        string site = mod.ValueKind == JsonValueKind.Object && mod.TryGetProperty("links", out var lk) ? Str(lk, "websiteUrl") ?? "" : "";
                        int classId = mod.ValueKind == JsonValueKind.Object ? Int(mod, "classId") ?? ClassMods : ClassMods;

                        if (!fileInfo.TryGetValue(r.FileId, out var f)) { lock (manual) manual.Add($"{label}（找不到檔案 {r.FileId}）{site}"); return; }
                        if (classId == ClassWorlds) { lock (manual) manual.Add($"{label}（地圖／存檔，請手動放進 saves）{site}"); return; }

                        var fileName = Path.GetFileName(Str(f, "fileName") ?? $"{r.FileId}.jar");
                        var url = Str(f, "downloadUrl") ?? await GetDownloadUrl(r.ProjectId, r.FileId, ct);
                        if (url is null) { lock (manual) manual.Add($"{label}（{fileName}，作者不開放第三方下載）{site}"); return; }

                        var sub = classId switch { ClassResourcePacks => "resourcepacks", ClassShaders => "shaderpacks", _ => "mods" };
                        await gate.WaitAsync(ct);
                        try { await DownloadAsync(url, Safe(Path.Combine(sub, fileName)), Sha1Of(f), ct); Interlocked.Increment(ref installed); }
                        finally { gate.Release(); }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { lock (manual) manual.Add($"{label}（下載失敗：{ex.Message}）"); }
                    finally { progress?.Report(($"下載模組 {Interlocked.Increment(ref done)}/{refs.Count}", 100.0 * done / total)); }
                }, ct)).ToList();
                await Task.WhenAll(tasks);

                if (manual.Count > 0)
                    await File.WriteAllLinesAsync(Path.Combine(dir, "manual-downloads.txt"),
                        new[] { "以下檔案沒有自動安裝，請到 CurseForge 網站手動下載後放進對應資料夾：", "" }.Concat(manual), ct);
                progress?.Report(("完成", 100));
                createdId = null; // 成功，不要清掉
                return new CfInstallResult(cfg.Id, name, installed, manual);
            }
            finally { foreach (var d in keep) d.Dispose(); }
        }
        catch
        {
            // 失敗或取消：不要留下半成品實例
            if (createdId is Guid id) try { await _instances.DeleteAsync(id); } catch { }
            throw;
        }
    }


    // ---------- 地圖（CurseForge「Worlds」分類）----------
    public async Task<IReadOnlyList<MapHit>> SearchWorldsAsync(string query, string? mcVersion, int offset = 0, CancellationToken ct = default)
    {
        var url = $"mods/search?gameId={GameMinecraft}&classId={ClassWorlds}&sortField=2&sortOrder=desc&pageSize=20&index={offset}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&searchFilter={Uri.EscapeDataString(query.Trim())}";
        if (!string.IsNullOrWhiteSpace(mcVersion)) url += $"&gameVersion={Uri.EscapeDataString(mcVersion.Trim())}";
        using var doc = await Api(HttpMethod.Get, url, null, ct);
        var list = new List<MapHit>();
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            string? icon = m.TryGetProperty("logo", out var lg) ? Str(lg, "thumbnailUrl") : null;
            long dl = m.TryGetProperty("downloadCount", out var d) && d.ValueKind == JsonValueKind.Number ? (long)d.GetDouble() : 0;
            list.Add(new MapHit(m.GetProperty("id").GetInt32().ToString(), Str(m, "name") ?? "", Str(m, "summary") ?? "", icon, dl));
        }
        return list;
    }

    /// 取得地圖最新（優先正式版）檔案的下載網址；作者不開放第三方下載時丟出說明。
    public async Task<MapDownload> GetWorldDownloadAsync(string projectId, CancellationToken ct = default)
    {
        string title = $"地圖 {projectId}", site = "";
        using (var info = await Api(HttpMethod.Get, $"mods/{projectId}", null, ct))
        {
            var data = info.RootElement.GetProperty("data");
            title = Str(data, "name") ?? title;
            if (data.TryGetProperty("links", out var lk)) site = Str(lk, "websiteUrl") ?? "";
        }
        using var files = await Api(HttpMethod.Get, $"mods/{projectId}/files?pageSize=30", null, ct);
        var best = files.RootElement.GetProperty("data").EnumerateArray()
            .Where(x => !(x.TryGetProperty("isAvailable", out var av) && av.ValueKind == JsonValueKind.False))
            .OrderBy(x => Int(x, "releaseType") ?? 9)
            .ThenByDescending(x => DateTime.TryParse(Str(x, "fileDate"), out var dt) ? dt : DateTime.MinValue)
            .Select(x => (JsonElement?)x).FirstOrDefault()
            ?? throw new InvalidOperationException("這張地圖沒有可下載的檔案");
        int fileId = best.GetProperty("id").GetInt32();
        var fileName = Path.GetFileName(Str(best, "fileName") ?? $"{fileId}.zip");
        var url = Str(best, "downloadUrl") ?? await GetDownloadUrl(int.Parse(projectId), fileId, ct)
            ?? throw new InvalidOperationException($"「{title}」作者不開放第三方啟動器下載，請到 CurseForge 網站手動下載後，用「匯入 zip」匯入。{site}");
        return new MapDownload(title, fileName, url);
    }

    public Task DownloadFileAsync(string url, string dest, CancellationToken ct = default) => DownloadAsync(url, dest, null, ct);

    static async Task<string?> GetDownloadUrl(int modId, int fileId, CancellationToken ct)
    {
        try
        {
            using var d = await Api(HttpMethod.Get, $"mods/{modId}/files/{fileId}/download-url", null, ct);
            var u = d.RootElement.TryGetProperty("data", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return string.IsNullOrEmpty(u) ? null : u;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // 403 等：作者不開放
    }

    /// manifest 的 modLoaders：id 形如 "forge-47.2.0"、"neoforge-21.1.50"（1.20.1 舊命名為 "neoforge-47.1.106"）、"fabric-0.15.7"、"quilt-0.20.0"
    static (LoaderType, string?) ParseLoader(JsonElement mc)
    {
        if (!mc.TryGetProperty("modLoaders", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return (LoaderType.Vanilla, null);
        var pick = arr[0];
        foreach (var m in arr.EnumerateArray())
            if (m.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True) { pick = m; break; }
        var id = Str(pick, "id") ?? "";
        int i = id.IndexOf('-');
        if (i <= 0) return (LoaderType.Vanilla, null);
        var ver = id[(i + 1)..];
        return id[..i].ToLowerInvariant() switch
        {
            "forge" => (LoaderType.Forge, ver),
            "neoforge" => (LoaderType.NeoForge, ver),
            "fabric" => (LoaderType.Fabric, ver),
            "quilt" => (LoaderType.Quilt, ver),
            _ => throw new NotSupportedException($"不支援的載入器：{id}")
        };
    }

    static async Task DownloadAsync(string url, string dest, string? sha1, CancellationToken ct)
    {
        if (sha1 != null && File.Exists(dest) && Sha1Of(dest) == sha1) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var part = dest + ".part";
        using (var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            await using var fs = File.Create(part);
            await res.Content.CopyToAsync(fs, ct);
        }
        if (sha1 != null && Sha1Of(part) != sha1) { File.Delete(part); throw new InvalidDataException($"檢查碼不符：{Path.GetFileName(dest)}"); }
        File.Move(part, dest, true);
    }

    static string Sha1Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }
}
