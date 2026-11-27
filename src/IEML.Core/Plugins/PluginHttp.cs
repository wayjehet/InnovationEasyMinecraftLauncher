// ─────────────────────────────────────────────────────────────────────────────
// PluginHttp.cs ── IEML 插件 2.0「非同步 HTTP」本體（取代 1.0 的同步 http.get）
//
// ★ 獨立檔案：沒有接進專案、沒有改任何現有程式碼。只依賴 .NET 8 標準函式庫與
//   IEML.Core.Plugins.PluginDeniedException（你專案 PluginGate.cs 裡已存在）。
//
// 提供：
//   • GET / POST / PUT / PATCH / DELETE，可帶 header 與文字 body
//   • SSE 串流（AI 逐字輸出用）
//   • 金鑰保管：API key 由主程式保管，只在送出請求時注入 header，插件永遠讀不到
//   • 修補 1.0 的缺口：擋 localhost／內網（連線當下檢查實際 IP，防 DNS 重新綁定）、
//     限制連接埠、禁止 IP 位址當網址、禁止 URL 帶帳密
//
// 規則（預設拒絕）：
//   1. 只允許 https，主機名稱必須完全符合 networkHosts 的某一條規則（"host" 或 "host:port"，預設 443）。
//   2. 不跟隨轉址、不存 cookie、不使用系統 Proxy（否則連線檢查會被 Proxy 繞過）。
//   3. 連線時才解析 DNS 並檢查實際 IP；憑證驗證仍用原本的網域名稱。
//   4. 請求本文 ≤ 1 MB、回應 ≤ 2 MB、串流總量 ≤ 8 MB；逾時與同時請求數都有上限。
//   5. 任何錯誤訊息都不包含 header 值（避免金鑰進 log）。
//
// 建議的 JS 介面（之後接 Prelude 用，這裡不實作）：
//   ieml.http.request({ method, url, headers, body, secret, timeoutMs }, function (err, res) {})
//   ieml.http.stream ({ method, url, headers, body, secret }, function (data) {}, function (err, res) {})
//   res = { status, body, truncated, contentType }；串流的 data 是每個 SSE 事件的 data 內容
//
// 建議的 manifest 新欄位（金鑰宣告，由使用者在設定頁貼上實際金鑰）：
//   "secrets": [{ "name": "openai", "label": "OpenAI API Key", "host": "api.openai.com",
//                 "header": "Authorization", "prefix": "Bearer " }]
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IEML.Core.Plugins;

// ───────────────────────── 資料型別 ─────────────────────────

public sealed class HttpRequestSpec
{
    string _method = "GET", _url = "";
    Dictionary<string, string> _headers = new();
    public string Method { get => _method; set => _method = value ?? "GET"; }
    public string Url { get => _url; set => _url = value ?? ""; }
    public Dictionary<string, string> Headers { get => _headers; set => _headers = value ?? new(); }
    public string? Body { get; set; }
    public string? Secret { get; set; }          // manifest.secrets 裡的 name；由主程式注入 header
    public int TimeoutMs { get; set; } = 30_000;
}

public sealed class HttpResult
{
    public int Status { get; init; }
    public string Body { get; init; } = "";
    public bool Truncated { get; init; }
    public string? ContentType { get; init; }
    public int Events { get; init; }             // 串流模式：收到的 SSE 事件數
}

/// 使用者在設定頁貼上的實際金鑰（存放時請用 DPAPI 加密；本檔只定義讀取介面）。
public sealed class SecretEntry
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";       // 只能用於這個網域
    public string HeaderName { get; set; } = "Authorization";
    public string Prefix { get; set; } = "";     // 例如 "Bearer "
    public string Value { get; set; } = "";
}

public interface ISecretVault
{
    /// 取得「這個插件」名下、名稱為 name 的金鑰；沒有就回傳 null。
    SecretEntry? Get(string pluginId, string name);
}

/// manifest "secrets" 的一項：插件宣告「我需要一把金鑰」。
public sealed class SecretDecl
{
    string _name = "", _label = "", _host = "", _header = "Authorization", _prefix = "";
    public string Name { get => _name; set => _name = value ?? ""; }
    public string Label { get => _label; set => _label = value ?? ""; }
    public string Host { get => _host; set => _host = value ?? ""; }
    public string Header { get => _header; set => _header = value ?? ""; }
    public string Prefix { get => _prefix; set => _prefix = value ?? ""; }

    static readonly Regex NameRx = new("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.Compiled);

    public string? Validate(IReadOnlyList<string>? networkHosts)
    {
        networkHosts ??= Array.Empty<string>();
        if (!NameRx.IsMatch(Name)) return $"secret name 格式不正確：{Name}";
        if (string.IsNullOrWhiteSpace(Label) || Label.Length > 40) return $"secret「{Name}」的 label 必填且不超過 40 字";
        if (Prefix.Length > 20) return $"secret「{Name}」的 prefix 太長";
        if (!HttpRules.IsValidHeaderName(Header) || HttpRules.IsForbiddenHeader(Header))
            return $"secret「{Name}」的 header 名稱不允許：{Header}";
        // host 必須是 networkHosts 內某條規則的主機（金鑰只能送去已授權的網域）
        var ok = networkHosts.Any(r => HttpRules.TryParseHostRule(r, out var h, out _) && h == Host);
        return ok ? null : $"secret「{Name}」的 host 必須出現在 networkHosts：{Host}";
    }
}

public sealed class HttpPolicy
{
    /// 是否允許連到 localhost／內網位址（例如本機的 Ollama）。預設 false；
    /// 建議之後做成獨立的高風險權限（例如 net.local），不要預設開放。
    public bool AllowPrivateNetwork { get; init; }
}

public static class HttpLimits
{
    public const int MaxRequestBytes = 1 * 1024 * 1024;
    public const int MaxResponseBytes = 2 * 1024 * 1024;
    public const int MaxStreamChars = 8 * 1024 * 1024;
    public const int MaxSseLineChars = 64 * 1024;
    public const int MaxHeaders = 32;
    public const int MaxHeaderValue = 4096;
    public const int MaxUrlLength = 2048;
    public const int MaxConcurrentPerPlugin = 4;
    public const int MinTimeoutMs = 1_000;
    public const int MaxTimeoutMs = 60_000;
    public static readonly TimeSpan StreamTotalTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(30);
}

// ───────────────────────── 規則與驗證 ─────────────────────────

public static class HttpRules
{
    static readonly HashSet<string> Methods = new(StringComparer.Ordinal) { "GET", "POST", "PUT", "PATCH", "DELETE" };

    // 插件不能自己設的 header（hop-by-hop、會影響路由／框架的、cookie）
    static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "content-length", "transfer-encoding", "connection", "upgrade", "te", "trailer",
        "keep-alive", "proxy-authorization", "proxy-connection", "cookie", "expect", "via",
    };

    static readonly Regex TokenRx = new("^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,64}$", RegexOptions.Compiled);
    static readonly Regex RuleRx = new("^([a-z0-9]([a-z0-9.-]{0,251}[a-z0-9])?)(:([0-9]{1,5}))?$", RegexOptions.Compiled);

    public static bool IsValidHeaderName(string name) => TokenRx.IsMatch(name);

    public static bool IsForbiddenHeader(string name) =>
        Forbidden.Contains(name)
        || name.StartsWith("proxy-", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("content-", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("content-type", StringComparison.OrdinalIgnoreCase));

    /// 解析 networkHosts 的一條規則："api.openai.com"（預設 443）或 "api.example.com:8443"。
    public static bool TryParseHostRule(string rule, out string host, out int port)
    {
        host = ""; port = 443;
        var m = RuleRx.Match(rule);
        if (!m.Success) return false;
        host = m.Groups[1].Value;
        if (m.Groups[4].Success)
        {
            if (!int.TryParse(m.Groups[4].Value, out port) || port is < 1 or > 65535) return false;
        }
        return !IPAddress.TryParse(host, out _);     // 規則本身也不能是 IP
    }

    /// 驗證 manifest 的 networkHosts（取代 1.0 只允許純網域的規則）。
    public static string? ValidateHostRules(IReadOnlyList<string> rules)
    {
        foreach (var r in rules)
            if (!TryParseHostRule(r, out _, out _))
                return $"networkHosts 格式不正確：{r}（小寫網域，可加 :連接埠；不能是 IP、萬用字元或路徑）";
        return null;
    }

    public static string NormalizeMethod(string method)
    {
        var m = (method ?? "").Trim().ToUpperInvariant();
        return Methods.Contains(m) ? m : throw new ArgumentException($"不支援的 HTTP 方法：{method}");
    }

    /// 驗證並解析網址；通過才回傳 Uri 與小寫主機名稱。
    public static Uri ValidateUrl(string url, IReadOnlyList<string> networkHosts, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(url) || url.Length > HttpLimits.MaxUrlLength)
            throw new ArgumentException("網址為空或太長");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new PluginDeniedException("只允許 https 網址");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new PluginDeniedException("網址不能包含帳號密碼");
        if (uri.HostNameType != UriHostNameType.Dns)
            throw new PluginDeniedException("網址必須使用網域名稱，不能用 IP 位址");

        host = uri.IdnHost.ToLowerInvariant();
        var port = uri.Port;
        foreach (var r in networkHosts)
            if (TryParseHostRule(r, out var h, out var p) && h == host && p == port)
                return uri;
        throw new PluginDeniedException($"{host}:{port} 不在這個插件的 networkHosts 內");
    }

    /// 是否為公開網路位址（排除本機、內網、連結本地、CGNAT、保留、多播）。
    public static bool IsPublicAddress(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a)) return false;

        if (a.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = a.GetAddressBytes();
            if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
            if (b[0] == 100 && b[1] is >= 64 and <= 127) return false;     // CGNAT 100.64/10
            if (b[0] == 169 && b[1] == 254) return false;                  // 連結本地（含雲端中繼資料 169.254.169.254）
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;       // 192.0.0.0/24
            if (b[0] == 198 && b[1] is 18 or 19) return false;             // 測試用 198.18/15
            if (b[0] >= 224) return false;                                 // 多播、保留、廣播
            return true;
        }

        if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (a.Equals(IPAddress.IPv6Any) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return false;
            var b = a.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                       // ULA fc00::/7
            return true;
        }
        return false;
    }
}

// ───────────────────────── 執行本體 ─────────────────────────

public static class PluginHttpClient
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> PerPlugin = new();
    static readonly HttpClient Strict = Create(allowPrivate: false);
    static readonly HttpClient Lenient = Create(allowPrivate: true);   // 分開連線池，避免「允許內網」的連線被一般插件重用

    static HttpClient Create(bool allowPrivate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,                        // 用 Proxy 會讓下面的 IP 檢查失去意義
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = allowPrivate ? ConnectLenient : ConnectStrict,
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    static ValueTask<Stream> ConnectStrict(SocketsHttpConnectionContext ctx, CancellationToken ct) => ConnectCore(ctx, ct, false);
    static ValueTask<Stream> ConnectLenient(SocketsHttpConnectionContext ctx, CancellationToken ct) => ConnectCore(ctx, ct, true);

    /// 連線當下才解析 DNS 並檢查「實際要連的 IP」。TLS 仍由 HttpClient 以原網域名稱驗證憑證。
    static async ValueTask<Stream> ConnectCore(SocketsHttpConnectionContext ctx, CancellationToken ct, bool allowPrivate)
    {
        var host = ctx.DnsEndPoint.Host;
        var port = ctx.DnsEndPoint.Port;
        var addrs = await Dns.GetHostAddressesAsync(host, ct);

        Exception? last = null;
        var tried = false;
        foreach (var a in addrs)
        {
            if (!allowPrivate && !HttpRules.IsPublicAddress(a)) continue;
            tried = true;
            var s = new Socket(a.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await s.ConnectAsync(new IPEndPoint(a, port), ct);
                return new NetworkStream(s, ownsSocket: true);
            }
            catch (Exception ex) { s.Dispose(); last = ex; }
        }
        if (!tried) throw new PluginDeniedException($"{host} 解析到本機或內網位址，已拒絕連線");
        throw last ?? new HttpRequestException($"無法連線到 {host}");
    }

    // ───── 一般請求 ─────

    public static async Task<HttpResult> SendAsync(
        string pluginId, IReadOnlyList<string> networkHosts, HttpRequestSpec spec,
        ISecretVault? vault, HttpPolicy? policy = null, CancellationToken ct = default)
    {
        using var req = BuildRequest(pluginId, networkHosts, spec, vault);
        using var slot = await AcquireSlot(pluginId);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ClampTimeout(spec.TimeoutMs));
        try
        {
            using var resp = await ClientFor(policy).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var (body, truncated) = await ReadCappedAsync(resp.Content, HttpLimits.MaxResponseBytes, cts.Token);
            return new HttpResult
            {
                Status = (int)resp.StatusCode,
                Body = body,
                Truncated = truncated,
                ContentType = resp.Content.Headers.ContentType?.ToString(),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("網路請求逾時"); }
        catch (HttpRequestException ex) { throw Unwrap(ex); }
    }

    // ───── SSE 串流 ─────

    /// 以 SSE 格式讀取回應；每個事件呼叫一次 onData（data: 內容，多行以 \n 連接）。
    /// 回應不是 2xx 時不會呼叫 onData，而是把（有上限的）錯誤本文放進結果回傳。
    public static async Task<HttpResult> StreamSseAsync(
        string pluginId, IReadOnlyList<string> networkHosts, HttpRequestSpec spec,
        ISecretVault? vault, Func<string, Task> onData, HttpPolicy? policy = null, CancellationToken ct = default)
    {
        using var req = BuildRequest(pluginId, networkHosts, spec, vault);
        req.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        using var slot = await AcquireSlot(pluginId);

        using var totalCts = new CancellationTokenSource(HttpLimits.StreamTotalTimeout);
        using var idleCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, totalCts.Token, idleCts.Token);
        idleCts.CancelAfter(HttpLimits.StreamIdleTimeout);

        try
        {
            using var resp = await ClientFor(policy).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var status = (int)resp.StatusCode;
            var ctype = resp.Content.Headers.ContentType?.ToString();

            if (!resp.IsSuccessStatusCode)
            {
                var (errBody, errTrunc) = await ReadCappedAsync(resp.Content, HttpLimits.MaxResponseBytes, linked.Token);
                return new HttpResult { Status = status, Body = errBody, Truncated = errTrunc, ContentType = ctype };
            }

            using var stream = await resp.Content.ReadAsStreamAsync(linked.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

            var line = new StringBuilder();
            var data = new StringBuilder();
            var hasData = false;
            var events = 0;
            long totalChars = 0;
            var truncated = false;
            var buf = new char[4096];

            async Task Dispatch()
            {
                if (!hasData) return;
                var payload = data.ToString();
                data.Clear(); hasData = false; events++;
                await onData(payload);
            }

            async Task HandleLine()
            {
                var s = line.ToString();
                line.Clear();
                if (s.Length == 0) { await Dispatch(); return; }          // 空行＝事件結束
                if (s[0] == ':') return;                                  // 註解／keep-alive
                if (!s.StartsWith("data:", StringComparison.Ordinal)) return;   // event:、id:、retry: 一律忽略
                var v = s.Substring(5);
                if (v.Length > 0 && v[0] == ' ') v = v.Substring(1);
                if (hasData) data.Append('\n');
                data.Append(v);
                hasData = true;
            }

            while (!truncated)
            {
                var n = await reader.ReadAsync(buf.AsMemory(), linked.Token);
                if (n == 0) break;
                idleCts.CancelAfter(HttpLimits.StreamIdleTimeout);        // 有資料就重置閒置計時

                totalChars += n;
                if (totalChars > HttpLimits.MaxStreamChars) { truncated = true; break; }

                for (var i = 0; i < n; i++)
                {
                    var c = buf[i];
                    if (c == '\n') await HandleLine();
                    else if (c != '\r')
                    {
                        if (line.Length >= HttpLimits.MaxSseLineChars) { truncated = true; break; }
                        line.Append(c);
                    }
                }
            }
            if (line.Length > 0 && !truncated) await HandleLine();
            if (!truncated) await Dispatch();                             // 串流結束時送出尚未送出的事件

            return new HttpResult { Status = status, Truncated = truncated, ContentType = ctype, Events = events };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(totalCts.IsCancellationRequested ? "串流超過時間上限" : "串流閒置逾時");
        }
        catch (HttpRequestException ex) { throw Unwrap(ex); }
    }

    // ───── 內部 ─────

    static HttpClient ClientFor(HttpPolicy? p) => p is { AllowPrivateNetwork: true } ? Lenient : Strict;

    static int ClampTimeout(int ms) =>
        ms < HttpLimits.MinTimeoutMs || ms > HttpLimits.MaxTimeoutMs ? 30_000 : ms;

    static HttpRequestMessage BuildRequest(string pluginId, IReadOnlyList<string> networkHosts,
        HttpRequestSpec spec, ISecretVault? vault)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentNullException.ThrowIfNull(spec);
        networkHosts ??= Array.Empty<string>();
        var method = HttpRules.NormalizeMethod(spec.Method);
        var uri = HttpRules.ValidateUrl(spec.Url, networkHosts, out var host);

        // 收集 header（不分大小寫；同名以最後一個為準），先驗證插件自己給的。
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (spec.Headers.Count > HttpLimits.MaxHeaders) throw new ArgumentException($"header 最多 {HttpLimits.MaxHeaders} 個");
        foreach (var (k, v) in spec.Headers)
        {
            if (!HttpRules.IsValidHeaderName(k)) throw new ArgumentException($"header 名稱不合法：{k}");
            if (HttpRules.IsForbiddenHeader(k)) throw new PluginDeniedException($"插件不能設定 header：{k}");
            if (v is null || v.Length > HttpLimits.MaxHeaderValue || v.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException($"header 值不合法：{k}");
            headers[k] = v;
        }

        // 金鑰注入：由主程式從保管庫取值，覆蓋插件給的同名 header；只能送往金鑰綁定的網域。
        if (!string.IsNullOrEmpty(spec.Secret))
        {
            var entry = vault?.Get(pluginId, spec.Secret)
                ?? throw new PluginDeniedException($"找不到金鑰「{spec.Secret}」，請到插件設定頁填入");
            if (!string.Equals(entry.Host, host, StringComparison.Ordinal))
                throw new PluginDeniedException($"金鑰「{spec.Secret}」只能用於 {entry.Host}");
            if (!HttpRules.IsValidHeaderName(entry.HeaderName) || HttpRules.IsForbiddenHeader(entry.HeaderName))
                throw new PluginDeniedException("金鑰設定的 header 名稱不允許");
            headers[entry.HeaderName] = entry.Prefix + entry.Value;
        }

        var req = new HttpRequestMessage(new HttpMethod(method), uri);
        if (!headers.ContainsKey("User-Agent"))
            req.Headers.TryAddWithoutValidation("User-Agent", "IEML-Plugin/" + pluginId);   // 讓對方伺服器能辨識來源

        // 本文
        if (spec.Body is not null)
        {
            if (method is "GET")
                throw new ArgumentException("GET 請求不能帶 body");
            if (Encoding.UTF8.GetByteCount(spec.Body) > HttpLimits.MaxRequestBytes)
                throw new ArgumentException("請求本文超過 1 MB");
            req.Content = new StringContent(spec.Body, Encoding.UTF8);
            req.Content.Headers.ContentType = null;
        }

        foreach (var (k, v) in headers)
        {
            if (k.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                if (req.Content is null) continue;                          // 沒有 body 就不需要
                if (!MediaTypeHeaderValue.TryParse(v, out var mt)) throw new ArgumentException("Content-Type 格式不正確");
                req.Content.Headers.ContentType = mt;
            }
            else if (!req.Headers.TryAddWithoutValidation(k, v))
                throw new ArgumentException($"無法設定 header：{k}");
        }
        if (req.Content is not null && req.Content.Headers.ContentType is null)
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };

        return req;
    }

    /// 每個插件同時最多 4 個請求；拿不到就直接拒絕，不排隊。
    static async Task<Slot> AcquireSlot(string pluginId)
    {
        var sem = PerPlugin.GetOrAdd(pluginId, _ => new SemaphoreSlim(HttpLimits.MaxConcurrentPerPlugin));
        if (!await sem.WaitAsync(0)) throw new InvalidOperationException("這個插件同時進行的網路請求太多");
        return new Slot(sem);
    }

    sealed class Slot : IDisposable
    {
        readonly SemaphoreSlim _s; int _done;
        public Slot(SemaphoreSlim s) => _s = s;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) _s.Release(); }
    }

    static async Task<(string Text, bool Truncated)> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        using var s = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        var truncated = false;
        int n;
        while ((n = await s.ReadAsync(buf.AsMemory(), ct)) > 0)
        {
            var room = maxBytes - (int)ms.Length;
            if (n > room) { ms.Write(buf, 0, Math.Max(room, 0)); truncated = true; break; }   // 超過就停止讀取
            ms.Write(buf, 0, n);
        }
        Encoding enc = Encoding.UTF8;
        var cs = content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(cs)) { try { enc = Encoding.GetEncoding(cs); } catch { enc = Encoding.UTF8; } }
        return (enc.GetString(ms.GetBuffer(), 0, (int)ms.Length), truncated);
    }

    /// HttpClient 會把連線時丟出的 PluginDeniedException 包在 HttpRequestException 裡，這裡還原。
    static Exception Unwrap(HttpRequestException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PluginDeniedException d) return d;
        return new HttpRequestException("網路請求失敗：" + (ex.InnerException?.Message ?? ex.Message));
    }
}
