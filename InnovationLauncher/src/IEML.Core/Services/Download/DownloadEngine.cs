using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
namespace IEML.Core.Services.Download;

internal sealed class DownloadHttpException : Exception
{
    public HttpStatusCode Status { get; }
    public TimeSpan? RetryAfter { get; }
    public DownloadHttpException(HttpStatusCode status, TimeSpan? retryAfter) : base($"HTTP {(int)status}")
    { Status = status; RetryAfter = retryAfter; }
}

/// Downloads one file to "<dest>.part", resumes with HTTP Range when a .part exists,
/// verifies SHA-1 (if given), then renames to dest. Throws on any problem; retry policy lives in DownloadManager.
internal static class DownloadEngine
{
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        return c;
    }

    public static string Sha1Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }

    public static async Task RunAsync(DownloadTask t, CancellationToken ct)
    {
        var dest = Path.GetFullPath(t.Dest);
        var part = dest + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        // already have a verified copy -> done
        if (t.Sha1 != null && File.Exists(dest) && Sha1Of(dest) == t.Sha1.ToLowerInvariant())
        {
            t.BytesTotal = t.BytesDone = new FileInfo(dest).Length;
            return;
        }

        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var req = new HttpRequestMessage(HttpMethod.Get, t.Url);
        if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);

        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(part);   // bad resume point -> next attempt starts clean
            throw new IOException("續傳位置無效，重新下載");
        }
        if (!res.IsSuccessStatusCode)
        {
            var ra = res.Headers.RetryAfter;
            TimeSpan? wait = ra?.Delta;
            if (wait is null && ra?.Date is { } d) wait = d - DateTimeOffset.UtcNow;
            throw new DownloadHttpException(res.StatusCode, wait);
        }

        bool resumed = have > 0 && res.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed) have = 0;                     // server ignored Range -> overwrite from zero
        long? len = res.Content.Headers.ContentLength;
        t.BytesTotal = len is null ? -1 : len.Value + have;
        t.BytesDone = have;

        long done = have;
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var fs = new FileStream(part, resumed ? FileMode.Append : FileMode.Create,
                         FileAccess.Write, FileShare.Read, 81920, true))
        {
            var buf = new byte[81920];
            var sw = Stopwatch.StartNew();
            long lastMs = 0, lastBytes = done;
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                int n;
                try { n = await src.ReadAsync(buf, stall.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new IOException("下載逾時（長時間沒有資料）"); }
                if (n == 0) break;
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                var ms = sw.ElapsedMilliseconds;
                if (ms - lastMs >= 250)
                {
                    t.SpeedBps = (done - lastBytes) * 1000.0 / (ms - lastMs);
                    t.BytesDone = done; lastBytes = done; lastMs = ms;
                }
            }
        }
        t.BytesDone = done; t.SpeedBps = 0;

        if (t.BytesTotal > 0 && done != t.BytesTotal) throw new IOException("下載不完整");   // retry resumes from .part
        if (t.Sha1 != null && Sha1Of(part) != t.Sha1.ToLowerInvariant())
        {
            File.Delete(part);
            throw new InvalidDataException("檢查碼不符");
        }
        File.Move(part, dest, true);
    }
}
