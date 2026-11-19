using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace IEML.Core.Services;

public record TerracottaProfile(string Name, string Kind);

/// Terracotta /state 的一筆回應。State：waiting / host-scanning / host-starting / host-ok /
/// guest-connecting / guest-starting / guest-ok / exception / stopped（程式自己加的，代表連不上陶瓦行程）
public record TerracottaState(string State, int Index, string? Room, string? Url, string? Difficulty, int? ExceptionType, IReadOnlyList<TerracottaProfile> Profiles);

/// 陶瓦聯機（Terracotta，© burningtnt，AGPL-3.0-or-later，附 AGPL 例外條款）整合。
/// 依官方 HTTP API 文件（v0.4.2）：本程式只下載「未修改的官方執行檔」並當成獨立行程啟動，透過本機 HTTP API 互動，
/// 不連結進本程式；依例外條款，介面上必須明顯標示陶瓦聯機的版權資訊（TerracottaWindow 已標示，請勿移除）。
/// 好處：房間碼（U/XXXX-XXXX-XXXX-XXXX）與 HMCL、PCL-CE 等互通。未編譯、未實測。
public sealed class TerracottaService
{
    static readonly HttpClient Dl = CreateDl();
    static HttpClient CreateDl()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    static string Dir => Path.Combine(AppPaths.Tools, "terracotta");
    static string PortFile => Path.Combine(Dir, "port.json");

    HttpClient? _api;
    Process? _proc;
    CancellationTokenSource? _poll;
    int _maxIndex = -1;

    public event Action<TerracottaState>? StateChanged;
    public event Action<string>? StatusChanged;       // 下載、啟動等進度文字
    public TerracottaState? Current { get; private set; }
    public bool IsRunning => _api is not null && _poll is { IsCancellationRequested: false };

    void Report(string s) => StatusChanged?.Invoke(s);

    // ---------- 房間碼 ----------
    public static bool IsValidRoomCode(string? s) => !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s.Trim(), @"^U/[A-Za-z0-9]{4}(-[A-Za-z0-9]{4}){3}$");

    // ---------- 啟動 / 停止 ----------

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        CleanupStale();   // 上一次的陶瓦異常結束（stopped）時，先清掉殘留的連線、輪詢與行程
        var exe = FindExe() ?? await DownloadAsync(ct);

        Directory.CreateDirectory(Dir);
        if (File.Exists(PortFile)) File.Delete(PortFile);
        Report("啟動陶瓦聯機…");
        var psi = new ProcessStartInfo(exe, $"--hmcl \"{PortFile}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        _proc = Process.Start(psi);

        // 官方文件：就緒後會把 {"port": N} 寫進指定檔案（Windows 下最久等 8 秒）
        int port = 0;
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(15))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(PortFile))
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(PortFile, ct));
                    if (doc.RootElement.TryGetProperty("port", out var p) && p.TryGetInt32(out port) && port > 0) break;
                }
            }
            catch (JsonException) { /* 檔案可能寫到一半，下一輪再讀 */ }
            catch (IOException) { }
            await Task.Delay(150, ct);
        }
        if (port <= 0) throw new InvalidOperationException("陶瓦聯機沒有在時限內啟動（可能被防毒或防火牆擋住）。");

        _api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        _maxIndex = -1;
        _poll = new CancellationTokenSource();
        _ = Task.Run(() => PollAsync(_poll.Token));
        Report("陶瓦聯機已啟動");
    }

    /// 清掉舊的 HTTP 連線、輪詢與行程（不呼叫 API，因為這時陶瓦多半已經不在了）。
    void CleanupStale()
    {
        try { _poll?.Cancel(); } catch { }
        _poll?.Dispose(); _poll = null;
        _api?.Dispose(); _api = null;
        try { if (_proc is { HasExited: false }) _proc.Kill(); } catch { }
        _proc?.Dispose(); _proc = null;
    }

    async Task PollAsync(CancellationToken ct)
    {
        int fails = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await _api!.GetStringAsync("/state", ct);
                fails = 0;
                var st = Parse(json);
                if (st is not null && st.Index > _maxIndex)   // 官方建議：只處理 index 變大的回應
                {
                    _maxIndex = st.Index;
                    Current = st;
                    StateChanged?.Invoke(st);
                }
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // 官方建議：連續失敗 5 次可視為陶瓦行程已結束
                if (++fails >= 5)
                {
                    var dead = new TerracottaState("stopped", _maxIndex + 1, null, null, null, null, Array.Empty<TerracottaProfile>());
                    _maxIndex = dead.Index; Current = dead; StateChanged?.Invoke(dead);
                    _poll?.Cancel();
                    return;
                }
            }
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
        }
    }

    static TerracottaState? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int index = r.TryGetProperty("index", out var i) && i.TryGetInt32(out var iv) ? iv : 0;
            int? type = r.TryGetProperty("type", out var t) && t.TryGetInt32(out var tv) ? tv : null;
            var profiles = new List<TerracottaProfile>();
            if (r.TryGetProperty("profiles", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray())
                    profiles.Add(new TerracottaProfile(
                        p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        p.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : ""));
            return new TerracottaState(S("state") ?? "waiting", index, S("room"), S("url"), S("difficulty"), type, profiles);
        }
        catch { return null; }
    }

    // ---------- 控制 ----------

    static string Q(string? v) => Uri.EscapeDataString(v ?? "");

    /// 以房主身份開房。房主需先進入世界並對區域網路開放，陶瓦會自動掃描。
    public async Task StartHostAsync(string? player, CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);
        using var res = await _api!.GetAsync($"/state/scanning?player={Q(player)}", ct);
        res.EnsureSuccessStatusCode();
    }

    /// 以加入方身份加入房間。邀請碼格式錯誤時回傳 400。
    public async Task StartGuestAsync(string room, string? player, CancellationToken ct = default)
    {
        room = room.Trim();
        if (!IsValidRoomCode(room)) throw new ArgumentException("房間碼格式不正確，應為 U/XXXX-XXXX-XXXX-XXXX。");
        await EnsureStartedAsync(ct);
        using var res = await _api!.GetAsync($"/state/guesting?room={Q(room)}&player={Q(player)}", ct);
        if ((int)res.StatusCode == 400) throw new InvalidOperationException("陶瓦拒絕了這個請求：房間碼無效，或目前不在待機狀態（先按「返回待機」）。");
        res.EnsureSuccessStatusCode();
    }

    /// 回到待機（離開房間、清掉異常狀態）。
    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_api is null) return;
        using var res = await _api.GetAsync("/state/ide", ct);
        res.EnsureSuccessStatusCode();
    }

    /// 結束陶瓦行程（啟動器關閉時呼叫）。
    public async Task StopAsync()
    {
        _poll?.Cancel();
        try
        {
            if (_api is not null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var _ = await _api.GetAsync("/panic?peaceful=true", cts.Token);
            }
        }
        catch { }
        _api?.Dispose(); _api = null;
        try { if (_proc is { HasExited: false }) _proc.Kill(); } catch { }
        _proc = null; Current = null;
    }

    public void StopBlocking() { try { StopAsync().Wait(TimeSpan.FromSeconds(4)); } catch { } }

    // ---------- 下載（未修改的官方執行檔） ----------

    static string? FindExe()
    {
        if (!Directory.Exists(Dir)) return null;
        return Directory.EnumerateFiles(Dir, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("terracotta", StringComparison.OrdinalIgnoreCase));
    }

    // 注意：我沒有確認 Release 附件的檔名格式，所以用「檔名含 windows 與架構」去挑，並接受 .exe 或 .zip。
    // 找不到或格式不認得時會提示使用者手動把官方 Windows 版放進資料夾。
    async Task<string> DownloadAsync(CancellationToken ct)
    {
        Report("第一次使用，正在下載陶瓦聯機…");
        using var res = await Dl.GetAsync("https://api.github.com/repos/burningtnt/Terracotta/releases/latest", ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? new[] { "arm64", "aarch64" } : new[] { "x86_64", "x64", "amd64" };
        string? url = null, name = null, digest = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString() ?? "";
            var l = n.ToLowerInvariant();
            if (!l.Contains("windows") || l.Contains("android") || !arch.Any(l.Contains)) continue;
            if (!(l.EndsWith(".exe") || l.EndsWith(".zip"))) continue;
            url = a.GetProperty("browser_download_url").GetString(); name = n;
            if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String) digest = d.GetString();
            break;
        }
        if (url is null || name is null)
            throw new InvalidOperationException($"找不到適合的陶瓦聯機 Windows 版下載檔。請到 github.com/burningtnt/Terracotta/releases 下載 Windows 版，解壓後把 terracotta*.exe 放進：{Dir}");

        Directory.CreateDirectory(Dir);
        var tmp = Path.Combine(Dir, "download.tmp");
        try
        {
            await using (var net = await Dl.GetStreamAsync(url, ct))
            await using (var fs = File.Create(tmp))
                await net.CopyToAsync(fs, ct);

            if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var fs = File.OpenRead(tmp);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!hash.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("陶瓦聯機下載檔的 SHA256 不符，已中止。");
            }

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(tmp, Dir, overwriteFiles: true);
            else File.Copy(tmp, Path.Combine(Dir, "terracotta.exe"), overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }

        return FindExe() ?? throw new InvalidOperationException($"下載完成但找不到 terracotta*.exe，請手動放進：{Dir}");
    }
}
