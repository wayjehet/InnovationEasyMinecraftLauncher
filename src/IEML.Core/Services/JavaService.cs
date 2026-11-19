using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using IEML.Core.Models;
namespace IEML.Core.Services;

public enum JavaSource { Manual, Local, Downloaded }
public record JavaInstall(string Path, int Major, string Description);
public record JavaResolution(string Path, int Major, JavaSource Source)
{
    public string SourceText => Source switch { JavaSource.Manual => "手動指定", JavaSource.Local => "本機", _ => "已下載" };
}

/// Java 管理。選擇順序（使用者指定）：
///   0. 實例設置裡手動指定的路徑（存在就直接用）
///   1. 本機已安裝、版本「符合」需求
///   2. 本機已安裝、版本「較高」（取最接近的；需求為 Java 8 的舊版不使用，因舊版 Forge/LaunchWrapper 在 Java 9+ 常會壞）
///   3. 已下載到 %AppData%\InnovationEasyLauncher\java\<版本>\ 的符合版本
///   4. 從 Adoptium（Temurin）下載符合版本的 JRE
/// 需求版本取自 Mojang 版本 JSON 的 javaVersion.majorVersion，連不上時用內建對照表推測。
/// 未實測；下載沒有做校驗碼檢查（只走 HTTPS）。
public sealed class JavaService
{
    const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        return c;
    }

    static string DownloadRoot => Path.Combine(AppPaths.Root, "java");
    static List<JavaInstall>? _localCache;
    static readonly Dictionary<string, int> _reqCache = new();

    // ---------- 需求版本 ----------

    public async Task<int> GetRequiredMajorAsync(string mcVersion, CancellationToken ct = default)
    {
        mcVersion = (mcVersion ?? "").Trim();
        lock (_reqCache) if (_reqCache.TryGetValue(mcVersion, out var c)) return c;
        int major;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var man = JsonDocument.Parse(await Http.GetStringAsync(ManifestUrl, cts.Token));
            string? url = null;
            foreach (var v in man.RootElement.GetProperty("versions").EnumerateArray())
                if (v.GetProperty("id").GetString() == mcVersion) { url = v.GetProperty("url").GetString(); break; }
            if (url is null) major = Guess(mcVersion);
            else
            {
                using var ver = JsonDocument.Parse(await Http.GetStringAsync(url, cts.Token));
                // 很舊的版本沒有 javaVersion 欄位 → Java 8
                major = ver.RootElement.TryGetProperty("javaVersion", out var jv) && jv.TryGetProperty("majorVersion", out var m)
                    ? m.GetInt32() : 8;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { major = Guess(mcVersion); }
        lock (_reqCache) _reqCache[mcVersion] = major;
        return major;
    }

    // 離線時的推測：1.16 以下 → 8；1.17 → 16；1.18–1.20.4 → 17；1.20.5 以上 → 21；其他格式的版本號 → 21（未確認）
    static int Guess(string mc)
    {
        var m = Regex.Match(mc, @"^1\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return 21;
        int minor = int.Parse(m.Groups[1].Value);
        int patch = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (minor <= 16) return 8;
        if (minor == 17) return 16;
        if (minor < 20 || (minor == 20 && patch < 5)) return 17;
        return 21;
    }

    // Java 16 已停止維護，下載時改抓 17（相容）
    static int DownloadMajor(int required) => required == 16 ? 17 : required;

    // ---------- 本機偵測 ----------

    public async Task<IReadOnlyList<JavaInstall>> DetectLocalAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (!refresh && _localCache is not null) return _localCache;
        var list = await Task.Run(() => DetectCore(ct), ct);
        _localCache = list;
        return list;
    }

    static List<JavaInstall> DetectCore(CancellationToken ct)
    {
        var exes = new List<string>();
        void AddHome(string? home)
        {
            if (string.IsNullOrWhiteSpace(home)) return;
            var p = Path.Combine(home, "bin", "java.exe");
            if (File.Exists(p)) exes.Add(p);
        }

        AddHome(Environment.GetEnvironmentVariable("JAVA_HOME"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            try { var p = Path.Combine(dir.Trim().Trim('"'), "java.exe"); if (File.Exists(p)) exes.Add(p); } catch { }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vendors = new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu", "BellSoft", "Amazon Corretto", "Semeru", "AdoptOpenJDK", "Eclipse Foundation" };
        foreach (var root in new[] { pf, pf86 })
            foreach (var v in vendors)
            {
                var d = Path.Combine(root, v);
                if (!Directory.Exists(d)) continue;
                try { foreach (var sub in Directory.EnumerateDirectories(d)) AddHome(sub); } catch { }
            }

        // 官方啟動器下載的執行環境
        var mcRuntime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime");
        if (Directory.Exists(mcRuntime))
            try { exes.AddRange(Directory.EnumerateFiles(mcRuntime, "java.exe", SearchOption.AllDirectories)); } catch { }

        var result = new List<JavaInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dl = Path.GetFullPath(DownloadRoot);
        foreach (var exe in exes)
        {
            ct.ThrowIfCancellationRequested();
            string full; try { full = Path.GetFullPath(exe); } catch { continue; }
            if (full.StartsWith(dl, StringComparison.OrdinalIgnoreCase)) continue; // 自己下載的另外算
            if (!seen.Add(full)) continue;
            var major = ReadMajor(full);
            if (major is null) continue;
            var home = Directory.GetParent(Path.GetDirectoryName(full)!)?.FullName ?? full;
            result.Add(new JavaInstall(full, major.Value, $"Java {major}  —  {home}"));
        }
        return result.OrderByDescending(j => j.Major).ThenBy(j => j.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static int? ReadMajor(string javaExe)
    {
        try
        {
            var home = Directory.GetParent(Path.GetDirectoryName(javaExe)!)?.FullName;
            if (home is not null)
            {
                var rel = Path.Combine(home, "release");
                if (File.Exists(rel))
                {
                    var m = Regex.Match(File.ReadAllText(rel), "JAVA_VERSION=\"([^\"]+)\"");
                    if (m.Success) return ParseMajor(m.Groups[1].Value);
                }
            }
            // 沒有 release 檔（例如 Oracle 的 javapath 捷徑）→ 實際執行 java -version
            var psi = new ProcessStartInfo(javaExe, "-version")
            { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var err = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
            var v = Regex.Match(err, "version \"([^\"]+)\"");
            return v.Success ? ParseMajor(v.Groups[1].Value) : null;
        }
        catch { return null; }
    }

    // "1.8.0_391" → 8，"17.0.9" → 17，"21" → 21
    static int? ParseMajor(string v)
    {
        var m = Regex.Match(v, @"^(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        int a = int.Parse(m.Groups[1].Value);
        if (a == 1 && m.Groups[2].Success) return int.Parse(m.Groups[2].Value);
        return a;
    }

    // ---------- 選擇 / 下載 ----------

    public async Task<JavaResolution> ResolveAsync(InstanceConfig cfg, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default)
    {
        // 0. 手動指定
        if (!string.IsNullOrWhiteSpace(cfg.JavaPath) && File.Exists(cfg.JavaPath))
            return new JavaResolution(cfg.JavaPath, ReadMajor(cfg.JavaPath) ?? 0, JavaSource.Manual);

        var req = await GetRequiredMajorAsync(cfg.McVersion, ct);
        var local = await DetectLocalAsync(false, ct);

        // 1. 本機符合
        var exact = local.FirstOrDefault(j => j.Major == req);
        if (exact is not null) return new JavaResolution(exact.Path, exact.Major, JavaSource.Local);

        // 2. 本機較高（需求 Java 8 的舊版不用較高版本）
        if (req > 8)
        {
            var higher = local.Where(j => j.Major > req).OrderBy(j => j.Major).FirstOrDefault();
            if (higher is not null) return new JavaResolution(higher.Path, higher.Major, JavaSource.Local);
        }

        // 3. 已下載
        var dlMajor = DownloadMajor(req);
        var have = FindDownloaded(dlMajor);
        if (have is not null) return have;

        // 4. 下載
        return await DownloadAsync(dlMajor, progress, ct);
    }

    static JavaResolution? FindDownloaded(int major)
    {
        var dir = Path.Combine(DownloadRoot, major.ToString());
        if (!Directory.Exists(dir)) return null;
        var exe = Directory.EnumerateFiles(dir, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
        return exe is null ? null : new JavaResolution(exe, major, JavaSource.Downloaded);
    }

    static async Task<JavaResolution> DownloadAsync(int major, IProgress<(string Stage, double Percent)>? progress, CancellationToken ct)
    {
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";
        var url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/{arch}/jre/hotspot/normal/eclipse";
        Directory.CreateDirectory(DownloadRoot);
        var zip = Path.Combine(DownloadRoot, $"jre-{major}.zip");
        var tmp = Path.Combine(DownloadRoot, $"{major}.tmp");
        var dir = Path.Combine(DownloadRoot, major.ToString());
        try
        {
            progress?.Report(($"下載 Java {major}", 0));
            using (var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!res.IsSuccessStatusCode)
                    throw new InvalidOperationException($"下載 Java {major} 失敗（HTTP {(int)res.StatusCode}）。可在實例設置手動指定 Java 路徑。");
                var total = res.Content.Headers.ContentLength;
                await using var net = await res.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(zip);
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await net.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report(($"下載 Java {major}", 100.0 * done / total.Value));
                }
            }
            progress?.Report(($"解壓縮 Java {major}", 100));
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(tmp, dir);
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
        return FindDownloaded(major) ?? throw new InvalidOperationException("Java 解壓縮後找不到 java.exe。");
    }
}
