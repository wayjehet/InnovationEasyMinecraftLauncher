using System.Diagnostics;
using System.Text;
namespace IEML.Core.Services;

/// 遊戲輸出記錄（記憶體內，最多保留最近 5000 行）。
/// LaunchService 啟動遊戲時呼叫 Start()，stdout / stderr 會進來；UI 訂閱 LineAdded 即時顯示。
/// 存取權杖等機密字串會在寫入前被遮蔽為 ***，不會出現在記錄裡。
public static class GameLog
{
    const int Max = 5000;
    static readonly object Gate = new();
    static readonly LinkedList<string> Buf = new();
    static readonly List<string> Secrets = new();

    /// 事件可能在任何執行緒觸發，UI 端請自行切回 UI 執行緒。
    public static event Action<string>? LineAdded;

    public static IReadOnlyList<string> Snapshot() { lock (Gate) return Buf.ToList(); }
    public static void Clear() { lock (Gate) Buf.Clear(); }

    public static void Append(string line)
    {
        string masked;
        lock (Gate)
        {
            masked = line;
            foreach (var s in Secrets) masked = masked.Replace(s, "***");
            Buf.AddLast(masked);
            while (Buf.Count > Max) Buf.RemoveFirst();
        }
        LineAdded?.Invoke(masked);
    }

    /// 設定轉向、啟動行程並開始讀取輸出。取代直接呼叫 process.Start()。
    public static void Start(Process p, string instanceName, params string?[] secrets)
    {
        lock (Gate)
            foreach (var s in secrets)
                if (!string.IsNullOrEmpty(s) && s.Length >= 8 && !Secrets.Contains(s)) Secrets.Add(s);

        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        p.EnableRaisingEvents = true;
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Append(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Append(e.Data); };
        p.Exited += (_, _) =>
        {
            string code; try { code = p.ExitCode.ToString(); } catch { code = "?"; }
            Append($"=== 遊戲已結束（結束碼 {code}）===");
        };

        Append($"=== 啟動 {instanceName}  {DateTime.Now:HH:mm:ss} ===");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
    }
}
