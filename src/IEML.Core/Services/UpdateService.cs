using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace IEML.Core.Services;

public record UpdateRelease(string Tag, string Notes, string Name, string ZipUrl, string? Sha256, string? Sha256Url, string? SigUrl, string HtmlUrl);

/// 自動更新：查 GitHub 最新正式版 → 使用者同意 → 下載 → 驗證 SHA-256（以及選用的 ECDSA 簽章）→ 解壓 → 關閉啟動器、換檔、重新啟動。
/// 安全：只從官方倉庫的 Release 下載、網域白名單、驗證失敗就中止；沒有「靜默更新」，一定要使用者按同意。
/// 未編譯、未實測。
public static class UpdateService
{
    public const string Repo = "wayjehet/InnovationEasyMinecraftLauncher";

    /// 更新簽章公鑰（ECDSA P-256，SubjectPublicKeyInfo 的 Base64）。空字串＝尚未啟用簽章，只驗證 SHA-256。
    /// 設定方式見 docs/UPDATE.md。一旦填入，沒有有效簽章的更新一律拒絕。
    public const string PublicKeyBase64 = "";
    public static bool SignatureEnabled => PublicKeyBase64.Length > 0;

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher-Updater/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    static string WorkDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InnovationEasyLauncher", "update");

    /// 只取開頭的數字版本（1.2.1），後面的字母／後綴一律忽略：v1.2.1EF、1.2.1-beta、1.2.1+abc 都讀成 1.2.1。
    public static bool TryParseVersion(string s, out Version v)
    {
        var m = System.Text.RegularExpressions.Regex.Match((s ?? "").Trim(), @"^[vV]?(\d+(?:\.\d+){1,3})");
        if (m.Success && Version.TryParse(m.Groups[1].Value, out var parsed)) { v = parsed; return true; }
        v = new Version(0, 0); return false;
    }

    /// 有比目前新的正式版才回傳；沒有就回傳 null。（GitHub 的 releases/latest 不含草稿與預發佈版。）
    public static async Task<UpdateRelease?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        using var res = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var latest) || !TryParseVersion(currentVersion, out var cur) || latest <= cur) return null;

        string? zipName = null, zipUrl = null, digest = null;
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString() ?? "";
            var u = a.GetProperty("browser_download_url").GetString() ?? "";
            urls[n] = u;
            if (zipName is null && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipName = n; zipUrl = u;
                if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String) digest = d.GetString();
            }
        }
        if (zipName is null || zipUrl is null) throw new InvalidOperationException($"新版本 {tag} 沒有可下載的 zip 檔");
        string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
        return new UpdateRelease(tag, r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", Path.GetFileName(zipName), zipUrl, sha,
            urls.GetValueOrDefault(zipName + ".sha256"), urls.GetValueOrDefault(zipName + ".sig"), r.GetProperty("html_url").GetString() ?? "");
    }

    /// 下載 → 驗證 → 解壓到暫存資料夾。回傳解壓後的資料夾。任何一步失敗都會丟出例外（並清掉已下載的檔案）。
    public static async Task<string> DownloadAndVerifyAsync(UpdateRelease u, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var safeTag = string.Concat(u.Tag.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-'));
        var dir = Path.Combine(WorkDir, safeTag.Length == 0 ? "new" : safeTag);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, u.Name);
        try
        {
            await FetchAsync(u.ZipUrl, zipPath, progress, ct);

            // 1) SHA-256：優先用 GitHub API 的 digest，其次 .sha256 檔
            var want = u.Sha256;
            if (want is null && u.Sha256Url is not null)
                want = (await FetchStringAsync(u.Sha256Url, ct)).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
            if (want is null && !SignatureEnabled) throw new InvalidDataException("新版本沒有可驗證的 SHA-256，已拒絕更新。");
            if (want is not null)
            {
                string got;
                await using (var fs = File.OpenRead(zipPath)) got = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
                if (got != want) throw new InvalidDataException("更新檔的 SHA-256 不符，已刪除，請稍後再試。");
            }

            // 2) 簽章（有設定公鑰才啟用）
            if (SignatureEnabled)
            {
                if (u.SigUrl is null) throw new InvalidDataException("新版本沒有簽章，已拒絕更新。");
                var sig = await FetchBytesAsync(u.SigUrl, ct);
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
                await using var fs = File.OpenRead(zipPath);
                if (!ecdsa.VerifyData(fs, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    throw new InvalidDataException("更新檔簽章驗證失敗，已拒絕更新。");
            }

            // 3) 解壓（防 Zip Slip）
            var outDir = Path.Combine(dir, "files");
            ExtractSafe(zipPath, outDir);
            if (!File.Exists(Path.Combine(outDir, "InnovationEasyMinecraftLauncher.exe")))
                throw new InvalidDataException("更新檔裡找不到 InnovationEasyMinecraftLauncher.exe");
            return outDir;
        }
        catch { try { Directory.Delete(dir, true); } catch { } throw; }
    }

    static void ExtractSafe(string zipPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var root = Path.GetFullPath(outDir) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
            var target = Path.GetFullPath(Path.Combine(outDir, e.FullName.Replace('\\', '/')));
            if (!target.StartsWith(root)) throw new InvalidDataException("更新檔含有不安全的路徑：" + e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, true);
        }
    }

    // ---------- 下載（網域白名單，轉址後也檢查）----------
    static async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var uri = new Uri(url); DownloadPolicy.Require(uri);
        var res = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        try { res.EnsureSuccessStatusCode(); DownloadPolicy.Require(res.RequestMessage?.RequestUri ?? uri); }
        catch { res.Dispose(); throw; }
        return res;
    }

    static async Task FetchAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        using var res = await GetAsync(url, ct);
        var total = res.Content.Headers.ContentLength;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(dest);
        var buf = new byte[81920]; long got = 0; int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            got += n;
            if (total is > 0) progress?.Report(100.0 * got / total.Value);
        }
    }

    static async Task<string> FetchStringAsync(string url, CancellationToken ct)
    { using var res = await GetAsync(url, ct); return await res.Content.ReadAsStringAsync(ct); }

    static async Task<byte[]> FetchBytesAsync(string url, CancellationToken ct)
    { using var res = await GetAsync(url, ct); return await res.Content.ReadAsByteArrayAsync(ct); }

    // ---------- 換檔並重新啟動 ----------
    /// 啟動一個背景 PowerShell：等啟動器關閉 → 備份舊 exe → 複製新檔 → 失敗就還原 → 重新啟動。呼叫後請立刻關閉啟動器。
    public static void ApplyAndRestart(string newFilesDir)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("找不到目前執行檔的位置");
        var dst = Path.GetDirectoryName(exe)!;
        var script = Path.Combine(Path.GetDirectoryName(newFilesDir)!, "apply-update.ps1");
        File.WriteAllText(script, ApplyScript, new UTF8Encoding(true));
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                                  "-ParentPid", Environment.ProcessId.ToString(), "-Src", newFilesDir, "-Dst", dst, "-Exe", exe })
            psi.ArgumentList.Add(a);        // 全部用 ArgumentList，路徑不進腳本文字
        Process.Start(psi);
    }

    /// 上一次更新留下的 .old 備份檔；啟動時順手清掉。
    public static void CleanupOld()
    {
        try
        {
            var exe = Environment.ProcessPath; if (exe is null) return;
            var old = exe + ".old"; if (File.Exists(old)) File.Delete(old);
        }
        catch { }
    }

    const string ApplyScript = @"param([int]$ParentPid, [string]$Src, [string]$Dst, [string]$Exe)
$ErrorActionPreference = 'Stop'
$log = Join-Path (Split-Path -Parent $Src) 'apply-update.log'
try {
    for ($i = 0; $i -lt 60; $i++) {
        if (-not (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 500
    }
    $bak = $Exe + '.old'
    if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Force }
    Move-Item -LiteralPath $Exe -Destination $bak -Force
    try { Copy-Item -Path (Join-Path $Src '*') -Destination $Dst -Recurse -Force }
    catch { Move-Item -LiteralPath $bak -Destination $Exe -Force; throw }
    Add-Content -LiteralPath $log ('OK ' + (Get-Date))
} catch {
    Add-Content -LiteralPath $log ('FAILED ' + $_.Exception.Message)
}
Start-Process -FilePath $Exe
";
}

/// 更新偏好：是否自動檢查、上次檢查時間、略過的版本。存在 %AppData%\InnovationEasyLauncher\update.json
public static class UpdateSettings
{
    class Store { public bool AutoCheck { get; set; } = true; public DateTime LastCheckUtc { get; set; } = DateTime.MinValue; public string? SkipTag { get; set; } }
    static string FilePath => Path.Combine(AppPaths.Root, "update.json");
    static Store Load() { try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new() : new(); } catch { return new(); } }
    static void Save(Store s) { try { File.WriteAllText(FilePath, JsonSerializer.Serialize(s)); } catch { } }

    public static bool AutoCheck { get => Load().AutoCheck; set { var s = Load(); s.AutoCheck = value; Save(s); } }
    public static DateTime LastCheckUtc => Load().LastCheckUtc;
    public static string? SkipTag { get => Load().SkipTag; set { var s = Load(); s.SkipTag = value; Save(s); } }
    public static void MarkChecked() { var s = Load(); s.LastCheckUtc = DateTime.UtcNow; Save(s); }
}
