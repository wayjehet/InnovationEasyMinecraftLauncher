using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using IEML.Core.Models;
using IEML.Core.Plugins;
namespace IEML.App;

/// 插件管理：掃描、啟停、授權、事件派送，以及插件能呼叫的「主程式能力」（都只會從 PluginRuntime.Handle 進來，那裡已過 PluginGate）。
public sealed class PluginManager
{
    readonly MainViewModel _vm;
    readonly PluginStore _store = new();
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })   // 不跟轉址：避免被導到沒允許的網域
    { Timeout = TimeSpan.FromSeconds(10) };

    public List<InstalledPlugin> Installed { get; private set; } = new();
    public Dictionary<string, PluginRuntime> Running { get; } = new();
    public List<string> Logs { get; } = new();
    public event Action? Changed;

    public PluginManager(MainViewModel vm)
    {
        _vm = vm;
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("IEML-Plugin/1.0");
    }

    public void RaiseChanged() => Application.Current.Dispatcher.BeginInvoke(() => Changed?.Invoke());

    public void Log(string id, string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{id}] {text}";
        Debug.WriteLine(line);
        lock (Logs) { Logs.Add(line); if (Logs.Count > 200) Logs.RemoveAt(0); }
    }

    // ───────────── 載入／啟停／授權 ─────────────
    public void LoadAll()
    {
        foreach (var r in Running.Values) r.Stop();
        Running.Clear();
        _store.Load();
        EnsureSample();
        Installed = _store.Discover();
        foreach (var p in Installed.Where(p => p.Error is null))
        {
            var st = _store.StateOf(p.Manifest.Id);
            st.Granted.RemoveAll(g => !p.Manifest.Permissions.Contains(g));   // 新增的權限一律不會自動授權
            if (st.Enabled) StartPlugin(p);
        }
        _store.Save();
        RaiseChanged();
    }

    void StartPlugin(InstalledPlugin p)
    {
        var rt = new PluginRuntime(p, _store.StateOf(p.Manifest.Id), this);
        Running[p.Manifest.Id] = rt;
        rt.Start();
    }

    public PluginState StateOf(string id) => _store.StateOf(id);

    public void SetEnabled(string id, bool on)
    {
        var p = Installed.FirstOrDefault(x => x.Manifest.Id == id);
        if (p is null || p.Error is not null) return;
        _store.StateOf(id).Enabled = on;
        _store.Save();
        if (Running.TryGetValue(id, out var old)) { old.Stop(); Running.Remove(id); }
        if (on) StartPlugin(p);
        RaiseChanged();
    }

    /// 授權／撤銷某項權限。立即生效（閘口每次呼叫都會檢查），不需要重載。
    public void SetGranted(string id, string perm, bool on)
    {
        var p = Installed.FirstOrDefault(x => x.Manifest.Id == id);
        if (p is null || !p.Manifest.Permissions.Contains(perm)) return;
        var st = _store.StateOf(id);
        st.Granted.Remove(perm);
        if (on) st.Granted.Add(perm);
        _store.Save();
    }

    public void OpenFolder()
    {
        Directory.CreateDirectory(PluginStore.Dir);
        try { Process.Start(new ProcessStartInfo("explorer.exe", PluginStore.Dir) { UseShellExecute = true }); } catch { }
    }

    // ───────────── 事件 ─────────────
    /// 通知所有「已啟用、有 hooks 權限、訂閱了這個事件」的插件。data 只放它們有權知道的最小資訊。
    public void Emit(string name, JsonNode? data)
    {
        foreach (var rt in Running.Values.ToList())
        {
            if (!rt.Loaded || !rt.EventNames.Contains(name)) continue;
            if (!PluginGate.Has(rt.Plugin.Manifest, rt.State, Perm.Hooks)) continue;
            rt.Dispatch("event", new JsonObject { ["name"] = name, ["data"] = data?.DeepClone() });
        }
    }

    // ───────────── 介面 ─────────────
    public void Toast(string text, bool error) =>
        Application.Current.Dispatcher.BeginInvoke(() => (Application.Current.MainWindow as MainWindow)?.ShowToast(text, error));

    public void OpenPageDeferred(PluginRuntime rt, string pageId) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!rt.Loaded || !rt.Pages.TryGetValue(pageId, out var info)) return;
            (Application.Current.MainWindow as MainWindow)?.ShowPage($"{info.Title}", $"插件「{rt.Plugin.Manifest.Name}」提供的頁面", new PluginPageView(rt, pageId), push: true);
        });

    // ───────────── 插件能讀到的資料（都是最小集合） ─────────────
    public JsonNode InstancesJson()
    {
        var arr = new JsonArray();
        foreach (var i in _vm.Instances)
            arr.Add(new JsonObject { ["id"] = i.Id.ToString(), ["name"] = i.Name, ["mcVersion"] = i.McVersion, ["loader"] = i.Loader.ToString() });
        return arr;
    }

    public JsonNode AccountsJson()
    {
        var arr = new JsonArray();
        var selected = _vm.SelectedAccount?.Id;
        foreach (var a in _vm.Accounts)
            arr.Add(new JsonObject
            {
                ["id"] = a.Id.ToString(), ["name"] = a.Name,
                ["type"] = a.Type == AccountType.Microsoft ? "microsoft" : "offline",
                ["selected"] = a.Id == selected
            });
        return arr;
    }

    public string GetToken() => _vm.GetSelectedAccountToken()
        ?? throw new InvalidOperationException("目前選的不是正版帳號，或取得權杖失敗／逾時。");

    /// net.http：只允許 HTTPS、manifest 列出的網域（完全相符）、不跟轉址、回應上限 1 MB、10 秒。會阻塞介面執行緒到完成（原型的取捨）。
    public JsonNode HttpGet(PluginManifest m, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("只允許 https 網址");
        if (!m.NetworkHosts.Contains(uri.Host.ToLowerInvariant()))
            throw new PluginDeniedException($"網域 {uri.Host} 不在這個插件的 networkHosts 內");
        var task = Task.Run(async () =>
        {
            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            await using var s = await resp.Content.ReadAsStreamAsync();
            var buf = new MemoryStream();
            var chunk = new byte[8192]; int n;
            while ((n = await s.ReadAsync(chunk)) > 0)
            {
                buf.Write(chunk, 0, n);
                if (buf.Length > 1024 * 1024) throw new InvalidOperationException("回應超過 1 MB");
            }
            return new JsonObject { ["status"] = (int)resp.StatusCode, ["body"] = Encoding.UTF8.GetString(buf.ToArray()) };
        });
        if (!task.Wait(TimeSpan.FromSeconds(12))) throw new TimeoutException("請求逾時");
        return task.Result;
    }

    // ───────────── 範例插件 ─────────────
    void EnsureSample()
    {
        var dir = PluginStore.Dir;
        Directory.CreateDirectory(dir);
        if (Directory.EnumerateDirectories(dir).Any()) return;
        var sample = Path.Combine(dir, "hello-sample");
        Directory.CreateDirectory(sample);
        File.WriteAllText(Path.Combine(sample, "plugin.json"), SampleManifest, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(sample, "main.js"), SampleMain, new UTF8Encoding(false));
    }

    const string SampleManifest = """
{
  "id": "hello-sample",
  "name": "範例插件",
  "version": "1.0.0",
  "author": "IEML",
  "description": "示範頁面、彈窗、儲存與事件。預設停用，到「更多 → 插件」啟用並勾選權限。",
  "main": "main.js",
  "permissions": ["ui.page", "ui.toast", "ui.dialog", "storage", "instances.read", "hooks"]
}
""";

    const string SampleMain = """
// 範例插件：頁面（render／onAction）、輸入框、按鈕、彈窗、儲存、事件。
// 權限沒勾的話，對應的呼叫會丟出 Error（可以用 try/catch 接住）。
ieml.ui.registerPage({
  id: 'hello',
  title: '你好，插件',
  subtitle: '示範頁面',

  // values＝頁面上有 id 的輸入框／勾選框目前的值；回傳要顯示的元件樹
  render: function (values) {
    var count = Number(ieml.storage.get('count') || '0');
    var insts = [];
    try { insts = ieml.instances.list(); } catch (e) { /* 沒有 instances.read 權限 */ }
    return {
      type: 'column',
      children: [
        { type: 'heading', text: '你好，' + (values.name || '陌生人') + '！' },
        { type: 'text', muted: true, text: '這個頁面是用 JavaScript 寫的。你按過「打招呼」 ' + count + ' 次。' },
        { type: 'input', id: 'name', label: '你的名字', value: values.name || '' },
        { type: 'row', children: [
          { type: 'button', label: '打招呼', action: 'hi', primary: true },
          { type: 'button', label: '問個問題', action: 'ask' }
        ] },
        { type: 'separator' },
        { type: 'text', text: '目前的實例：' },
        { type: 'list', items: insts.map(function (i) { return i.name + '（' + i.mcVersion + ' ' + i.loader + '）'; }) }
      ]
    };
  },

  onAction: function (action, values) {
    if (action === 'hi') {
      ieml.storage.set('count', Number(ieml.storage.get('count') || '0') + 1);
      ieml.ui.toast('你好，' + (values.name || '陌生人') + '！');
    } else if (action === 'ask') {
      ieml.ui.confirm('確認', '要記住你的名字嗎？', function (ok) {
        if (ok) ieml.storage.set('name', values.name || '');
        ieml.ui.toast(ok ? '記住了！' : '好吧。');
      });
    }
  }
});

// 事件：啟動實例前通知（需要 hooks 權限）
ieml.on('instance.launch', function (info) {
  ieml.ui.toast('範例插件：即將啟動「' + info.name + '」');
});
""";
}
