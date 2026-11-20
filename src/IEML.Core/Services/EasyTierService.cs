using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IEML.Core.Contracts;
namespace IEML.Core.Services;

/// EasyTier 連線房間。
/// - 第一次使用時從 GitHub Releases 下載 easytier-core 到 tools\easytier\
/// - 建立虛擬網卡需要管理員權限：以 UAC (runas) 啟動一支 PowerShell 監看腳本，由它啟動 easytier-core
///   （未提升權限的程式無法結束已提升權限的行程，所以停止／離開時靠「停止旗標檔」與「父行程 PID 監看」）
/// - 虛擬 IP 透過 easytier-cli（不需管理員）查詢
public sealed class EasyTierService : IEasyTierService
{
    /// 官方公共節點。可在 CreateRoom 回傳的 EasyTierRoom.PeerUrl 換成自己的節點。
    public const string DefaultPeer = "tcp://public.easytier.cn:11010";

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("InnovationEasyLauncher/0.1");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    static string Dir => Path.Combine(AppPaths.Tools, "easytier");
    static string ScriptPath => Path.Combine(Dir, "easytier-run.ps1");
    static string ArgsPath => Path.Combine(Dir, "room.args");
    static string StopPath => Path.Combine(Dir, "stop.flag");

    Process? _wrapper;
    string? _cli;
    string? _rpc;
    bool _running;

    /// 狀態文字（下載中、等待授權…），事件可能在任何執行緒觸發，UI 端請自行切回 UI 執行緒。
    public event Action<string>? StatusChanged;
    public EasyTierRoom? CurrentRoom { get; private set; }
    public string? VirtualIp { get; private set; }
    public bool IsRunning => _running && !WrapperExited();

    void Report(string s) => StatusChanged?.Invoke(s);

    bool WrapperExited()
    {
        try { return _wrapper is null || _wrapper.HasExited; }
        catch { return false; } // 提升權限的行程可能查不到狀態，視為仍在執行
    }

    // ---------- 房號 ----------

    const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    static string Rand(int n)
    {
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++) sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        return sb.ToString();
    }

    public EasyTierRoom CreateRoom() => new("ieml-" + Rand(8), Rand(12), DefaultPeer);

    /// 邀請碼格式：房號:密碼
    public static string ToInviteCode(EasyTierRoom r) => $"{r.NetworkName}:{r.Secret}";

    public static bool TryParseInviteCode(string? text, out EasyTierRoom room)
    {
        room = null!;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return false;
        var i = t.IndexOf(':');
        if (i <= 0 || i >= t.Length - 1) return false;
        var name = t[..i].Trim();
        var secret = t[(i + 1)..].Trim();
        // 只允許英數、底線、減號：這些值會放進行程參數，不接受其他字元
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_-]{1,64}$") || !Regex.IsMatch(secret, @"^[A-Za-z0-9_-]{1,64}$")) return false;
        room = new EasyTierRoom(name, secret, DefaultPeer);
        return true;
    }

    // ---------- 啟動 / 停止 ----------

    public async Task<string> StartAsync(EasyTierRoom room, CancellationToken ct = default)
    {
        if (IsRunning) throw new InvalidOperationException("已在連線中，請先停止目前的房間。");
        if (!Regex.IsMatch(room.NetworkName, @"^[A-Za-z0-9_-]{1,64}$") || !Regex.IsMatch(room.Secret, @"^[A-Za-z0-9_-]{1,64}$"))
            throw new ArgumentException("房號或密碼格式不正確（只能用英數、底線、減號）。");
        var peer = string.IsNullOrWhiteSpace(room.PeerUrl) ? DefaultPeer : room.PeerUrl!;
        if (!Regex.IsMatch(peer, @"^(tcp|udp|ws|wss|wg|quic)://[A-Za-z0-9.\-:\[\]]+$"))
            throw new ArgumentException("節點網址格式不正確。");

        var (core, cli) = await EnsureBinariesAsync(ct);
        _cli = cli;

        var port = FreePort();
        _rpc = $"127.0.0.1:{port}";

        Directory.CreateDirectory(Dir);
        await File.WriteAllTextAsync(ScriptPath, RunScript, new UTF8Encoding(true), ct);
        // 參數檔：每行一個參數；腳本讀完立刻刪除，避免密碼留在磁碟上。
        await File.WriteAllLinesAsync(ArgsPath, new[]
        {
            "--network-name", room.NetworkName,
            "--network-secret", room.Secret,
            "--peers", peer,
            "--rpc-portal", _rpc,
            "--dhcp",
        }, ct);
        if (File.Exists(StopPath)) File.Delete(StopPath);

        Report("請在彈出的視窗按「是」，授權建立虛擬網卡…");
        // 以管理員身分執行的指令列：要放進引號的路徑不得含引號或換行（Windows 路徑本來就不會有，這裡再擋一次）
        foreach (var v in new[] { ScriptPath, core, ArgsPath, StopPath })
            if (v.Contains('"') || v.Contains('\n') || v.Contains('\r')) throw new InvalidOperationException("路徑含有不安全字元，已中止。");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ScriptPath}\" " +
                        $"-Core \"{core}\" -ArgsFile \"{ArgsPath}\" -StopFile \"{StopPath}\" -ParentPid {Environment.ProcessId}",
        };
        try { _wrapper = Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TryDelete(ArgsPath);
            throw new InvalidOperationException("已取消管理員授權，無法建立虛擬網卡。");
        }
        _running = true;
        CurrentRoom = room;

        try
        {
            Report("連線中，等待取得虛擬 IP…");
            var started = DateTime.UtcNow;
            string? ip = null;
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(60))
            {
                ct.ThrowIfCancellationRequested();
                if (WrapperExited()) throw new InvalidOperationException("EasyTier 已意外結束，請檢查防火牆或防毒軟體是否擋住 easytier-core.exe。");
                var allowNoCidr = DateTime.UtcNow - started > TimeSpan.FromSeconds(15);
                ip = await TryGetIpAsync(allowNoCidr, ct);
                if (ip is not null) break;
                await Task.Delay(1000, ct);
            }
            if (ip is null) throw new TimeoutException("等候虛擬 IP 逾時：可能連不上節點，或房號／密碼有誤。");
            VirtualIp = ip;
            Report($"已連線，虛擬 IP：{ip}");
            return ip;
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        var w = _wrapper;
        try { Directory.CreateDirectory(Dir); File.WriteAllText(StopPath, "stop"); } catch { }
        if (w is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await w.WaitForExitAsync(cts.Token);
            }
            catch { /* 逾時或查詢失敗：腳本也會因旗標檔／父行程消失而自行收尾 */ }
        }
        _wrapper = null;
        _running = false;
        CurrentRoom = null;
        VirtualIp = null;
        TryDelete(ArgsPath);
        Report("已停止");
    }

    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        try { return ((System.Net.IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    // ---------- 虛擬 IP ----------

    async Task<string?> TryGetIpAsync(bool allowNoCidr, CancellationToken ct)
    {
        if (_cli is null || _rpc is null) return null;
        try
        {
            var psi = new ProcessStartInfo(_cli)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("--rpc-portal"); psi.ArgumentList.Add(_rpc); psi.ArgumentList.Add("node");
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var outTask = p.StandardOutput.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(); } catch { } ct.ThrowIfCancellationRequested(); return null; }
            return ParseIp(await outTask, allowNoCidr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    // 未實測：easytier-cli node 的輸出格式要以實際版本為準。
    // 先找帶 /遮罩 的私有位址（例如 10.126.126.2/24），逾時後放寬為任何私有位址。
    // 排除 tcp://x.x.x.x:port 這類監聽位址（前面是 / 或 . 、後面是 :）。
    static string? ParseIp(string text, bool allowNoCidr)
    {
        var re = new Regex(@"(?<![\d./])(\d{1,3}(?:\.\d{1,3}){3})(?:/(\d{1,2}))?(?![\d.:])");
        string? fallback = null;
        foreach (Match m in re.Matches(text))
        {
            var ip = m.Groups[1].Value;
            if (ip.StartsWith("0.") || ip.StartsWith("127.") || ip.StartsWith("169.254.") || !IsPrivate(ip)) continue;
            if (m.Groups[2].Success) return ip;
            fallback ??= ip;
        }
        return allowNoCidr ? fallback : null;
    }

    static bool IsPrivate(string ip)
    {
        var p = ip.Split('.');
        if (p.Length != 4 || !int.TryParse(p[0], out var a) || !int.TryParse(p[1], out var b)) return false;
        return a == 10 || (a == 172 && b is >= 16 and <= 31) || (a == 192 && b == 168) || (a == 100 && b is >= 64 and <= 127);
    }

    // ---------- 下載 easytier-core ----------

    async Task<(string Core, string Cli)> EnsureBinariesAsync(CancellationToken ct)
    {
        var found = Find();
        if (found is not null) return found.Value;

        Report("第一次使用，正在下載 EasyTier…");
        using var res = await Http.GetAsync("https://api.github.com/repos/EasyTier/EasyTier/releases/latest", ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
        string? url = null, digest = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("easytier-windows-", StringComparison.OrdinalIgnoreCase)
                && name.Contains(arch, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                url = a.GetProperty("browser_download_url").GetString();
                if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String) digest = d.GetString();
                break;
            }
        }
        if (url is null) throw new InvalidOperationException($"在 EasyTier 最新版本裡找不到 Windows {arch} 的壓縮檔，請手動下載 easytier-core.exe 放到 {Dir}。");

        Directory.CreateDirectory(Dir);
        var zip = Path.Combine(Dir, "easytier-download.zip");
        try
        {
            await using (var net = await Http.GetStreamAsync(url, ct))
            await using (var fs = File.Create(zip))
                await net.CopyToAsync(fs, ct);

            // GitHub 新版 API 會附 sha256 digest（"sha256:xxxx"），有的話就驗證
            if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var fs = File.OpenRead(zip);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!hash.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("EasyTier 下載檔的 SHA256 不符，已中止。");
            }

            Report("解壓縮 EasyTier…");
            ZipFile.ExtractToDirectory(zip, Dir, overwriteFiles: true);
        }
        finally { TryDelete(zip); }

        return Find() ?? throw new InvalidOperationException("解壓縮後找不到 easytier-core.exe。");
    }

    static (string Core, string Cli)? Find()
    {
        if (!Directory.Exists(Dir)) return null;
        var core = Directory.EnumerateFiles(Dir, "easytier-core.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (core is null) return null;
        var cli = Path.Combine(Path.GetDirectoryName(core)!, "easytier-cli.exe");
        return File.Exists(cli) ? (core, cli) : null;
    }

    // ---------- 以管理員權限執行的監看腳本 ----------
    // 啟動 easytier-core；旗標檔出現或啟動器（父行程）消失時，結束 easytier-core。
    const string RunScript = @"param([string]$Core, [string]$ArgsFile, [string]$StopFile, [int]$ParentPid)
$ErrorActionPreference = 'Stop'
$list = @(Get-Content -LiteralPath $ArgsFile -Encoding UTF8)
Remove-Item -LiteralPath $ArgsFile -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $StopFile) { Remove-Item -LiteralPath $StopFile -Force }
$p = Start-Process -FilePath $Core -ArgumentList $list -WorkingDirectory (Split-Path -Parent $Core) -WindowStyle Hidden -PassThru
while (-not $p.HasExited) {
    if (Test-Path -LiteralPath $StopFile) { break }
    if (-not (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
";
}
