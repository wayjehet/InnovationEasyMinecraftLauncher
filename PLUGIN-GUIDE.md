# IEML 插件製作指導手冊

> 給想為 Innovation Easy Minecraft Launcher（IEML）寫插件的人。
> 規格細節與沙盒設計請看 [`PLUGINS.md`](PLUGINS.md)；要請 AI 幫你寫，請把 `IEML-plugin-guide-for-AI.md` 整份貼給它。

---

## 目錄
1. [先搞懂三件事](#1-先搞懂三件事)
2. [五分鐘做出第一個插件](#2-五分鐘做出第一個插件)
3. [plugin.json 完整說明](#3-pluginjson-完整說明)
4. [權限](#4-權限)
5. [撰寫 main.js：API 速查](#5-撰寫-mainjsapi-速查)
6. [頁面：render 與 onAction](#6-頁面render-與-onaction)
7. [沙盒限制（先看這個少踩雷）](#7-沙盒限制)
8. [範例集](#8-範例集)
9. [插件 2.0（非同步網路、金鑰、exec、釘選）](#9-插件-20)
10. [除錯](#10-除錯)
11. [發佈前檢查清單](#11-發佈前檢查清單)
12. [常見問題](#12-常見問題)

---

## 1. 先搞懂三件事

**① 插件是純 JavaScript，跑在沙盒裡。**
沒有檔案、網路、程序、環境變數，也看不到任何 .NET 物件。插件唯一能做的事，就是呼叫全域物件 `ieml` 上列出來的方法。

**② 敏感能力都由使用者逐項勾選。**（彈窗是例外：不需要權限，但有頻率限制。）
你在 `plugin.json` 宣告想要的權限，但**宣告 ≠ 擁有**。使用者要到「更多 → 插件」把插件啟用、再把權限一項項勾起來才算數。沒權限就呼叫，會丟出 `Error`——所以每個可能被拒絕的呼叫都要 `try/catch`。

**③ 畫面不是你畫的，是你「描述」的。**
插件拿不到任何 WPF 控制項。你的 `render()` 回傳一棵 JSON 元件樹，啟動器負責畫；使用者按了按鈕，啟動器呼叫你的 `onAction()`，然後重新呼叫 `render()`。

---

## 2. 五分鐘做出第一個插件

### 步驟
1. 開啟 IEML →「更多 → 插件 → 開啟插件資料夾」（路徑：`%AppData%\InnovationEasyLauncher\plugins\`）。
2. 在裡面建立資料夾 `counter`（**資料夾名稱必須等於 manifest 的 `id`**）。
3. 放入兩個檔案：

`plugins/counter/plugin.json`
```json
{
  "id": "counter",
  "name": "計數器",
  "version": "1.0.0",
  "author": "你的名字",
  "description": "按一下加一",
  "main": "main.js",
  "permissions": ["ui.page", "ui.toast", "storage"],
  "networkHosts": []
}
```

`plugins/counter/main.js`
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
    if (action === 'inc') { ieml.storage.set('n', n + 1); ieml.ui.toast('+1'); }
    if (action === 'reset') ieml.storage.set('n', 0);
  }
});
```
4. 回到「更多 → 插件」按「重新載入」。
5. 在「計數器」卡片：勾「啟用這個插件」→ 勾三個權限 → 按「開啟：計數器」。

> 小提醒：改了 `main.js` 之後，要按「重新載入」才會生效。權限的勾選／撤銷則是**立即生效**，不用重載。

---

## 3. plugin.json 完整說明

```json
{
  "id": "my-plugin",
  "name": "我的插件",
  "version": "1.0.0",
  "author": "作者",
  "description": "一句話說明",
  "main": "main.js",
  "permissions": ["ui.page", "ui.toast"],
  "networkHosts": []
}
```

| 欄位 | 規則 |
|---|---|
| `id` | 小寫英數、`.` `_` `-`，2–40 字，開頭必須是英數。**必須等於資料夾名稱**，否則插件被拒載。 |
| `name` | 必填，≤ 40 字。 |
| `version` | ≤ 20 字。**更新插件時請務必提高版本號**（2.0 的 exec 同意、釘選核准會依版本／內容失效）。 |
| `author` | ≤ 40 字。 |
| `description` | ≤ 300 字。 |
| `main` | 插件資料夾內的相對 `.js` 路徑，不能含 `..`。`main.js` 檔案 ≤ 512 KB。 |
| `permissions` | 只能用第 4 節列出的權限。**只宣告真正需要的。** |
| `networkHosts` | 宣告 `net.http` 就必須填：小寫完整網域，如 `"api.github.com"`；不能有 `https://`、路徑、萬用字元。2.0 起可寫 `"host:port"`。 |
| `apiVersion` | 預設 1。要用 2.0 功能（見第 9 節）才寫 `2`。 |

manifest 有任何錯誤，插件卡片會顯示紅字「無法載入：…」並說明原因。

---

## 4. 權限

預設全部關閉。

| 權限 | 解鎖的 API | 風險 |
|---|---|---|
| `ui.page` | `ieml.ui.registerPage`、`ieml.ui.openPage` | |
| `ui.dialog` | （已開放）彈窗現在**不需要權限**；這個權限只為相容舊插件而保留，不用宣告 | |
| `ui.toast` | `ieml.ui.toast` | |
| `hooks` | `ieml.on(...)` 接收事件 | |
| `storage` | `ieml.storage.get/set/remove`（總量 ≤ 200 KB） | |
| `instances.read` | `ieml.instances.list()`（不含路徑） | |
| `accounts.read` | `ieml.accounts.list()`（不含 UUID、權杖） | |
| `token.read` | `ieml.accounts.getToken()` | ⚠ 高：能以該帳號身分登入遊戲 |
| `net.http` | `ieml.http.get`；2.0：`http.request`、`http.stream` | ⚠ 高：可能把資料傳出去 |
| `exec` | 2.0：`ieml.exec.run` | ⚠⚠ 最高：外部程式以使用者權限執行 |

**設計原則：**
- 只要真正會用到的權限。要求太多，使用者不會裝。
- 不要同時要 `token.read` 和 `net.http`，除非功能絕對需要——合在一起就等於能把使用者的登入權杖傳出去。
- 插件更新後**新增的權限不會自動授權**，使用者要重新勾選。

---

## 5. 撰寫 main.js：API 速查

```js
ieml.log(text)                              // 不需權限；寫進插件紀錄（≤ 500 字）

// 事件（需要 hooks）
ieml.on('launcher.ready', function () {})
ieml.on('instance.launch', function (info) {})   // info = { name, mcVersion, loader }

// 介面
ieml.ui.registerPage({ id, title, subtitle, render(values), onAction(action, values) })  // ui.page
ieml.ui.openPage(id)                                       // ui.page
ieml.ui.toast(text, isError)                               // ui.toast（≤ 200 字）
ieml.ui.confirm(title, message, function (ok) {})          // 不需權限；ok = true/false
ieml.ui.info(title, message, function () {})               // 不需權限
ieml.ui.choose(title, message, ['A','B'], function (i) {}) // 不需權限；i = 按鈕索引，Esc = -1

// 儲存（storage）
ieml.storage.get(key)            // 字串或 null
ieml.storage.set(key, value)     // 非字串會自動 JSON.stringify
ieml.storage.remove(key)

// 讀取資料
ieml.instances.list()            // [{ id, name, mcVersion, loader }]                 instances.read
ieml.accounts.list()             // [{ id, name, type: 'microsoft'|'offline', selected }]  accounts.read
ieml.accounts.getToken()         // 字串；目前選的不是正版帳號會丟 Error              token.read

// 網路（net.http）
ieml.http.get(url)               // 同步，回傳 { status, body }；1.0 舊介面
```

重點細節：
- **對話框標題會被自動加上「插件「名稱」：」前綴**，使用者永遠分得出是誰在問。對話框訊息 ≤ 800 字，按鈕最多 4 個、每個 ≤ 30 字。
- **彈窗不需要權限，但有頻率限制**：同時只能有一個彈窗在等使用者回應，每分鐘最多 6 個，超過會丟 Error。請只在真的需要使用者決定時才彈，不要拿來顯示一般訊息（那是 `toast` 的工作）。插件必須已啟用才能彈。
- 對話框的結果**只能用 callback 拿**，沒有同步版本。
- `storage` 的資料存在插件資料夾的 `data.json`；key ≤ 100 字，總量 ≤ 200 KB，超過會丟 Error。
- 一個插件最多 20 個頁面、最多訂閱 20 種事件。
- 頁面 id 只能用小寫英數、`_`、`-`（1–40 字）。

---

## 6. 頁面：render 與 onAction

### 運作流程
```
使用者開啟頁面 ─→ render({})
使用者改了輸入框、按下按鈕 ─→ onAction(action, values) ─→ 自動重新 render(values)
```
- `values`＝畫面上**有 `id` 的** `input`／`checkbox` 目前的值（`{ 欄位id: 值 }`）。
- 要保留狀態，請存在**模組層變數**或 `ieml.storage`，在 `render` 裡讀出來。`render` 每次都是重新跑一遍。
- 想保留使用者剛輸入的文字，在 `render` 裡用 `value: values.欄位id || ''`。

### 元件一覽

| type | 欄位 | 說明 |
|---|---|---|
| `heading` | `text` | 大標題 |
| `text` | `text`、`muted`（布林） | 一般文字；`muted: true` 為灰字 |
| `button` | `label`、`action`、`primary` | 按下後呼叫 `onAction(action, values)`；`primary` 是主色按鈕 |
| `input` | `id`、`label`、`value` | 單行輸入框 |
| `checkbox` | `id`、`label`、`value`（布林） | 勾選框 |
| `list` | `items`（字串陣列） | 項目清單（最多 100 項） |
| `row` / `column` | `children` | 橫向（會換行）／直向排列 |
| `separator` | 無 | 分隔線 |

**限制**：深度 ≤ 8、元件總數 ≤ 500、單一文字 ≤ 2000 字、`row` 最多 50 個子項、`column` 最多 100 個。超過會被截斷或顯示「…」。

### 非同步結果會自動重畫
`ui.confirm`／`info`／`choose`、`http.request`、`http.stream`、`exec.run` 的結果都是之後才回來的 callback。**每次 callback 執行完，目前開著的插件頁會自動重新呼叫 `render`**（短時間內的多次通知會合併，約每 150 毫秒最多重畫一次，所以串流逐字輸出也很順）。

所以標準寫法就是：在 callback 裡把結果存進變數，`render` 裡讀出來顯示，不用再做別的。

自動重畫時啟動器會：
- 保留使用者**目前輸入框／勾選框的值**（不會被你 `render` 裡寫的預設值蓋掉），以及輸入框的游標與捲動位置；
- 只在這個頁面還開著時重畫，離開頁面就停止。

注意事項：
- **不要在 `render` 裡發網路請求、彈窗或執行外部程式**——它們的 callback 會觸發重畫，重畫又再發一次，變成無限迴圈。這類動作請放在 `onAction` 或事件處理函式裡。
- `render` 要保持輕量（單次執行 ≤ 2 秒），因為串流時它會被頻繁呼叫。
- 只更動 `storage` 或模組變數、沒有 callback 的情況（例如 `launcher.ready` 事件處理）不會觸發重畫；那種情況頁面會在使用者下次操作或重開時更新。

---

## 7. 沙盒限制

**沒有這些東西**：`fetch`、`XMLHttpRequest`、`setTimeout`、`setInterval`、`console`、`require`、`import`、`window`、`document`、檔案與程序 API。要記錄請用 `ieml.log`。

**執行限制**（每次進入插件程式碼都會重新計算）：

| 項目 | 上限 |
|---|---|
| 單次執行時間 | 2 秒 |
| 記憶體 | 32 MB |
| 遞迴深度 | 120 層 |
| 陳述式數量 | 30 萬條 |

→ 不要寫長迴圈或重運算；要處理大量資料，請分批。

**其他：**
- 腳本是 strict mode。
- `storage.*`、`http.get` 都是同步的；對話框與 2.0 的網路／exec 都是 callback。
- 解析 JSON 請用 `JSON.parse` 並 `try/catch`。
- 沒有「背景執行」：插件只在 載入、事件、頁面 render／action、callback 這些時刻才會執行。

---

## 8. 範例集

### 8.1 啟動實例時提醒（hooks）
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

### 8.2 列出實例、輸入框與確認對話框
```js
ieml.ui.registerPage({
  id: 'main', title: '實例清單', subtitle: '示範輸入框與彈窗',
  render: function (values) {
    var insts = [];
    try { insts = ieml.instances.list(); }               // 沒有 instances.read 權限時會丟 Error
    catch (e) { return { type: 'text', text: '請先到插件設定勾選「讀取實例清單」。' }; }
    return { type: 'column', children: [
      { type: 'heading', text: '共 ' + insts.length + ' 個實例' },
      { type: 'list', items: insts.map(function (i) { return i.name + '（' + i.mcVersion + ' ' + i.loader + '）'; }) },
      { type: 'input', id: 'note', label: '備註', value: values.note || ieml.storage.get('note') || '' },
      { type: 'button', label: '儲存備註', action: 'save', primary: true }
    ] };
  },
  onAction: function (action, values) {
    if (action !== 'save') return;
    ieml.ui.confirm('儲存', '要儲存這段備註嗎？', function (ok) {
      if (ok) ieml.storage.set('note', values.note || '');
    });
  }
});
```
權限：`ui.page`、`storage`、`instances.read`（`confirm` 彈窗不需要權限）。

### 8.3 查詢網路資料（net.http，1.0 同步版）
```json
{ "id": "net-demo", "name": "網路範例", "version": "1.0.0", "author": "me",
  "description": "示範 http.get", "main": "main.js",
  "permissions": ["ui.page", "net.http"], "networkHosts": ["api.github.com"] }
```
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
      var r = ieml.http.get('https://api.github.com/repos/' + values.repo);
      if (r.status !== 200) { result = '失敗，狀態碼 ' + r.status; return; }
      var j = JSON.parse(r.body);
      result = j.full_name + '：★ ' + j.stargazers_count;
    } catch (e) { result = '錯誤：' + e.message; }
  }
});
```
> 同步的 `http.get` 會讓介面凍結到完成（最多 12 秒）。新插件請優先用第 9 節的非同步版。

---

## 9. 插件 2.0

在 manifest 加上 `"apiVersion": 2` 才能使用。**1.0 插件完全不受影響**；沒宣告 2 的插件呼叫這些方法會被拒絕。

### 9.1 非同步 HTTP（`net.http`）
```js
ieml.http.request({
  method: 'POST',                                   // GET／POST／PUT／PATCH／DELETE
  url: 'https://api.example.com/v1/x',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ a: 1 }),                    // 文字，≤ 1 MB；GET 不能帶 body
  secret: 'mykey',                                   // 選填：指名金鑰（見 9.2）
  timeoutMs: 20000                                   // 選填，1000–60000，預設 30000
}, function (err, res) {
  if (err) { /* err 是 Error */ return; }
  // res = { status, body, truncated, contentType }
});
```
- 只能 `https`；網址的主機必須完全符合 `networkHosts` 其中一條（`"host"` 預設 443，或 `"host:port"`）。
- 網址**不能用 IP**、不能帶帳密；**不跟轉址**（3xx 直接回傳）；不存 cookie；不走系統 Proxy。
- 連線當下會檢查實際 IP，**localhost／內網一律被擋**（所以不能連本機的 Ollama 之類）。
- 回應 ≤ 2 MB（`truncated: true` 表示被截斷）；每個插件同時最多 4 個請求。
- 你不能自己設 `Host`、`Cookie`、`Content-Length`、`Proxy-*` 等 header。

### 9.2 金鑰保管（`secrets`）
**絕對不要把 API key 寫進 `main.js`，也不要叫使用者貼進輸入框**——那樣金鑰就在插件手上，插件被改壞或外流就洩漏。請宣告 `secrets`：

```json
"permissions": ["ui.page", "net.http"],
"networkHosts": ["api.openai.com"],
"secrets": [
  { "name": "openai", "label": "OpenAI API Key", "host": "api.openai.com",
    "header": "Authorization", "prefix": "Bearer " }
]
```
- 使用者在「更多 → 插件」你的插件卡片裡填入金鑰（用 Windows DPAPI 加密保存）。
- 你的程式只用 `secret: 'openai'` **指名**，主程式在送出請求時才把 header 注入；**你永遠讀不到金鑰內容**。
- 金鑰只能送往它綁定的 `host`；`host` 必須出現在 `networkHosts`。
- 最多 4 把；`name` 為小寫英數、`_`、`-`（1–32 字）。
- 沒填金鑰就呼叫，callback 會收到錯誤「找不到金鑰…請到插件設定頁填入」。建議在頁面提示使用者先去設定。

### 9.3 SSE 串流（AI 逐字輸出）
```js
ieml.http.stream({ method: 'POST', url: '…', headers: {…}, body: '…', secret: 'openai' },
  function (data) { /* 每個 SSE 事件的 data 內容（字串） */ },
  function (err, res) { /* 結束時一次；res = { status, truncated, events, body } */ });
```
- `res.body` 只有在回應不是 2xx 時才有內容（錯誤本文）；2xx 時資料都從第二個參數（onData）來。
- 閒置 30 秒沒資料會逾時、全程最多 5 分鐘。
- 串流進行中如果使用者撤銷 `net.http` 或停用插件，串流會立刻中止。

**完整範例：簡易 AI 聊天（串流＋金鑰＋釘選）**

`plugin.json`
```json
{
  "id": "mini-chat", "name": "迷你聊天", "version": "1.0.0", "author": "me",
  "description": "串流 AI 聊天範例", "main": "main.js",
  "apiVersion": 2,
  "permissions": ["ui.page", "net.http"],
  "networkHosts": ["api.openai.com"],
  "secrets": [{ "name": "openai", "label": "OpenAI API Key", "host": "api.openai.com", "header": "Authorization", "prefix": "Bearer " }],
  "contributes": { "pins": [{ "id": "chat", "page": "main", "title": "迷你聊天", "icon": "💬", "slot": "more" }] }
}
```
`main.js`
```js
var answer = '', status = '', busy = false;

ieml.ui.registerPage({
  id: 'main', title: '迷你聊天', subtitle: '串流輸出',
  render: function (values) {
    return { type: 'column', children: [
      { type: 'input', id: 'q', label: '問題', value: values.q || '' },
      { type: 'row', children: [
        { type: 'button', label: busy ? '回答中…' : '送出', action: 'ask', primary: true }
      ] },      // 串流的每個事件都會自動重畫，不需要「重新整理」按鈕
      { type: 'text', muted: true, text: status },
      { type: 'text', text: answer }
    ] };
  },
  onAction: function (action, values) {
    if (action !== 'ask' || busy || !values.q) return;
    busy = true; answer = ''; status = '';
    ieml.http.stream({
      method: 'POST',
      url: 'https://api.openai.com/v1/chat/completions',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ model: 'gpt-4o-mini', stream: true, messages: [{ role: 'user', content: values.q }] }),
      secret: 'openai'
    }, function (data) {
      if (data === '[DONE]') return;
      try {
        var d = JSON.parse(data).choices[0].delta.content;
        if (d) answer += d;
      } catch (e) { /* 忽略無法解析的事件 */ }
    }, function (err, res) {
      busy = false;
      status = err ? '錯誤：' + err.message : (res.status === 200 ? '完成' : '失敗 ' + res.status + '：' + res.body.slice(0, 200));
    });
  }
});
```
> 範例中的 API 路徑與欄位只是示意，請依你實際使用的服務文件調整。

### 9.4 exec：執行外部程式（`exec`，⚠ 高風險）
```json
"apiVersion": 2,
"permissions": ["exec"],
"exec": [
  { "id": "ffmpeg", "file": "bin/ffmpeg.exe", "sha256": "<64 字元十六進位>", "description": "把影片轉成 gif" }
]
```
```js
ieml.exec.run({ id: 'ffmpeg', args: ['-version'], timeoutMs: 10000 }, function (err, res) {
  if (err) { /* 使用者拒絕、雜湊不符、逾時等 */ return; }
  // res = { exitCode, stdout, stderr, timedOut, outputTruncated }
});
```
**取得 sha256**（PowerShell）：
```powershell
Get-FileHash -Algorithm SHA256 .\bin\ffmpeg.exe
```
規則：
- 你只能指名 manifest 宣告的 `id`，**不能給路徑或指令**；`file` 必須是插件資料夾內的相對 `.exe`。
- 不經過 shell，參數用陣列（每個 ≤ 1024 字、最多 32 個）；沒有 stdin；環境變數清空後只給最小集合。
- 執行前核對 sha256，不符就拒絕——所以**每次更新 exe 都要同步更新 manifest 的雜湊**。
- 同一時間全啟動器只能跑一個外部程式；逾時預設 30 秒、最長 5 分鐘；stdout／stderr 各 ≤ 64K 字。
- 使用者勾選 `exec` 時要先同意協議；同意綁定「協議版本＋插件版本＋exec 清單」，任何一項變動就失效。
- **每次執行前，啟動器都會彈窗問使用者**（顯示插件、程式、參數）。使用者拒絕時 callback 收到錯誤「使用者拒絕執行」。
- 程式一旦跑起來，以使用者的 Windows 權限執行，沙盒管不到它。**能不用就不要用。**

### 9.5 釘選頁面到「更多」（`contributes`）
```json
"apiVersion": 2,
"permissions": ["ui.page"],
"contributes": { "pins": [
  { "id": "chat", "page": "main", "title": "AI 助手", "icon": "🤖", "slot": "more" }
] }
```
- 你只能「申請」；使用者要在插件卡片逐項核准，入口才會出現在「更多」頁。
- `page` 要是你用 `registerPage` 註冊的頁面 id；`title` 1–20 字、前後不能有空白、不能含控制或隱形字元、**不能與內建項目同名**；`icon` 選填，1–2 個字元或表情符號。
- 核准綁定 `slot｜page｜title｜icon` 的雜湊：**之後只要改了其中任何一項，使用者就得重新核准**。
- 每個插件最多 3 個入口；「更多」頁所有插件入口加起來最多 12 個。
- 入口顯示為「標題（插件名稱）」，永遠標明來源。插件停用、撤銷 `ui.page`、頁面不存在時，入口立刻消失。

---

## 10. 除錯

| 想知道什麼 | 去哪看 |
|---|---|
| manifest 有沒有寫錯 | 插件卡片的紅字「無法載入：…」 |
| 載入時 JS 報錯 | 卡片上的紅字「載入失敗：…」 |
| 執行時 JS 報錯 | 卡片上的紅字「執行錯誤：…」 |
| 呼叫被拒絕（沒勾權限等） | 頁面底部「最近紀錄（含被拒絕的呼叫）」（顯示最後 12 行） |
| 自己的訊息 | `ieml.log('...')`，也會出現在上面的紀錄 |

常用技巧：
- 開發時**先把權限都勾上**，確認功能正常，再回頭檢查「少勾某權限時，我的插件會不會優雅地降級」。
- 把可能失敗的呼叫包 `try/catch`，失敗時顯示友善訊息，而不是讓頁面變空白。
- 沒有 `console`，不要寫 `console.log`——會直接報錯。
- 頁面空白或只看到「這個頁面沒有內容」：通常是 `render` 丟了例外，或沒有 `return` 元件樹。

---

## 11. 發佈前檢查清單

- [ ] `id` 與資料夾名稱一致，格式正確。
- [ ] `permissions` 只含真正用到的；有 `net.http` 就有 `networkHosts`。
- [ ] 每個可能被拒絕的呼叫都有 `try/catch`，且錯誤訊息對使用者有意義。
- [ ] 沒有用到 `fetch`、`setTimeout`、`console` 等不存在的東西。
- [ ] 沒有長迴圈（單次執行 ≤ 2 秒）。
- [ ] API key 一律走 `secrets`，沒有寫在程式碼裡。
- [ ] `render` 裡沒有發網路請求、彈窗或 exec（否則 callback 會觸發重畫，變成迴圈）。
- [ ] 提高了 `version`（尤其是有 `exec` 或 `contributes` 的插件）。
- [ ] 若有 `exec`：`sha256` 與實際檔案相符，`description` 寫清楚用途。
- [ ] 用一個全新的插件資料夾、**全部權限都沒勾**的狀態測過一次。
- [ ] 打包成 zip 時，壓縮檔裡只有一層插件資料夾（資料夾名＝`id`），並附上簡短的 README 說明用途與需要的權限。

> 目前沒有插件簽章或市集：使用者是「自己把資料夾放進去」。請在說明裡誠實列出每個權限的用途，這是取得信任最有效的方式。

---

## 12. 常見問題

**Q：我改了 main.js 但畫面沒變？**
按「更多 → 插件 → 重新載入」。

**Q：`ieml.ui.confirm` 出現「彈窗太頻繁」或「已經有一個彈窗在等待使用者回應」？**
彈窗不需要權限，但同時只能有一個、每分鐘最多 6 個。把需要使用者決定的事合併成一次詢問，一般提示改用 `toast`。

**Q：呼叫 `ieml.ui.toast` 出現「使用者尚未授權：ui.toast」？**
使用者還沒勾那項權限。請 `try/catch`，並在頁面上提示使用者去勾選。

**Q：`ieml.http.request` 說「不在這個插件的 networkHosts 內」？**
`networkHosts` 必須和網址的主機（與連接埠）完全相同；`api.example.com` 不等於 `www.api.example.com`，也不含子網域。非 443 的埠要寫 `"host:port"`。

**Q：我想連本機的服務（localhost／區網）？**
目前一律被擋（防止插件掃描使用者內網）。

**Q：callback 回來了，畫面卻沒更新？**
callback 後頁面會自動重畫（見 [第 6 節](#非同步結果會自動重畫)）。如果還是沒變：確認你有把結果存進 `render` 讀得到的變數、`render` 沒丟例外（看紀錄），而且那個頁面還開著。

**Q：能不能在背景定時做事？**
不行。沒有 `setTimeout`／`setInterval`，插件只在事件、頁面動作與 callback 時執行。

**Q：`storage` 的資料在哪？**
插件資料夾內的 `data.json`（≤ 200 KB）。使用者刪掉插件資料夾就一起刪除。

**Q：舊插件（沒寫 apiVersion）還能用嗎？**
可以，完全不受影響；只是不能使用第 9 節的功能。

**Q：我的釘選入口被核准後，更新插件就消失了？**
你改了 `title`、`page`、`icon` 或 `slot` 其中之一，核准便失效；使用者需要在插件卡片重新核准。不想讓使用者重做，就不要改這些欄位。

**Q：想請 AI 幫我寫？**
把 `IEML-plugin-guide-for-AI.md` 整份貼給 AI，再描述你要的功能；它會輸出 `plugin.json` 與 `main.js`。拿到後請對照第 11 節的檢查清單自己看過一遍——尤其是權限與金鑰的部分。
