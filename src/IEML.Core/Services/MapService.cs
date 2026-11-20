using System.IO.Compression;
using IEML.Core.Contracts;
namespace IEML.Core.Services;

public record WorldInfo(string Name, string Path, long Size, DateTime LastPlayed)
{
    public string SizeText => Size >= 1_073_741_824 ? $"{Size / 1_073_741_824.0:0.#} GB"
        : Size >= 1_048_576 ? $"{Size / 1_048_576.0:0.#} MB" : $"{Math.Max(1, Size / 1024)} KB";
    public string DateText => LastPlayed.ToString("yyyy-MM-dd HH:mm");
}

/// 管理實例的 saves 資料夾：列出、匯入（zip／資料夾／網址）、備份、刪除。
/// 判斷地圖的依據是資料夾裡有 level.dat。未編譯、未實測。
public class MapService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    readonly IInstanceService _instances;
    public MapService(IInstanceService instances) => _instances = instances;

    string InstanceDir(Guid id) => System.IO.Path.GetFullPath(_instances.GetInstanceDir(id));
    public string SavesDir(Guid id) => System.IO.Path.Combine(InstanceDir(id), "saves");
    public string BackupsDir(Guid id) => System.IO.Path.Combine(InstanceDir(id), "backups");

    // ---------- 列出 ----------
    public IReadOnlyList<WorldInfo> List(Guid id)
    {
        var saves = SavesDir(id);
        if (!Directory.Exists(saves)) return Array.Empty<WorldInfo>();
        var list = new List<WorldInfo>();
        foreach (var d in Directory.EnumerateDirectories(saves))
        {
            var lv = System.IO.Path.Combine(d, "level.dat");
            if (!File.Exists(lv)) continue;
            long size = 0;
            try { size = Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { }
            list.Add(new WorldInfo(System.IO.Path.GetFileName(d), d, size, File.GetLastWriteTime(lv)));
        }
        return list.OrderByDescending(w => w.LastPlayed).ToList();
    }

    // ---------- 匯入 zip ----------
    /// 在 zip 裡找最淺的 level.dat，把它所在的資料夾整個解壓成一張地圖。回傳最後使用的資料夾名稱。
    public Task<string> ImportZipAsync(Guid id, string zipPath, string? nameHint = null,
        IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(zipPath);
            static string N(ZipArchiveEntry e) => e.FullName.Replace('\\', '/');
            var lv = zip.Entries
                .Where(e => !N(e).StartsWith("__MACOSX/") && e.Name == "level.dat")
                .OrderBy(e => N(e).Count(c => c == '/')).FirstOrDefault()
                ?? throw new InvalidDataException("這不是 Minecraft 地圖（壓縮檔裡找不到 level.dat）。若是整合包請用 CurseForge 視窗的「匯入本機整合包」。");
            var prefix = N(lv)[..^"level.dat".Length];                 // "" 或 "地圖名/"
            var baseName = prefix.Length > 0 ? prefix.TrimEnd('/').Split('/').Last()
                         : (string.IsNullOrWhiteSpace(nameHint) ? System.IO.Path.GetFileNameWithoutExtension(zipPath) : nameHint);

            var saves = SavesDir(id); Directory.CreateDirectory(saves);
            var name = Unique(saves, Sanitize(baseName));
            var dest = System.IO.Path.Combine(saves, name);
            var destFull = System.IO.Path.GetFullPath(dest) + System.IO.Path.DirectorySeparatorChar;

            var entries = zip.Entries.Where(e => N(e).StartsWith(prefix, StringComparison.Ordinal)
                                              && !N(e).StartsWith("__MACOSX/") && !N(e).EndsWith("/")).ToList();
            int done = 0;
            try
            {
                foreach (var e in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(dest, N(e)[prefix.Length..]));
                    if (!target.StartsWith(destFull)) throw new InvalidDataException("壓縮檔含有不安全的路徑：" + e.FullName);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, true);
                    progress?.Report(($"解壓縮 {name}", 100.0 * ++done / Math.Max(1, entries.Count)));
                }
            }
            catch { try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { } throw; }   // 失敗不留半張地圖
            return name;
        }, ct);

    // ---------- 匯入資料夾 ----------
    public Task<string> ImportFolderAsync(Guid id, string folder, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var src = folder;
            if (!File.Exists(System.IO.Path.Combine(src, "level.dat")))
                src = Directory.EnumerateDirectories(folder).FirstOrDefault(d => File.Exists(System.IO.Path.Combine(d, "level.dat")))
                      ?? throw new InvalidDataException("這個資料夾裡找不到 level.dat，不是 Minecraft 地圖。");
            var saves = SavesDir(id); Directory.CreateDirectory(saves);
            var name = Unique(saves, Sanitize(System.IO.Path.GetFileName(src.TrimEnd('\\', '/'))));
            var dest = System.IO.Path.Combine(saves, name);
            var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).ToList();
            int done = 0;
            try
            {
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = System.IO.Path.Combine(dest, System.IO.Path.GetRelativePath(src, f));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    File.Copy(f, target, true);
                    progress?.Report(($"複製 {name}", 100.0 * ++done / Math.Max(1, files.Count)));
                }
            }
            catch { try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { } throw; }
            return name;
        }, ct);

    // ---------- 從網址下載（必須是 zip 的直接連結）----------
    public async Task<string> DownloadAndImportAsync(Guid id, string url, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidOperationException("請貼上 http(s) 開頭的網址");
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ieml_map_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadToAsync(uri.ToString(), tmp, progress, ct);
            var hint = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            return await ImportZipAsync(id, tmp, hint, progress, ct);
        }
        catch (InvalidDataException) { throw new InvalidDataException("下載的檔案不是地圖 zip。很多網站（Planet Minecraft、Mediafire 等）的連結是網頁而不是檔案，請先用瀏覽器下載，再用「匯入 zip」。"); }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    async Task DownloadToAsync(string url, string dest, IProgress<(string Stage, double Percent)>? progress, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long? total = res.Content.Headers.ContentLength;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(dest);
        var buf = new byte[81920]; long got = 0; int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            got += n;
            progress?.Report(("下載地圖", total is > 0 ? 100.0 * got / total.Value : 0));
        }
    }

    /// 給外部（CurseForge 下載）用：下載完的 zip 直接匯入。
    public async Task<string> ImportDownloadedAsync(Guid id, string zipPath, string? nameHint, IProgress<(string Stage, double Percent)>? progress, CancellationToken ct)
    {
        try { return await ImportZipAsync(id, zipPath, nameHint, progress, ct); }
        finally { try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { } }
    }

    // ---------- 備份／刪除 ----------
    public Task<string> BackupAsync(Guid id, WorldInfo w, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var dir = BackupsDir(id); Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, $"{w.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(w.Path, file, CompressionLevel.Fastest, includeBaseDirectory: true);
            return file;
        }, ct);

    public void Delete(WorldInfo w) { if (Directory.Exists(w.Path)) Directory.Delete(w.Path, true); }

    // ---------- 工具 ----------
    static string Sanitize(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "地圖" : name;
    }

    static string Unique(string saves, string name)
    {
        if (!Directory.Exists(System.IO.Path.Combine(saves, name))) return name;
        for (int i = 2; ; i++)
            if (!Directory.Exists(System.IO.Path.Combine(saves, $"{name} ({i})"))) return $"{name} ({i})";
    }
}
