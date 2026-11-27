// ─────────────────────────────────────────────────────────────────────────────
// PluginExec.cs ── IEML 插件 2.0「exec（啟動外部程式）」權限的本體
//
// ★ 這是獨立檔案：沒有接進專案、沒有改任何現有程式碼。
//   只依賴 .NET 8 標準函式庫（不依賴 WPF、不依賴 IEML 其他類別），放進 IEML.Core/Plugins/ 即可編譯。
//
// 設計原則（預設拒絕）：
//   1. 插件不能給路徑／指令，只能指名 manifest 宣告過的 id。
//   2. 不經過 shell：UseShellExecute=false、參數用陣列（ArgumentList），不拼字串。
//   3. 執行前比對 sha256；檔案在雜湊到啟動期間被開啟鎖定，避免「檢查後被換掉」。
//   4. 逾時、輸出上限、同時只跑一個、不給 stdin、環境變數清空後只給最小集合。
//   5. 授權綁定「協議版本 + 插件版本 + exec 清單雜湊」，任何一項變動就要重新同意。
//
// 注意：exec 一旦授權，外部程式以使用者的 Windows 權限執行，沙盒管不到它。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IEML.Core.Plugins;

/// manifest 裡 "exec" 陣列的一項：插件允許執行的一個程式。
public sealed class ExecDecl
{
    // JSON 給 null 時 setter 會轉成空字串，Validate 才不會因 null 而崩潰（空字串會被格式檢查擋下）。
    string _id = "", _file = "", _sha = "", _desc = "";
    public string Id { get => _id; set => _id = value ?? ""; }                  // 插件呼叫時用的代號，例如 "ffmpeg"
    public string File { get => _file; set => _file = value ?? ""; }            // 相對於插件資料夾的 .exe 路徑，例如 "bin/ffmpeg.exe"
    public string Sha256 { get => _sha; set => _sha = value ?? ""; }            // 該檔案的 SHA-256（十六進位，64 字元）
    public string Description { get => _desc; set => _desc = value ?? ""; }     // 給使用者看的用途說明（協議視窗會顯示）

    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.Compiled);
    static readonly Regex ShaRx = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    /// 回傳錯誤訊息；null＝通過。
    public string? Validate()
    {
        if (!IdRx.IsMatch(Id)) return $"exec id 格式不正確：{Id}（小寫英數、底線、連字號，1–32 字）";
        if (string.IsNullOrWhiteSpace(File) || File.Length > 200) return $"exec「{Id}」的 file 必填且不超過 200 字";
        if (Path.IsPathRooted(File) || File.Contains("..") || File.Contains(':') || File.StartsWith('\\') || File.StartsWith('/'))
            return $"exec「{Id}」的 file 必須是插件資料夾內的相對路徑";
        if (!File.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return $"exec「{Id}」只允許 .exe";
        if (!ShaRx.IsMatch(Sha256)) return $"exec「{Id}」的 sha256 必須是 64 字元十六進位";
        if (Description.Length > 200) return $"exec「{Id}」的 description 太長（上限 200 字）";
        return null;
    }

    /// 驗證整份 exec 清單（數量、重複 id、各項格式）。
    public static string? ValidateAll(IReadOnlyList<ExecDecl> decls)
    {
        if (decls is null) return null;
        if (decls.Count > ExecLimits.MaxDecls) return $"exec 最多只能宣告 {ExecLimits.MaxDecls} 個程式";
        var seen = new HashSet<string>();
        foreach (var d in decls)
        {
            if (d is null) return "exec 內含空項目";
            var err = d.Validate();
            if (err is not null) return err;
            if (!seen.Add(d.Id)) return $"exec id 重複：{d.Id}";
        }
        return null;
    }
}

/// 執行限制（集中放在一處，方便審查）。
public static class ExecLimits
{
    public const int MaxDecls = 8;
    public const int MaxArgs = 32;
    public const int MaxArgLength = 1024;
    public const int MaxOutputChars = 64 * 1024;          // stdout、stderr 各自上限
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);
}

public sealed class ExecResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public bool TimedOut { get; init; }
    public bool OutputTruncated { get; init; }
}

/// 使用者對「某插件的 exec 權限」的同意紀錄（存進 plugins.json 的 PluginState 裡）。
public sealed class ExecConsent
{
    public int AgreementVersion { get; set; }       // 同意的協議版本
    public string PluginVersion { get; set; } = ""; // 同意當時的插件版本
    public string DeclsHash { get; set; } = "";     // 同意當時 exec 清單的雜湊
    public DateTime AcceptedAtUtc { get; set; }
}

public static class ExecPolicy
{
    /// 協議文字改了就把這個數字 +1，所有人都會被要求重新同意。
    public const int CurrentAgreementVersion = 1;

    /// exec 清單的穩定雜湊（依 id 排序；任何一項 id／file／sha256 變動都會改變結果）。
    public static string HashDecls(IEnumerable<ExecDecl> decls)
    {
        ArgumentNullException.ThrowIfNull(decls);
        var canonical = string.Join("\n", decls.Where(d => d is not null).OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => $"{d.Id}|{d.File.Replace('\\', '/').ToLowerInvariant()}|{d.Sha256.ToLowerInvariant()}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static ExecConsent CreateConsent(string pluginVersion, IEnumerable<ExecDecl> decls) => new()
    {
        AgreementVersion = CurrentAgreementVersion,
        PluginVersion = pluginVersion,
        DeclsHash = HashDecls(decls),
        AcceptedAtUtc = DateTime.UtcNow,
    };

    /// 同意紀錄是否仍然有效。協議、插件版本、exec 清單任何一項變了都回傳 false。
    public static bool IsConsentValid(ExecConsent? c, string pluginVersion, IEnumerable<ExecDecl> decls) =>
        c is not null
        && c.AgreementVersion == CurrentAgreementVersion
        && c.PluginVersion == pluginVersion
        && string.Equals(c.DeclsHash, HashDecls(decls), StringComparison.Ordinal);
}

public static class PluginExecRunner
{
    // 全域只允許同時執行一個外部程式（避免插件洗版、耗盡資源）。
    static readonly SemaphoreSlim Gate = new(1, 1);

    /// 執行插件宣告過的外部程式。呼叫前，呼叫端（PluginGate 那一層）必須已確認：
    ///   插件已啟用、manifest 有宣告 exec、使用者已勾選、同意紀錄仍有效（ExecPolicy.IsConsentValid）、
    ///   並且（建議）已向使用者做過「插件 X 要執行 Y」的確認。
    /// 這個方法本身只負責「安全地跑起來」。
    public static async Task<ExecResult> RunAsync(
        string pluginDir,
        IReadOnlyList<ExecDecl> declared,
        string execId,
        IReadOnlyList<string> args,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pluginDir);
        ArgumentNullException.ThrowIfNull(declared);
        args ??= Array.Empty<string>();

        // 1) 只能指名宣告過的 id
        var decl = declared.FirstOrDefault(d => d is not null && d.Id == execId)
            ?? throw new PluginDeniedException($"這個插件沒有宣告可執行的程式：{execId}");

        // 2) 驗證參數
        if (args.Count > ExecLimits.MaxArgs) throw new ArgumentException($"參數最多 {ExecLimits.MaxArgs} 個");
        foreach (var a in args)
        {
            if (a is null) throw new ArgumentException("參數不能是 null");
            if (a.Length > ExecLimits.MaxArgLength) throw new ArgumentException($"單一參數最長 {ExecLimits.MaxArgLength} 字");
            if (a.Contains('\0')) throw new ArgumentException("參數不能包含 NUL 字元");
        }

        var to = timeout ?? ExecLimits.DefaultTimeout;
        if (to <= TimeSpan.Zero || to > ExecLimits.MaxTimeout) to = ExecLimits.DefaultTimeout;

        // 3) 同時只跑一個：拿不到就直接拒絕，不排隊
        if (!await Gate.WaitAsync(0, CancellationToken.None))
            throw new InvalidOperationException("已有外部程式正在執行，請稍後再試");

        try
        {
            var exePath = ResolveInsidePluginDir(pluginDir, decl.File);

            // 4) 開啟並鎖定檔案（FileShare.Read：別人只能讀、不能寫／刪／換），再算雜湊，然後在鎖定狀態下啟動。
            using var lockStream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actual = SHA256.HashData(lockStream);
            var expected = Convert.FromHexString(decl.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                throw new PluginDeniedException($"「{decl.Id}」的檔案雜湊與 manifest 不符，已拒絕執行（檔案可能被更換）");

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,                 // 不經過 shell
                CreateNoWindow = true,
                WorkingDirectory = Path.GetFullPath(pluginDir),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);   // 陣列傳參數，沒有字串拼接、不會被注入

            // 環境變數：清空後只給最小集合，避免把使用者的環境（可能含金鑰）交給外部程式。
            psi.Environment.Clear();
            var sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            var temp = Path.GetTempPath();
            psi.Environment["SystemRoot"] = sysRoot;
            psi.Environment["PATH"] = Path.Combine(sysRoot, "System32");
            psi.Environment["TEMP"] = temp;
            psi.Environment["TMP"] = temp;

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) throw new InvalidOperationException("無法啟動程式");
            lockStream.Dispose();                        // 已啟動，可以放開鎖（Windows 對執行中的 exe 本來就不允許覆寫）

            try { proc.StandardInput.Close(); } catch { }   // 不給 stdin

            using var timeoutCts = new CancellationTokenSource(to);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            // 輸出要一直讀（即使超過上限也要丟掉繼續讀），否則子程序會因為管線塞滿而卡住。
            var outTask = ReadCappedAsync(proc.StandardOutput);
            var errTask = ReadCappedAsync(proc.StandardError);

            var timedOut = false;
            try { await proc.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                timedOut = timeoutCts.IsCancellationRequested;
                KillTree(proc);
                if (!timedOut) throw;                    // 是呼叫端取消，就把取消往上丟
            }

            // 最多再等 2 秒讓輸出讀完（孫程序握著管線不放的情況）
            await Task.WhenAny(Task.WhenAll(outTask, errTask), Task.Delay(TimeSpan.FromSeconds(2)));
            var (o, ot) = outTask.IsCompletedSuccessfully ? outTask.Result : ("", true);
            var (e, et) = errTask.IsCompletedSuccessfully ? errTask.Result : ("", true);

            return new ExecResult
            {
                ExitCode = timedOut ? -1 : SafeExitCode(proc),
                Stdout = o,
                Stderr = e,
                TimedOut = timedOut,
                OutputTruncated = ot || et,
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    // ───────────── 內部工具 ─────────────

    /// 解析 file 的實際路徑，確認：在插件資料夾內、存在、且路徑上沒有符號連結／連接點（避免繞出資料夾）。
    static string ResolveInsidePluginDir(string pluginDir, string relative)
    {
        var root = Path.GetFullPath(pluginDir);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new PluginDeniedException("程式路徑不在插件資料夾內");
        if (!File.Exists(full)) throw new FileNotFoundException("找不到宣告的程式檔案：" + relative);

        // 從檔案往上走到插件資料夾，任何一層是 ReparsePoint（symlink／junction）都拒絕。
        for (var p = full; p is not null && p.Length > root.Length; p = Path.GetDirectoryName(p))
        {
            var attr = File.GetAttributes(p);
            if ((attr & FileAttributes.ReparsePoint) != 0)
                throw new PluginDeniedException("程式路徑含有符號連結或連接點，已拒絕");
        }
        return full;
    }

    static async Task<(string Text, bool Truncated)> ReadCappedAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        var truncated = false;
        var buf = new char[4096];
        int n;
        while ((n = await reader.ReadAsync(buf.AsMemory())) > 0)
        {
            var room = ExecLimits.MaxOutputChars - sb.Length;
            if (room > 0) sb.Append(buf, 0, Math.Min(n, room));
            if (n > room) truncated = true;              // 超過的部分丟掉，但繼續讀完
        }
        return (sb.ToString(), truncated);
    }

    static void KillTree(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }

    static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
