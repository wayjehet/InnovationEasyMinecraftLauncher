using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using IEML.Core.Contracts;
using IEML.Core.Models;
namespace IEML.Core.Services;

public class ModrinthService : IModrinthService
{
    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
        // Modrinth asks for an identifying User-Agent. Replace the contact with your own.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1 (contact: your-email@example.com)");
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
        var facets = new List<string[]> { new[] { $"project_type:{TypeName(kind)}" } };
        if (!string.IsNullOrEmpty(mcVersion)) facets.Add(new[] { $"versions:{mcVersion}" });
        if (loader is not null && loader != LoaderType.Vanilla && (kind == ContentKind.Mod || kind == ContentKind.Modpack))
            facets.Add(new[] { $"categories:{LoaderName(loader.Value)}" });

        var url = $"search?query={Uri.EscapeDataString(query)}&limit=20&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        using var doc = await GetJson(url, default);
        var list = new List<ContentHit>();
        foreach (var h in doc.RootElement.GetProperty("hits").EnumerateArray())
            list.Add(new ContentHit(h.GetProperty("project_id").GetString()!, h.GetProperty("title").GetString() ?? "",
                h.GetProperty("description").GetString() ?? "",
                h.TryGetProperty("icon_url", out var ic) ? ic.GetString() : null, kind));
        return list;
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
        var ver = doc.RootElement[0];

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

    static async Task DownloadAsync(string url, string dest, string? sha1, CancellationToken ct)
    {
        if (File.Exists(dest) && sha1 != null && Sha1Of(dest) == sha1) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using (var fs = File.Create(dest)) await res.Content.CopyToAsync(fs, ct);
        if (sha1 != null && Sha1Of(dest) != sha1) { File.Delete(dest); throw new InvalidDataException($"檢查碼不符：{Path.GetFileName(dest)}"); }
    }

    static string Sha1Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }

    public async Task<Guid> InstallModpackAsync(string projectId, CancellationToken ct = default)
    {
        string title; string packUrl;
        using (var proj = await GetJson($"project/{projectId}", ct)) title = proj.RootElement.GetProperty("title").GetString() ?? "Modpack";
        using (var vers = await GetJson($"project/{projectId}/version", ct))
        {
            if (vers.RootElement.GetArrayLength() == 0) throw new InvalidOperationException("此整合包沒有可下載的版本");
            var files = vers.RootElement[0].GetProperty("files");
            var f = files.EnumerateArray().FirstOrDefault(x => x.GetProperty("primary").GetBoolean());
            if (f.ValueKind == JsonValueKind.Undefined) f = files[0];
            packUrl = f.GetProperty("url").GetString()!;
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"ieml_{Guid.NewGuid():N}.mrpack");
        try
        {
            await DownloadAsync(packUrl, tmp, null, ct);
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
            foreach (var e in zip.Entries)
            {
                foreach (var prefix in new[] { "overrides/", "client-overrides/" })
                    if (e.FullName.StartsWith(prefix) && !e.FullName.EndsWith('/'))
                    {
                        var dest = Safe(e.FullName[prefix.Length..]);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        e.ExtractToFile(dest, true);
                    }
            }

            var gate = new SemaphoreSlim(6);
            var tasks = new List<Task>();
            foreach (var f in root.GetProperty("files").EnumerateArray())
            {
                if (f.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var cl) && cl.GetString() == "unsupported") continue;
                var dest = Safe(f.GetProperty("path").GetString()!);
                var url = f.GetProperty("downloads")[0].GetString()!;
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
