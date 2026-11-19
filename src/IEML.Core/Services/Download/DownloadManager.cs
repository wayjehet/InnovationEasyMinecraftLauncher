using System.Net;
using System.Net.Http;
namespace IEML.Core.Services.Download;

/// Single place for all downloads we start ourselves: global parallel limit, retry policy,
/// pause / resume / cancel. (CmlLib's own vanilla downloads and the Forge/NeoForge installer's
/// library downloads cannot go through here.)
public sealed class DownloadManager
{
    public static DownloadManager Instance { get; } = new();

    internal sealed class Settings { public int MaxParallel { get; set; } = 4; public int MaxRetries { get; set; } = 3; }
    static string SettingsPath => Path.Combine(AppPaths.Root, "download.json");

    readonly object _lock = new();
    readonly List<DownloadTask> _tasks = new();
    int _running;

    public int MaxParallel { get; private set; } = 4;
    public int MaxRetries { get; private set; } = 3;

    public event Action<DownloadTask>? TaskAdded;
    public event Action<DownloadTask>? TaskChanged;   // state changed (progress uses the task's PropertyChanged)
    public event Action? TasksCleared;

    DownloadManager()
    {
        try
        {
            var s = JsonStore.ReadAsync(SettingsPath, () => new Settings()).GetAwaiter().GetResult();
            MaxParallel = Math.Clamp(s.MaxParallel, 1, 16);
            MaxRetries = Math.Clamp(s.MaxRetries, 0, 10);
        }
        catch { }
    }

    public async Task SetLimitsAsync(int maxParallel, int maxRetries)
    {
        MaxParallel = Math.Clamp(maxParallel, 1, 16);
        MaxRetries = Math.Clamp(maxRetries, 0, 10);
        await JsonStore.WriteAsync(SettingsPath, new Settings { MaxParallel = MaxParallel, MaxRetries = MaxRetries });
        Pump();
    }

    public IReadOnlyList<DownloadTask> Snapshot() { lock (_lock) return _tasks.ToList(); }
    public int RunningCount { get { lock (_lock) return _tasks.Count(t => t.State == DownloadState.Running); } }
    public double TotalSpeedBps { get { lock (_lock) return _tasks.Where(t => t.State == DownloadState.Running).Sum(t => t.SpeedBps); } }

    // ---------- API for services ----------
    public DownloadTask Enqueue(string url, string dest, string title, string source, string? sha1 = null, string? group = null)
    {
        var t = new DownloadTask(url, dest, title, source, sha1, group);
        lock (_lock) _tasks.Add(t);
        TaskAdded?.Invoke(t);
        Pump();
        return t;
    }

    /// Enqueue and wait. Throws on failure, OperationCanceledException on cancel. Pausing just keeps it waiting.
    public async Task DownloadAsync(string url, string dest, string title, string source,
        string? sha1 = null, string? group = null, CancellationToken ct = default)
    {
        var t = Enqueue(url, dest, title, source, sha1, group);
        using var reg = ct.Register(() => Cancel(t));
        await t.Completion;
    }

    // ---------- controls (for the UI) ----------
    public void Pause(DownloadTask t)
    {
        bool cancelRun = false;
        lock (_lock)
        {
            if (t.State == DownloadState.Queued) t.State = DownloadState.Paused;
            else if (t.State == DownloadState.Running) { t.Intent = DownloadIntent.Pause; cancelRun = true; }
            else return;
        }
        if (cancelRun) t.Cts?.Cancel(); else TaskChanged?.Invoke(t);
    }

    public void Resume(DownloadTask t)
    {
        lock (_lock) { if (t.State != DownloadState.Paused) return; t.State = DownloadState.Queued; }
        TaskChanged?.Invoke(t);
        Pump();
    }

    public void Cancel(DownloadTask t)
    {
        bool cancelRun = false;
        lock (_lock)
        {
            if (t.State == DownloadState.Running) { t.Intent = DownloadIntent.Cancel; cancelRun = true; }
            else if (t.State is DownloadState.Queued or DownloadState.Paused) { t.State = DownloadState.Canceled; DeletePart(t); t.Tcs.TrySetCanceled(); }
            else return;
        }
        if (cancelRun) t.Cts?.Cancel(); else { TaskChanged?.Invoke(t); Pump(); }
    }

    /// Failed tasks can't be revived (their Completion already faulted), so this queues a fresh copy.
    public DownloadTask? Retry(DownloadTask t)
    {
        if (t.State is not (DownloadState.Failed or DownloadState.Canceled)) return null;
        lock (_lock) _tasks.Remove(t);
        return Enqueue(t.Url, t.Dest, t.Title, t.Source, t.Sha1, t.Group);
    }

    public void PauseAll() { foreach (var t in Snapshot()) Pause(t); }
    public void ResumeAll() { foreach (var t in Snapshot()) Resume(t); }

    public void ClearFinished()
    {
        lock (_lock) _tasks.RemoveAll(t => t.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled);
        TasksCleared?.Invoke();
    }

    // ---------- scheduler ----------
    void Pump()
    {
        var start = new List<DownloadTask>();
        lock (_lock)
        {
            foreach (var t in _tasks)
            {
                if (_running >= MaxParallel) break;
                if (t.State != DownloadState.Queued) continue;
                _running++; t.State = DownloadState.Running; t.Intent = DownloadIntent.None;
                t.Cts = new CancellationTokenSource(); t.Error = null;
                start.Add(t);
            }
        }
        foreach (var t in start) { TaskChanged?.Invoke(t); _ = Task.Run(() => RunAsync(t)); }
    }

    async Task RunAsync(DownloadTask t)
    {
        var cts = t.Cts!;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                t.Attempt = attempt;
                try
                {
                    await DownloadEngine.RunAsync(t, cts.Token);
                    t.Error = null; t.SpeedBps = 0; t.State = DownloadState.Completed; t.Tcs.TrySetResult();
                    return;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { OnStopped(t); return; }
                catch (Exception ex) when (attempt < MaxRetries && IsRetryable(ex, out var wait))
                {
                    t.SpeedBps = 0;
                    t.Error = $"{Describe(ex)}（重試 {attempt + 1}/{MaxRetries}）";
                    try { await Task.Delay(wait ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cts.Token); }
                    catch (OperationCanceledException) { OnStopped(t); return; }
                }
                catch (Exception ex)
                {
                    t.SpeedBps = 0; t.Error = Describe(ex); t.State = DownloadState.Failed;
                    t.Tcs.TrySetException(new IOException($"{t.Title}：{t.Error}", ex));
                    return;
                }
            }
        }
        finally
        {
            lock (_lock) _running--;
            TaskChanged?.Invoke(t);
            Pump();
        }
    }

    void OnStopped(DownloadTask t)
    {
        t.SpeedBps = 0;
        if (t.Intent == DownloadIntent.Pause) { t.State = DownloadState.Paused; return; }   // .part is kept for resume
        t.State = DownloadState.Canceled; DeletePart(t); t.Tcs.TrySetCanceled();
    }

    static void DeletePart(DownloadTask t)
    {
        try { var p = Path.GetFullPath(t.Dest) + ".part"; if (File.Exists(p)) File.Delete(p); } catch { }
    }

    /// 429 honours Retry-After; 5xx, network errors, truncated downloads and bad hashes are retried; 4xx (403, 404…) are not.
    static bool IsRetryable(Exception ex, out TimeSpan? wait)
    {
        wait = null;
        switch (ex)
        {
            case DownloadHttpException h:
                var c = (int)h.Status;
                if (c == 429)
                {
                    wait = h.RetryAfter is { } r && r > TimeSpan.Zero ? TimeSpan.FromSeconds(Math.Min(r.TotalSeconds, 60)) : TimeSpan.FromSeconds(5);
                    return true;
                }
                return c >= 500;
            case HttpRequestException:
            case IOException:
            case InvalidDataException:
                return true;
            default:
                return false;
        }
    }

    static string Describe(Exception ex) => ex switch
    {
        DownloadHttpException { Status: HttpStatusCode.Forbidden } => "HTTP 403（來源拒絕下載）",
        DownloadHttpException { Status: HttpStatusCode.NotFound } => "HTTP 404（檔案不存在）",
        DownloadHttpException h => $"HTTP {(int)h.Status}",
        InvalidDataException => "檢查碼不符",
        _ => ex.Message
    };
}
