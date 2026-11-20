using System.Security.Cryptography;
using CmlLib.Core.Auth.Microsoft;
namespace IEML.Core.Services;

/// 每個啟動器帳號一份登入快取。
/// 安全設計：登入快取（含 Microsoft／Xbox／Minecraft 權杖）平常只以「Windows DPAPI（目前使用者）加密」的 .dat 存在硬碟上，
/// 複製到 USB 或別台電腦／別的 Windows 使用者都解不開。登入時才解密成暫存檔給函式庫用，用完立刻重新加密並抹除暫存檔。
/// 限制：DPAPI 擋不住「同一個 Windows 使用者身分下執行的惡意程式」，也擋不住在你登入狀態下直接操作你電腦的人。
/// NOTE: written against CmlLib.Core.Auth.Microsoft 3.x from memory; fix names if the build complains.
public static class MicrosoftSessions
{
    static string Dir => Path.Combine(AppPaths.Root, "ms");
    // 暫存檔放本機（不會漫遊同步），預設只有目前使用者能讀
    static string TempDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InnovationEasyLauncher", "tmp");

    static string SealedPath(Guid id) => Path.Combine(Dir, $"{id:N}.dat");
    static string LegacyPath(Guid id) => Path.Combine(Dir, $"{id:N}.json");   // 舊版明文快取，會自動遷移並刪除

    public static bool HasCache(Guid id) => File.Exists(SealedPath(id)) || File.Exists(LegacyPath(id));

    /// 開啟登入：解密快取到暫存檔 → 建立 handler。務必用 using 包住，Dispose 時才會重新加密並刪掉暫存檔。
    public static LoginScope Open(Guid id)
    {
        Directory.CreateDirectory(Dir); Directory.CreateDirectory(TempDir);
        CleanupStale();
        var tmp = Path.Combine(TempDir, $"{Guid.NewGuid():N}.tmp");
        if (File.Exists(SealedPath(id)))
        {
            byte[]? plain = null;
            try { plain = Secrets.Unprotect(File.ReadAllBytes(SealedPath(id)), Secrets.LoginCache); File.WriteAllBytes(tmp, plain); }
            catch (CryptographicException)      // 換了電腦／Windows 使用者：解不開就當作沒有快取，重新登入
            { try { File.Delete(SealedPath(id)); } catch { } }
            finally { Secrets.Zero(plain); }    // 解密後的位元組用完就從記憶體抹除
        }
        else if (File.Exists(LegacyPath(id)))   // 舊版明文：搬進暫存檔，加密後把明文刪掉
        {
            File.Copy(LegacyPath(id), tmp, true);
        }
        var handler = new JELoginHandlerBuilder().WithAccountManager(tmp).Build();
        return new LoginScope(id, tmp, handler);
    }

    internal static void Seal(Guid id, string tmp)
    {
        try
        {
            if (File.Exists(tmp) && new FileInfo(tmp).Length > 2)    // 只有 "{}" 代表沒有登入狀態，不要蓋掉既有的好快取
            {
                var plain = File.ReadAllBytes(tmp);
                byte[] enc;
                try { enc = Secrets.Protect(plain, Secrets.LoginCache); } finally { Secrets.Zero(plain); }
                var dest = SealedPath(id);
                File.WriteAllBytes(dest + ".new", enc);
                File.Move(dest + ".new", dest, true);
                try { if (File.Exists(LegacyPath(id))) SecureDelete(LegacyPath(id)); } catch { }
            }
        }
        finally { SecureDelete(tmp); }
    }

    public static void DeleteCache(Guid id)
    {
        try { if (File.Exists(SealedPath(id))) File.Delete(SealedPath(id)); } catch { }
        try { if (File.Exists(LegacyPath(id))) SecureDelete(LegacyPath(id)); } catch { }
    }

    /// 先用 0 覆寫再刪除（盡力而為；SSD 的磨損平均機制無法保證抹除乾淨）。
    static void SecureDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var len = new FileInfo(path).Length;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write)) { fs.Write(new byte[Math.Max(1, len)]); fs.Flush(true); }
        }
        catch { }
        try { File.Delete(path); } catch { }
    }

    /// 上次當機可能留下的暫存檔（內含明文權杖）：一律抹除。
    static void CleanupStale()
    {
        try { foreach (var f in Directory.EnumerateFiles(TempDir, "*.tmp")) if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-2)) SecureDelete(f); }
        catch { }
    }
}

public sealed class LoginScope : IDisposable
{
    readonly Guid _id; readonly string _tmp; bool _done;
    public JELoginHandler Handler { get; }
    internal LoginScope(Guid id, string tmp, JELoginHandler handler) { _id = id; _tmp = tmp; Handler = handler; }
    /// 目前快取內容大小（位元組）；2 以下代表函式庫沒有存下登入狀態。
    public long CacheSize => File.Exists(_tmp) ? new FileInfo(_tmp).Length : -1;
    public void Dispose() { if (_done) return; _done = true; MicrosoftSessions.Seal(_id, _tmp); }
}
