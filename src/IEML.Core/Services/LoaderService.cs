using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using IEML.Core.Models;
namespace IEML.Core.Services;

/// Fabric/Quilt: drop official profile JSON into versions/.
/// NeoForge（含 1.20.1 舊命名）/Forge: download the official installer jar and run it headless (--installClient) into the instance dir.
/// Returns the version id that CmlLib.Core should launch.
public static class LoaderService
{
    static readonly HttpClient Http = new();
    public delegate void Report(string stage, double percent);

    public static Task<string> EnsureInstalledAsync(InstanceConfig cfg, string dir,
        IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default) => cfg.Loader switch
    {
        LoaderType.Fabric => InstallProfile(cfg, dir, "https://meta.fabricmc.net/v2/versions/loader", ct),
        LoaderType.Quilt => InstallProfile(cfg, dir, "https://meta.quiltmc.org/v3/versions/loader", ct),
        LoaderType.NeoForge => InstallNeoForge(cfg, dir, progress, ct),
        LoaderType.Forge => InstallForge(cfg, dir, progress, ct),
        _ => throw new NotSupportedException("原版不需要載入器")
    };

    // ---------- Fabric / Quilt ----------
    static async Task<string> InstallProfile(InstanceConfig cfg, string dir, string meta, CancellationToken ct)
    {
        var loaderVer = cfg.LoaderVersion;
        if (string.IsNullOrEmpty(loaderVer))
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"{meta}/{cfg.McVersion}", ct));
            string? first = null, stable = null;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var l = e.GetProperty("loader");
                var v = l.GetProperty("version").GetString();
                first ??= v;
                bool isStable = l.TryGetProperty("stable", out var st) ? st.GetBoolean() : !(v?.Contains('-') ?? true);
                if (isStable) { stable = v; break; }
            }
            loaderVer = stable ?? first ?? throw new InvalidOperationException($"{cfg.McVersion} 沒有可用的 {cfg.Loader} 版本");
        }
        var profile = await Http.GetStringAsync($"{meta}/{cfg.McVersion}/{loaderVer}/profile/json", ct);
        using var pd = JsonDocument.Parse(profile);
        var id = pd.RootElement.GetProperty("id").GetString()!;
        var vdir = Path.Combine(dir, "versions", id);
        Directory.CreateDirectory(vdir);
        var jp = Path.Combine(vdir, id + ".json");
        if (!File.Exists(jp)) await File.WriteAllTextAsync(jp, profile, ct);
        cfg.LoaderVersion = loaderVer;
        return id;
    }

    // ---------- NeoForge ----------
    /// 1.20.1 的 NeoForge 還是舊命名（版本 47.1.x，artifact 為 net.neoforged:forge，安裝程式 forge-1.20.1-47.1.x-installer.jar）；
    /// 1.20.2 以上才是 net.neoforged:neoforge（版本 20.2.x / 21.x.x …）。
    static bool IsLegacyNeoForge(string mc) => mc == "1.20.1";

    static async Task<string> InstallNeoForge(InstanceConfig cfg, string dir, IProgress<(string, double)>? progress, CancellationToken ct)
    {
        var mc = cfg.McVersion;
        var legacy = IsLegacyNeoForge(mc);
        var ver = cfg.LoaderVersion;
        // 容許寫成「1.20.1-47.1.106」，統一存成「47.1.106」
        if (legacy && !string.IsNullOrEmpty(ver) && ver.StartsWith(mc + "-")) ver = ver[(mc.Length + 1)..];
        if (string.IsNullOrEmpty(ver)) ver = await LatestNeoForge(mc, ct);

        var id = FindVersionId(dir, ver);
        if (id == null)
        {
            string url, file;
            if (legacy)
            {
                var full = $"{mc}-{ver}";
                url = $"https://maven.neoforged.net/releases/net/neoforged/forge/{full}/forge-{full}-installer.jar";
                file = $"neoforge-legacy-{full}-installer.jar";
            }
            else
            {
                url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{ver}/neoforge-{ver}-installer.jar";
                file = $"neoforge-{ver}-installer.jar";
            }
            await RunInstaller(url, file, dir, cfg, progress, ct);
            id = FindVersionId(dir, ver) ?? throw new InvalidOperationException("NeoForge 安裝完成，但找不到版本資料夾（請看 loader-install.log）");
        }
        cfg.LoaderVersion = ver;
        return id;
    }

    static async Task<string> LatestNeoForge(string mc, CancellationToken ct)
    {
        var p = mc.Split('.');
        if (p.Length < 2 || p[0] != "1" || !int.TryParse(p[1], out var a))
            throw new NotSupportedException($"NeoForge 不支援版本 {mc}");
        int b = p.Length > 2 && int.TryParse(p[2], out var bb) ? bb : 0;

        if (IsLegacyNeoForge(mc))
        {
            // 舊命名：metadata 裡的版本形如 "1.20.1-47.1.106"，去掉 MC 版本前綴後比較
            var xmlL = XDocument.Parse(await Http.GetStringAsync("https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml", ct));
            var pre = mc + "-";
            var allL = xmlL.Descendants("versions").Elements("version").Select(x => x.Value)
                .Where(v => v.StartsWith(pre)).Select(v => v[pre.Length..]).ToList();
            var stableL = allL.Where(v => !v.Contains('-')).ToList();
            return Newest(stableL.Count > 0 ? stableL : allL)
                   ?? throw new InvalidOperationException($"找不到適用於 {mc} 的 NeoForge 版本");
        }

        // 新命名：MC 1.A.B -> "A.B.<build>"（B 沒有時為 0）。只有 1.20.2 以上使用這個 artifact。
        if (a < 20 || (a == 20 && b < 2)) throw new NotSupportedException("NeoForge 只支援 Minecraft 1.20.1 與 1.20.2 以上");
        var prefix = $"{a}.{b}.";
        var xml = XDocument.Parse(await Http.GetStringAsync("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", ct));
        var all = xml.Descendants("versions").Elements("version").Select(x => x.Value).Where(v => v.StartsWith(prefix)).ToList();
        var stable = all.Where(v => !v.Contains('-')).ToList();
        return Newest(stable.Count > 0 ? stable : all)
               ?? throw new InvalidOperationException($"找不到適用於 {mc} 的 NeoForge 版本");
    }

    static string? Newest(IEnumerable<string> versions)
    {
        string? best = null;
        foreach (var v in versions) if (best == null || CompareVer(v, best) > 0) best = v;
        return best;
    }

    static int CompareVer(string a, string b)
    {
        static int[] Parts(string s) => s.Split('.', '-').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var (pa, pb) = (Parts(a), Parts(b));
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // ---------- Forge ----------
    static async Task<string> InstallForge(InstanceConfig cfg, string dir, IProgress<(string, double)>? progress, CancellationToken ct)
    {
        var ver = cfg.LoaderVersion;
        if (string.IsNullOrEmpty(ver))
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct));
            var promos = doc.RootElement.GetProperty("promos");
            ver = promos.TryGetProperty($"{cfg.McVersion}-recommended", out var r) ? r.GetString()
                : promos.TryGetProperty($"{cfg.McVersion}-latest", out var l) ? l.GetString() : null;
            if (ver == null) throw new InvalidOperationException($"找不到適用於 {cfg.McVersion} 的 Forge 版本");
        }
        var id = FindVersionId(dir, ver);
        if (id == null)
        {
            var full = $"{cfg.McVersion}-{ver}";
            var url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{full}/forge-{full}-installer.jar";
            await RunInstaller(url, $"forge-{full}-installer.jar", dir, cfg, progress, ct);
            id = FindVersionId(dir, ver) ?? throw new InvalidOperationException("Forge 安裝完成，但找不到版本資料夾（請看 loader-install.log）");
        }
        cfg.LoaderVersion = ver;
        return id;
    }

    // ---------- shared ----------
    static string? FindVersionId(string dir, string loaderVer)
    {
        var vd = Path.Combine(dir, "versions");
        if (!Directory.Exists(vd)) return null;
        string? loose = null;
        foreach (var d in Directory.GetDirectories(vd))
        {
            var name = Path.GetFileName(d);
            if (!name.Contains("forge", StringComparison.OrdinalIgnoreCase)) continue;      // forge / neoforge / 1.20.1-forge-47.x
            if (!File.Exists(Path.Combine(d, name + ".json"))) continue;
            // 嚴格比對：避免 21.1.5 誤中 21.1.50
            if (name.EndsWith("-" + loaderVer, StringComparison.OrdinalIgnoreCase) ||
                name.Contains("-" + loaderVer + "-", StringComparison.OrdinalIgnoreCase)) return name;
            if (name.Contains(loaderVer)) loose ??= name;                                     // 很舊的 Forge 命名備援
        }
        return loose;
    }

    static string FindJava(InstanceConfig cfg, string dir)
    {
        if (!string.IsNullOrWhiteSpace(cfg.JavaPath) && File.Exists(cfg.JavaPath)) return cfg.JavaPath;
        var rt = Path.Combine(dir, "runtime"); // Mojang runtime that CmlLib downloaded during vanilla install
        if (Directory.Exists(rt))
        {
            var j = Directory.EnumerateFiles(rt, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (j != null) return j;
        }
        var jh = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (jh != null) { var p = Path.Combine(jh, "bin", "java.exe"); if (File.Exists(p)) return p; }
        return "java";
    }

    static async Task RunInstaller(string url, string fileName, string dir, InstanceConfig cfg,
        IProgress<(string, double)>? progress, CancellationToken ct)
    {
        var jar = Path.Combine(AppPaths.Tools, fileName);
        if (!File.Exists(jar))
        {
            progress?.Report(("下載載入器安裝程式", 0));
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            var tmp = jar + ".part";
            await using (var fs = File.Create(tmp)) await res.Content.CopyToAsync(fs, ct);
            File.Move(tmp, jar, true);
        }

        // the official installer refuses to run without a launcher_profiles.json
        var lp = Path.Combine(dir, "launcher_profiles.json");
        if (!File.Exists(lp)) await File.WriteAllTextAsync(lp, "{\"profiles\":{}}", ct);

        var java = FindJava(cfg, dir);
        var psi = new ProcessStartInfo(java)
        {
            WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar"); psi.ArgumentList.Add(jar);
        psi.ArgumentList.Add("--installClient"); psi.ArgumentList.Add(dir);

        var log = new StringBuilder();
        log.AppendLine($"java: {java}\nargs: {string.Join(' ', psi.ArgumentList)}\n");
        using var proc = new Process { StartInfo = psi };
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (log) log.AppendLine(e.Data);
            progress?.Report(("安裝載入器：" + (e.Data.Length > 60 ? e.Data[..60] : e.Data), 0));
        }
        proc.OutputDataReceived += OnLine; proc.ErrorDataReceived += OnLine;

        try { proc.Start(); }
        catch (Exception ex) { throw new InvalidOperationException($"無法執行 Java（{java}）：{ex.Message}。請在實例設置指定 Java 路徑。"); }
        proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        string text; lock (log) text = log.ToString();
        await File.WriteAllTextAsync(Path.Combine(dir, "loader-install.log"), text, ct);
        if (proc.ExitCode != 0)
        {
            var tail = string.Join('\n', text.Split('\n').TakeLast(12));
            throw new InvalidOperationException($"載入器安裝失敗（exit {proc.ExitCode}）。完整記錄：{dir}\\loader-install.log\n{tail}");
        }
    }
}
