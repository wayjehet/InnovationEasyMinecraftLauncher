using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IEML.Core.Plugins;
using Jint;
namespace IEML.App;

static class J
{
    public static JsonNode? Get(JsonNode? n, string key) => n is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;
    public static string? S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static int I(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
    public static bool B(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public static string Clip(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];
}

/// 一個插件 = 一個獨立的 Jint 引擎（JavaScript 直譯器）。
/// 沙盒的本質：引擎裡**沒有**檔案、網路、程序、.NET（沒開 CLR 互通）；唯一的出口是 __host(method, json)，
/// 而 __host 每次呼叫都先過 PluginGate.Authorize。限制：2 秒逾時、32 MB 記憶體、遞迴 120 層、30 萬條陳述式。
public sealed class PluginRuntime : IDisposable
{
    public InstalledPlugin Plugin { get; }
    public PluginState State { get; }
    public Dictionary<string, (string Title, string Subtitle)> Pages { get; } = new();
    public HashSet<string> EventNames { get; } = new();
    public string? LastError { get; private set; }
    public bool Loaded { get; private set; }

    readonly PluginManager _mgr;
    Engine? _engine;
    CancellationTokenSource _cts = new();
    bool _dialogOpen;
    readonly Queue<DateTime> _dialogTimes = new();

    /// 頁面內容可能已經過時（非同步 callback 剛跑完）：開著的插件頁會據此重畫。
    public event Action? PageInvalidated;      // 插件停止時取消進行中的網路請求／外部程式
    Dictionary<string, string>? _data;
    static readonly Regex PageId = new("^[a-z0-9_-]{1,40}$", RegexOptions.Compiled);
    const int StorageLimit = 200 * 1024;

    public PluginRuntime(InstalledPlugin plugin, PluginState state, PluginManager mgr)
    { Plugin = plugin; State = state; _mgr = mgr; }

    public bool Start()
    {
        try
        {
            var main = Path.GetFullPath(Path.Combine(Plugin.Dir, Plugin.Manifest.Main));
            if (!main.StartsWith(Path.GetFullPath(Plugin.Dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("main 不在插件資料夾內");
            if (new FileInfo(main).Length > 512 * 1024) throw new InvalidDataException("main.js 太大（上限 512 KB）");

            _cts = new CancellationTokenSource();
            _engine = new Engine(o =>
            {
                o.Strict();
                o.TimeoutInterval(TimeSpan.FromSeconds(2));
                o.LimitMemory(32 * 1024 * 1024);
                o.LimitRecursion(120);
                o.MaxStatements(300_000);
                // 刻意不呼叫 AllowClr()：腳本看不到任何 .NET 型別。
            });
            _engine.SetValue("__host", new Func<string, string, string>(Host));
            _engine.Execute(Prelude);
            _engine.Execute(File.ReadAllText(main));
            Loaded = true; LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = "載入失敗：" + ex.Message;
            _mgr.Log(Plugin.Manifest.Id, LastError);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        Loaded = false;
        try { _cts.Cancel(); } catch { }
        try { _engine?.Dispose(); } catch { }
        _engine = null;
        Pages.Clear(); EventNames.Clear();
    }

    public void Dispose() => Stop();

    // ───────────── 唯一的出口：JS → 主程式 ─────────────
    string Host(string method, string json)
    {
        try
        {
            PluginGate.Authorize(Plugin.Manifest, State, method);        // ← 權限閘口（每次呼叫都檢查）
            var args = string.IsNullOrEmpty(json) || json == "null" ? null : JsonNode.Parse(json);
            return Handle(method, args)?.ToJsonString() ?? "";
        }
        catch (Exception ex)
        {
            _mgr.Log(Plugin.Manifest.Id, $"{method} 被拒絕或失敗：{ex.Message}");
            return new JsonObject { ["__error"] = ex.Message }.ToJsonString();   // 變成 JS 的 Error，插件可 try/catch
        }
    }

    JsonNode? Handle(string method, JsonNode? a)
    {
        var id = Plugin.Manifest.Id;
        switch (method)
        {
            case "log": _mgr.Log(id, J.Clip(J.S(a), 500)); return null;

            case "hooks.on":
            {
                var n = J.S(a);
                if (n is { Length: > 0 and <= 64 } && EventNames.Count < 20) EventNames.Add(n);
                return null;
            }

            case "ui.registerPage":
            {
                var pid = J.S(J.Get(a, "id"));
                if (pid is null || !PageId.IsMatch(pid)) throw new ArgumentException("頁面 id 只能用小寫英數、底線、連字號（1–40 字）");
                if (Pages.Count >= 20 && !Pages.ContainsKey(pid)) throw new InvalidOperationException("頁面數量太多（上限 20）");
                Pages[pid] = (J.Clip(J.S(J.Get(a, "title")) ?? pid, 60), J.Clip(J.S(J.Get(a, "subtitle")), 120));
                _mgr.RaiseChanged();
                return null;
            }

            case "ui.openPage":
            {
                var pid = J.S(a) ?? "";
                if (!Pages.ContainsKey(pid)) throw new ArgumentException("沒有這個頁面");
                _mgr.OpenPageDeferred(this, pid);      // 延後到目前的腳本執行完才開，避免引擎重入
                return null;
            }

            case "ui.toast":
                _mgr.Toast(J.Clip(J.S(J.Get(a, "text")), 200), J.B(J.Get(a, "error")));
                return null;

            case "ui.confirm":
            {
                int cb = J.I(J.Get(a, "cb")); var t = DialogTitle(a); var msg = J.Clip(J.S(J.Get(a, "message")), 800);
                DialogQuota();
                _ = Ask();
                async Task Ask() { bool ok; try { ok = await Dialogs.ConfirmAsync(t, msg); } finally { _dialogOpen = false; } Callback(cb, JsonValue.Create(ok)); }
                return null;
            }

            case "ui.info":
            {
                int cb = J.I(J.Get(a, "cb")); var t = DialogTitle(a); var msg = J.Clip(J.S(J.Get(a, "message")), 800);
                DialogQuota();
                _ = Ask();
                async Task Ask() { try { await Dialogs.InfoAsync(t, msg); } finally { _dialogOpen = false; } Callback(cb, null); }
                return null;
            }

            case "ui.choose":
            {
                int cb = J.I(J.Get(a, "cb")); var t = DialogTitle(a); var msg = J.Clip(J.S(J.Get(a, "message")), 800);
                var buttons = (J.Get(a, "buttons") as JsonArray)?.Select(x => J.Clip(J.S(x), 30)).Where(s => s.Length > 0).Take(4).ToArray() ?? Array.Empty<string>();
                if (buttons.Length == 0) throw new ArgumentException("至少要有一個按鈕");
                DialogQuota();
                _ = Ask();
                async Task Ask() { int i; try { i = await Dialogs.ChoiceAsync(t, msg, buttons); } finally { _dialogOpen = false; } Callback(cb, JsonValue.Create(i)); }
                return null;
            }

            case "storage.get":
                return JsonValue.Create(Data().TryGetValue(J.Clip(J.S(a), 100), out var sv) ? sv : null);

            case "storage.set":
            {
                var key = J.Clip(J.S(J.Get(a, "key")), 100); var val = J.S(J.Get(a, "value")) ?? "";
                if (key.Length == 0) throw new ArgumentException("key 不能是空的");
                var d = Data();
                var next = new Dictionary<string, string>(d) { [key] = val };
                if (next.Sum(kv => kv.Key.Length + kv.Value.Length) > StorageLimit) throw new InvalidOperationException("儲存空間已滿（上限 200 KB）");
                _data = next; SaveData();
                return null;
            }

            case "storage.remove":
                if (Data().Remove(J.Clip(J.S(a), 100))) SaveData();
                return null;

            case "instances.list": return _mgr.InstancesJson();
            case "accounts.list": return _mgr.AccountsJson();
            case "account.getToken": return JsonValue.Create(_mgr.GetToken());
            case "http.get": return _mgr.HttpGet(Plugin.Manifest, J.S(J.Get(a, "url")) ?? "");

            // ───── 2.0：非同步 HTTP（結果用 callback 回來；每個請求都在 PluginHttpClient 內重新驗證網域／IP） ─────
            case "http.request":
            {
                int cb = J.I(J.Get(a, "cb"));
                var spec = ReadHttpSpec(a);
                _ = Run();
                async Task Run()
                {
                    try
                    {
                        var r = await PluginHttpClient.SendAsync(id, Plugin.Manifest.NetworkHosts, spec, _mgr.Vault, null, _cts.Token);
                        CallbackResult(cb, null, HttpJson(r));
                    }
                    catch (Exception ex) { _mgr.Log(id, $"http.request 失敗：{ex.Message}"); CallbackResult(cb, ex.Message, null); }
                }
                return null;
            }

            case "http.stream":
            {
                int dcb = J.I(J.Get(a, "dcb")), cb = J.I(J.Get(a, "cb"));
                var spec = ReadHttpSpec(a);
                _ = Run();
                async Task Run()
                {
                    try
                    {
                        var r = await PluginHttpClient.StreamSseAsync(id, Plugin.Manifest.NetworkHosts, spec, _mgr.Vault, data =>
                        {
                            // 串流進行中權限被撤銷、或插件被停用：立刻中止
                            if (!Loaded || !PluginGate.Has(Plugin.Manifest, State, Perm.NetHttp))
                                throw new PluginDeniedException("網路權限已被撤銷，串流中止");
                            Callback(dcb, JsonValue.Create(data), keep: true);
                            return Task.CompletedTask;
                        }, null, _cts.Token);
                        CallbackResult(cb, null, HttpJson(r));
                    }
                    catch (Exception ex) { _mgr.Log(id, $"http.stream 失敗：{ex.Message}"); CallbackResult(cb, ex.Message, null); }
                }
                return null;
            }

            // ───── 2.0：exec（每次執行都會再問使用者一次） ─────
            case "exec.run":
            {
                int cb = J.I(J.Get(a, "cb"));
                var execId = J.S(J.Get(a, "id")) ?? "";
                var args = (J.Get(a, "args") as JsonArray)?.Select(x => J.S(x) ?? throw new ArgumentException("exec 參數必須是字串")).ToList() ?? new List<string>();
                var tmo = J.I(J.Get(a, "timeoutMs"));
                var decl = Plugin.Manifest.Exec.FirstOrDefault(d => d.Id == execId)
                    ?? throw new PluginDeniedException($"這個插件沒有宣告可執行的程式：{execId}");
                _ = Run();
                async Task Run()
                {
                    try
                    {
                        var shown = args.Count == 0 ? "（無）" : J.Clip(string.Join(" ", args.Select(x => "\"" + J.Clip(x, 80).Replace('\n', ' ').Replace('\r', ' ') + "\"")), 400);
                        var ok = await Dialogs.ConfirmAsync(
                            $"插件「{Plugin.Manifest.Name}」要執行外部程式",
                            $"程式：{decl.Id}（{decl.File}）\n用途：{(decl.Description.Length > 0 ? decl.Description : "（插件沒有說明）")}\n參數：{shown}\n\n外部程式會以你的 Windows 權限執行，沙盒管不到它。只在你信任這個插件時才允許。",
                            "允許執行", "拒絕");
                        if (!ok) { CallbackResult(cb, "使用者拒絕執行", null); return; }
                        PluginGate.Authorize(Plugin.Manifest, State, "exec.run");     // 對話框期間權限或同意可能被撤銷，再檢查一次
                        var r = await PluginExecRunner.RunAsync(Plugin.Dir, Plugin.Manifest.Exec, execId, args,
                            tmo > 0 ? TimeSpan.FromMilliseconds(tmo) : null, _cts.Token);
                        CallbackResult(cb, null, new JsonObject
                        {
                            ["exitCode"] = r.ExitCode, ["stdout"] = r.Stdout, ["stderr"] = r.Stderr,
                            ["timedOut"] = r.TimedOut, ["outputTruncated"] = r.OutputTruncated
                        });
                    }
                    catch (Exception ex) { _mgr.Log(id, $"exec.run 失敗：{ex.Message}"); CallbackResult(cb, ex.Message, null); }
                }
                return null;
            }

            default: throw new PluginDeniedException("未知的方法：" + method);   // 不該發生（Authorize 已擋）
        }
    }

    static HttpRequestSpec ReadHttpSpec(JsonNode? a)
    {
        var t = J.I(J.Get(a, "timeoutMs"));
        var spec = new HttpRequestSpec
        {
            Method = J.S(J.Get(a, "method")) ?? "GET",
            Url = J.S(J.Get(a, "url")) ?? "",
            Body = J.S(J.Get(a, "body")),
            Secret = J.S(J.Get(a, "secret")),
            TimeoutMs = t > 0 ? t : 30_000,
        };
        if (J.Get(a, "headers") is JsonObject ho)
            foreach (var kv in ho)
                spec.Headers[kv.Key] = J.S(kv.Value) ?? throw new ArgumentException($"header 值必須是字串：{kv.Key}");
        return spec;
    }

    static JsonObject HttpJson(HttpResult r) => new()
    {
        ["status"] = r.Status, ["body"] = r.Body, ["truncated"] = r.Truncated, ["contentType"] = r.ContentType, ["events"] = r.Events
    };

    /// 彈窗不需要權限，所以限制頻率：同時只能有一個在等使用者回應，每分鐘最多 6 個（防洗版、防卡住介面）。
    void DialogQuota()
    {
        if (_dialogOpen) throw new InvalidOperationException("已經有一個彈窗在等待使用者回應");
        var now = DateTime.UtcNow;
        while (_dialogTimes.Count > 0 && now - _dialogTimes.Peek() > TimeSpan.FromMinutes(1)) _dialogTimes.Dequeue();
        if (_dialogTimes.Count >= 6) throw new InvalidOperationException("彈窗太頻繁（每分鐘最多 6 個）");
        _dialogTimes.Enqueue(now);
        _dialogOpen = true;
    }

    string DialogTitle(JsonNode? a) => $"插件「{Plugin.Manifest.Name}」：{J.Clip(J.S(J.Get(a, "title")), 60)}";   // 標明來源，避免插件假冒系統對話框

    // ───────────── 儲存（每個插件自己的 data.json） ─────────────
    string DataPath => Path.Combine(Plugin.Dir, "data.json");
    Dictionary<string, string> Data()
    {
        if (_data is not null) return _data;
        try { _data = File.Exists(DataPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(DataPath)) ?? new() : new(); }
        catch { _data = new(); }
        return _data;
    }
    void SaveData() => File.WriteAllText(DataPath, JsonSerializer.Serialize(_data));

    // ───────────── 主程式 → JS ─────────────
    void Callback(int cb, JsonNode? result, bool keep = false)
    {
        if (cb <= 0 || !Loaded) return;
        Dispatch("callback", new JsonObject { ["id"] = cb, ["result"] = result, ["keep"] = keep });   // keep＝回呼會被重複呼叫（串流資料）
        PageInvalidated?.Invoke();                                                                  // 狀態可能變了 → 開著的插件頁自動重畫
    }

    /// 2.0 的 (err, res) 型回呼：JS 端會轉成 callback(Error|null, res)。
    void CallbackResult(int cb, string? err, JsonNode? res) =>
        Callback(cb, new JsonObject { ["err"] = err, ["res"] = res });

    /// 呼叫插件裡的函式（render／onAction／事件／回呼），回傳 JSON 字串或 null。錯誤只記錄，不會讓啟動器掛掉。
    public string? Dispatch(string kind, JsonNode? payload)
    {
        if (_engine is null) return null;
        try
        {
            var r = _engine.Invoke("__dispatch", kind, payload?.ToJsonString() ?? "null");
            return r.IsNull() || r.IsUndefined() ? null : r.ToString();
        }
        catch (Exception ex)
        {
            LastError = "執行錯誤：" + ex.Message;
            _mgr.Log(Plugin.Manifest.Id, LastError);
            return null;
        }
    }

    // 插件內的 `ieml` 物件：全部包在 __host 之上，plugin 看到的只有這些方法。
    const string Prelude = """
(function (g) {
  'use strict';
  var host = g.__host;
  try { delete g.__host; } catch (e) {}
  var pages = {}, handlers = {}, cbs = {}, seq = 0;
  function call(method, args) {
    var raw = host(method, args === undefined ? 'null' : JSON.stringify(args));
    if (!raw) return null;
    var res = JSON.parse(raw);
    if (res && typeof res === 'object' && res.__error) throw new Error(res.__error);
    return res;
  }
  function strMap(h) {
    var out = {};
    if (h && typeof h === 'object') for (var k in h) if (Object.prototype.hasOwnProperty.call(h, k)) out[k] = String(h[k]);
    return out;
  }
  function wrap(fn) {
    return function (r) { if (typeof fn === 'function') fn(r && r.err ? new Error(r.err) : null, r ? r.res : null); };
  }
  function httpOpts(o) {
    o = o || {};
    return {
      method: o.method ? String(o.method) : 'GET', url: String(o.url), headers: strMap(o.headers),
      body: (o.body === undefined || o.body === null) ? null : String(o.body),
      secret: o.secret ? String(o.secret) : null, timeoutMs: Number(o.timeoutMs) || 0
    };
  }
  function reg(fn) { if (typeof fn !== 'function') return 0; var id = ++seq; cbs[id] = fn; return id; }
  g.ieml = {
    apiVersion: 2,
    log: function (m) { call('log', String(m)); },
    on: function (name, fn) {
      if (typeof fn !== 'function') return;
      call('hooks.on', String(name));
      (handlers[name] = handlers[name] || []).push(fn);
    },
    ui: {
      registerPage: function (p) {
        if (!p || !p.id) throw new Error('page.id 必填');
        pages[p.id] = p;
        call('ui.registerPage', { id: String(p.id), title: String(p.title || p.id), subtitle: String(p.subtitle || '') });
      },
      openPage: function (id) { call('ui.openPage', String(id)); },
      toast: function (text, isError) { call('ui.toast', { text: String(text), error: !!isError }); },
      confirm: function (title, message, fn) { call('ui.confirm', { title: String(title), message: String(message), cb: reg(fn) }); },
      info: function (title, message, fn) { call('ui.info', { title: String(title), message: String(message), cb: reg(fn) }); },
      choose: function (title, message, buttons, fn) {
        call('ui.choose', { title: String(title), message: String(message), buttons: (buttons || []).map(String), cb: reg(fn) });
      }
    },
    storage: {
      get: function (k) { return call('storage.get', String(k)); },
      set: function (k, v) { call('storage.set', { key: String(k), value: typeof v === 'string' ? v : JSON.stringify(v) }); },
      remove: function (k) { call('storage.remove', String(k)); }
    },
    instances: { list: function () { return call('instances.list'); } },
    accounts: {
      list: function () { return call('accounts.list'); },
      getToken: function () { return call('account.getToken'); }
    },
    http: {
      get: function (url) { return call('http.get', { url: String(url) }); },
      // 2.0：ieml.http.request({ method, url, headers, body, secret, timeoutMs }, function (err, res) {})
      request: function (opts, fn) {
        var o = httpOpts(opts); o.cb = reg(wrap(fn));
        call('http.request', o);
      },
      // 2.0：ieml.http.stream({ ... }, function (data) {}, function (err, res) {})   data＝每個 SSE 事件的 data
      stream: function (opts, onData, done) {
        var o = httpOpts(opts);
        var dcb = reg(function (d) { if (typeof onData === 'function') onData(d); });
        var cb = reg(function (r) { delete cbs[dcb]; wrap(done)(r); });
        o.dcb = dcb; o.cb = cb;
        try { call('http.stream', o); } catch (e) { delete cbs[dcb]; delete cbs[cb]; throw e; }
      }
    },
    // 2.0：ieml.exec.run({ id, args: [], timeoutMs }, function (err, res) {})   res = { exitCode, stdout, stderr, timedOut, outputTruncated }
    exec: {
      run: function (opts, fn) {
        opts = opts || {};
        call('exec.run', {
          id: String(opts.id), args: (opts.args || []).map(String), timeoutMs: Number(opts.timeoutMs) || 0, cb: reg(wrap(fn))
        });
      }
    }
  };
  g.__dispatch = function (kind, json) {
    var p = json ? JSON.parse(json) : null, out;
    if (kind === 'page.render') {
      var pg = pages[p.id];
      out = pg && typeof pg.render === 'function' ? pg.render(p.values || {}) : { type: 'text', text: '（這個頁面沒有 render 函式）' };
    } else if (kind === 'page.action') {
      var pg2 = pages[p.id];
      if (pg2 && typeof pg2.onAction === 'function') pg2.onAction(p.action, p.values || {});
    } else if (kind === 'event') {
      var hs = handlers[p.name] || [];
      for (var i = 0; i < hs.length; i++) { try { hs[i](p.data); } catch (e) { call('log', '事件處理錯誤：' + e); } }
    } else if (kind === 'callback') {
      var f = cbs[p.id]; if (!p.keep) delete cbs[p.id];
      if (f) f(p.result);
    }
    return out === undefined ? null : JSON.stringify(out);
  };
})(this);
""";
}
