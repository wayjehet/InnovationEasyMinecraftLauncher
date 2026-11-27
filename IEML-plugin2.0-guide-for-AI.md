# IEML 插件開發指南（給 AI 讀的）

> 使用方式：把整份文件貼給 AI，再說明你要的插件功能。AI 應輸出 `plugin.json` 與 `main.js` 兩個檔案。

## 你的任務
你要為 Innovation Easy Minecraft Launcher（IEML）寫插件。插件是**純 JavaScript 腳本**，跑在 Jint 沙盒裡，只能透過全域物件 `ieml` 與啟動器互動。請嚴格遵守下面的規則，不要使用未列出的 API。

## 輸出格式
一個插件 = 一個資料夾，**資料夾名稱必須等於 manifest 的 `id`**：
```
plugins/my-plugin/
  plugin.json
  main.js
```

### plugin.json
```json
{
  "id": "my-plugin",
  "name": "我的插件",
  "version": "1.0.0",
  "author": "作者",
  "description": "一句話說明（≤300 字）",
  "main": "main.js",
  "permissions": ["ui.page", "ui.toast"],
  "networkHosts": []
}
```
規則：
- `id`：小寫英數、`.` `_` `-`，2–40 字，開頭必須是英數。
- `name` ≤ 40 字、`author` ≤ 40 字、`version` ≤ 20 字。
- `main`：資料夾內的相對 `.js` 路徑，不能含 `..`。`main.js` ≤ 512 KB。
- `permissions` 只能用下表列出的權限，**只宣告真正需要的**。
- 宣告 `net.http` 就**必須**填 `networkHosts`（小寫完整網域，例如 `"api.github.com"`；不能有 `https://`、路徑、萬用字元、連接埠）。

## 權限（預設全關，使用者要逐項勾選）
| 權限 | 解鎖的 API |
|---|---|
| `ui.page` | `ieml.ui.registerPage`、`ieml.ui.openPage` |
| `ui.dialog` | （已開放，不需宣告）`ieml.ui.confirm`、`ieml.ui.info`、`ieml.ui.choose` 現在不需要任何權限；每分鐘最多 6 個、同時只能 1 個 |
| `ui.toast` | `ieml.ui.toast` |
| `hooks` | `ieml.on(...)` 收事件 |
| `storage` | `ieml.storage.get/set/remove`（總量 ≤ 200 KB） |
| `instances.read` | `ieml.instances.list()` |
| `accounts.read` | `ieml.accounts.list()` |
| `token.read` ⚠ | `ieml.accounts.getToken()`（正版帳號存取權杖，高風險） |
| `net.http` ⚠ | `ieml.http.get(url)`、`ieml.http.request`、`ieml.http.stream`（只限 `networkHosts` 的 HTTPS） |
| `exec` ⚠⚠ | `ieml.exec.run`（只能執行 manifest 宣告、已核對 sha256 的程式；需要 apiVersion 2） |

`ieml.log(text)` 不需要權限。沒權限就呼叫會丟出 `Error`，請用 `try/catch` 包住。

## API 參考
```js
ieml.log(text)

ieml.on('launcher.ready', function () {})                 // 需要 hooks
ieml.on('instance.launch', function (info) {})            // info = { name, mcVersion, loader }

ieml.ui.registerPage({ id, title, subtitle, render(values), onAction(action, values) })
ieml.ui.openPage(id)
ieml.ui.toast(text, isError)
ieml.ui.confirm(title, message, function (ok) {})         // ok: true/false
ieml.ui.info(title, message, function () {})
ieml.ui.choose(title, message, ['A', 'B'], function (i) {})  // i = 按鈕索引，Esc = -1

ieml.storage.get(key)            // 回傳字串或 null
ieml.storage.set(key, value)     // 非字串會自動 JSON.stringify
ieml.storage.remove(key)

ieml.instances.list()            // [{ id, name, mcVersion, loader }]
ieml.accounts.list()             // [{ id, name, type: 'microsoft'|'offline', selected }]
ieml.accounts.getToken()         // 字串；目前選的不是正版帳號會丟 Error

ieml.http.get(url)               // 同步，回傳 { status, body }（body 是字串）
```

## 插件 2.0（manifest 加 `"apiVersion": 2` 才能用；不需要就不要加）
```js
// 非同步 HTTP：callback(err, res)；res = { status, body, truncated, contentType }
ieml.http.request({ method: 'POST', url: 'https://api.example.com/v1/x',
                    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ a: 1 }),
                    secret: 'mykey', timeoutMs: 20000 }, function (err, res) {});

// SSE 串流（AI 逐字輸出）：onData(data) 每個事件一次；done(err, res) 結束時一次
ieml.http.stream({ method: 'POST', url: '…', body: '…', secret: 'mykey' },
                 function (data) {}, function (err, res) {});

// 執行外部程式：res = { exitCode, stdout, stderr, timedOut, outputTruncated }
ieml.exec.run({ id: 'ffmpeg', args: ['-version'], timeoutMs: 10000 }, function (err, res) {});
```
新增的 manifest 欄位（都要 `apiVersion: 2`）：
```json
{
  "apiVersion": 2,
  "networkHosts": ["api.example.com", "api.example.com:8443"],
  "secrets": [{ "name": "mykey", "label": "My API Key", "host": "api.example.com", "header": "Authorization", "prefix": "Bearer " }],
  "exec": [{ "id": "ffmpeg", "file": "bin/ffmpeg.exe", "sha256": "<64 字元十六進位>", "description": "轉檔" }],
  "contributes": { "pins": [{ "id": "chat", "page": "main", "title": "AI 助手", "icon": "🤖", "slot": "more" }] }
}
```
規則：
- **金鑰不要寫進 main.js，也不要叫使用者貼進輸入框。** 宣告 `secrets`，使用者會在插件設定頁填；插件只用 `secret: 'name'` 指名，讀不到內容。`secrets` 的 `host` 必須在 `networkHosts` 內，且需要 `net.http`。
- `networkHosts` 可寫 `host` 或 `host:port`（預設 443）；網址不能用 IP、不能帶帳密、不跟轉址；localhost／內網一律被擋。
- 宣告 `exec` 就必須有 `exec` 權限（反之亦然）；`sha256` 必須是檔案真正的雜湊；每次執行都會彈窗問使用者。
- `contributes.pins` 需要 `ui.page`；`page` 是 `registerPage` 的頁面 id；`title` 1–20 字，不能與內建項目（伺服器查找、遊戲日誌、種子地圖、地圖管理、帳號管理、地城探險、插件…）同名；使用者核准後才會出現在「更多」頁。每插件最多 3 個。
- 2.0 的 callback 是 `(err, res)`；失敗時 `err` 是 Error。沒有 `setTimeout`，要等結果就在 callback 裡把狀態存進變數，頁面會在動作後自動重畫。

## 頁面元件樹
`render(values)` 必須**回傳一個 JSON 物件**（元件樹）。啟動器負責畫，你拿不到任何 UI 物件。

| type | 欄位 |
|---|---|
| `heading` | `text` |
| `text` | `text`、`muted`（灰字，布林） |
| `button` | `label`、`action`（字串）、`primary`（布林） |
| `input` | `id`、`label`、`value` |
| `checkbox` | `id`、`label`、`value`（布林） |
| `list` | `items`（字串陣列） |
| `row` / `column` | `children`（陣列） |
| `separator` | 無 |

限制：深度 ≤ 8、元件總數 ≤ 500、單一文字 ≤ 2000 字。
流程：任何非同步 callback（彈窗、http.request／stream、exec.run）執行完，開著的頁面也會自動重畫（串流逐字輸出直接存進變數即可，不需要「重新整理」按鈕）；**不要在 `render` 裡發請求／彈窗／exec，會變成無限迴圈**。
`values` 是畫面上有 `id` 的 input／checkbox 目前的值 → 使用者按按鈕 → 呼叫 `onAction(action, values)` → 之後**自動重新呼叫 `render(values)`**。所以要顯示的狀態請存在變數或 `ieml.storage`，在 `render` 裡讀出來。

## 沙盒限制（寫程式時必須避開）
- **沒有** `fetch`、`XMLHttpRequest`、`setTimeout`、`setInterval`、`console`、`require`、`import`、`window`、`document`、檔案與程序 API。要記錄請用 `ieml.log`。
- 腳本為 strict mode。每次執行最多 2 秒、32 MB、遞迴 120 層、30 萬條陳述式——不要寫長迴圈或重運算。
- `http.get` 與 `storage` 都是**同步**；對話框的結果只能用 callback 取得。
- `http.get`：只允許 `https://`、網域要完全符合 `networkHosts`、不會跟隨轉址（3xx 會直接回傳 status）、回應 ≤ 1 MB、10 秒逾時。只有 GET。
- 對話框標題會被自動加上「插件「名稱」：」前綴。
- 頁面 id 只能用小寫英數、`_`、`-`（1–40 字），一個插件最多 20 頁；`ieml.on` 最多訂閱 20 種事件。
- 解析 JSON 請用 `JSON.parse`，並用 `try/catch` 處理失敗。

## 撰寫守則
1. 只宣告用得到的權限；不要同時要 `token.read` 和 `net.http`，除非功能絕對需要。`exec` 非必要不要用。
2. 每個可能因權限被拒而失敗的呼叫都用 `try/catch`，並顯示友善訊息。
3. 頁面一律用 `render` 回傳元件樹，不要嘗試操作 DOM。
4. 輸出時給出完整的 `plugin.json` 與 `main.js`，並簡述使用者需要勾選哪些權限。

## 範例 1：計數器頁面
plugin.json
```json
{ "id": "counter", "name": "計數器", "version": "1.0.0", "author": "me",
  "description": "按一下加一", "main": "main.js",
  "permissions": ["ui.page", "ui.toast", "storage"], "networkHosts": [] }
```
main.js
```js
ieml.ui.registerPage({
  id: 'main', title: '計數器', subtitle: '按按看',
  render: function (values) {
    var n = Number(ieml.storage.get('n') || '0');
    return { type: 'column', children: [
      { type: 'heading', text: '目前：' + n },
      { type: 'row', children: [
        { type: 'button', label: '+1', action: 'inc', primary: true },
        { type: 'button', label: '歸零', action: 'reset' }
      ] }
    ] };
  },
  onAction: function (action) {
    var n = Number(ieml.storage.get('n') || '0');
    if (action === 'inc') ieml.storage.set('n', n + 1);
    if (action === 'reset') ieml.storage.set('n', 0);
  }
});
```

## 範例 2：用 net.http 查詢網路資料
plugin.json
```json
{ "id": "net-demo", "name": "網路範例", "version": "1.0.0", "author": "me",
  "description": "示範 http.get", "main": "main.js",
  "permissions": ["ui.page", "net.http"], "networkHosts": ["api.github.com"] }
```
main.js
```js
var result = '尚未查詢';

ieml.ui.registerPage({
  id: 'main', title: '網路範例', subtitle: '查詢 GitHub',
  render: function (values) {
    return { type: 'column', children: [
      { type: 'input', id: 'repo', label: 'owner/repo', value: values.repo || 'microsoft/vscode' },
      { type: 'button', label: '查詢', action: 'go', primary: true },
      { type: 'text', text: result }
    ] };
  },
  onAction: function (action, values) {
    if (action !== 'go') return;
    try {
      var r = ieml.http.get('https://api.github.com/repos/' + encodeURIComponent(values.repo).replace('%2F', '/'));
      if (r.status !== 200) { result = '失敗，狀態碼 ' + r.status; return; }
      var j = JSON.parse(r.body);
      result = j.full_name + '：★ ' + j.stargazers_count;
    } catch (e) {
      result = '錯誤：' + e.message;   // 例如沒授權 net.http
    }
  }
});
```

## 範例 3：啟動實例時提醒
```json
{ "id": "launch-note", "name": "啟動提醒", "version": "1.0.0", "author": "me",
  "description": "啟動時顯示提示", "main": "main.js",
  "permissions": ["hooks", "ui.toast"], "networkHosts": [] }
```
```js
ieml.on('instance.launch', function (info) {
  ieml.ui.toast('即將啟動 ' + info.name + '（' + info.mcVersion + ' ' + info.loader + '）');
});
```
