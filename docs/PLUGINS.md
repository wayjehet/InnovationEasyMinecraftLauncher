# IEML 插件系統（原型）

插件是**用 JavaScript 寫的腳本**，在啟動器內的沙盒（[Jint](https://github.com/sebastienros/jint)）裡執行。
插件可以新增頁面、顯示彈窗、接收事件、存少量資料；**預設全部停用、沒有任何權限**，每項權限都要使用者逐項勾選。

## 資料夾與載入
- 插件資料夾：`%AppData%\InnovationEasyLauncher\plugins\`（「更多 → 插件 → 開啟插件資料夾」）
- 一個插件 = 一個子資料夾，**資料夾名稱必須等於 manifest 的 `id`**：
  ```
  plugins/
    my-plugin/
      plugin.json
      main.js
  ```
- 第一次執行、插件資料夾是空的時候，會自動放一個 `hello-sample` 範例（預設停用）。

## plugin.json
```json
{
  "id": "my-plugin",
  "name": "我的插件",
  "version": "1.0.0",
  "author": "你",
  "description": "一句話說明",
  "main": "main.js",
  "permissions": ["ui.page", "ui.toast"],
  "networkHosts": []
}
```
- `id`：小寫英數、`.` `_` `-`，2–40 字。`main`：插件資料夾內的 `.js` 相對路徑。
- `networkHosts`：只有宣告 `net.http` 時才需要，列出允許連線的網域（小寫、完全相符、不含路徑與萬用字元）。

## 權限（全部預設關閉）
| 權限 | 能做什麼 | 風險 |
|---|---|---|
| `ui.page` | 新增頁面（在「更多 → 插件」開啟） | |
| `ui.dialog` | 顯示確認／提示／選擇彈窗（標題會標示來源插件） | |
| `ui.toast` | 顯示提示條 | |
| `hooks` | 接收事件：`launcher.ready`、`instance.launch` | |
| `storage` | 在自己的資料夾存少量資料（上限 200 KB） | |
| `instances.read` | 讀取實例清單（id、名稱、MC 版本、載入器；**不含路徑**） | |
| `accounts.read` | 讀取帳號清單（id、名稱、正版／離線、是否選取；**不含 UUID、權杖**） | |
| `token.read` | 讀取目前選取的正版帳號的存取權杖 | ⚠ 高：能以該帳號身分登入遊戲 |
| `net.http` | 對 `networkHosts` 的網域發 HTTPS GET | ⚠ 高：可能把你開放給它的資料傳出去 |

- 授權＝**manifest 有宣告 ∩ 使用者有勾選**。插件更新後新增的權限不會自動授權。
- 撤銷權限立即生效（每次呼叫都重新檢查）。

## API（全域物件 `ieml`）
```js
ieml.log(text)                                   // 寫進插件紀錄（不需權限）
ieml.on('instance.launch', function (info) {})   // hooks：info = { name, mcVersion, loader }
ieml.on('launcher.ready', function () {})

ieml.ui.registerPage({ id, title, subtitle, render(values), onAction(action, values) })   // ui.page
ieml.ui.openPage(id)                              // ui.page
ieml.ui.toast(text, isError)                      // ui.toast
ieml.ui.confirm(title, message, function (ok) {})            // ui.dialog，ok = true/false
ieml.ui.info(title, message, function () {})                 // ui.dialog
ieml.ui.choose(title, message, ['A', 'B'], function (i) {})  // ui.dialog，i = 按鈕索引，Esc = -1

ieml.storage.get(key) / .set(key, value) / .remove(key)      // storage（值是字串；物件請自己 JSON.stringify）
ieml.instances.list()                             // instances.read → [{ id, name, mcVersion, loader }]
ieml.accounts.list()                              // accounts.read → [{ id, name, type, selected }]
ieml.accounts.getToken()                          // token.read → 字串
ieml.http.get(url)                                // net.http → { status, body }
```
沒有權限的呼叫會丟出 `Error`（可 `try/catch`）。對話框與 HTTP 之外都是同步的；對話框的結果用 callback 取得。

## 頁面：宣告式元件樹
`render(values)` 回傳一棵 JSON 元件樹，啟動器負責畫；插件拿不到任何 WPF 物件。
`values` 是頁面上有 `id` 的輸入框／勾選框目前的值；按鈕觸發 `onAction(action, values)` 後會重新呼叫 `render(values)`。

| type | 欄位 |
|---|---|
| `heading` | `text` |
| `text` | `text`、`muted`（灰字） |
| `button` | `label`、`action`、`primary`（實色按鈕） |
| `input` | `id`、`label`、`value` |
| `checkbox` | `id`、`label`、`value`（布林） |
| `list` | `items`（字串陣列） |
| `row` / `column` | `children` |
| `separator` | |

限制：深度 ≤ 8、元件總數 ≤ 500、文字 ≤ 2000 字。

## 沙盒保證與限制
**保證（由主程式強制，插件寫什麼都繞不過）：**
- 插件是腳本，在獨立的 Jint 引擎裡跑；**沒有開 CLR 互通**，看不到任何 .NET 型別，沒有檔案、網路、程序、環境變數。
- 唯一的出口是 `__host(method, json)`；**每次呼叫都先過 `PluginGate.Authorize`**（`IEML.Core/Plugins/PluginGate.cs`）。
  方法不在表中＝拒絕；權限沒宣告或沒授權＝拒絕；插件沒啟用＝拒絕。要審查沙盒，只要讀這個檔案與 `PluginRuntime.Handle`。
- 資源限制：每次執行 2 秒逾時、32 MB 記憶體、遞迴 120 層、30 萬條陳述式。
- 彈窗標題固定加上「插件「名稱」：」前綴，避免假冒系統對話框。
- `net.http`：只允許 HTTPS、manifest 列的網域、不跟轉址、回應 ≤ 1 MB、10 秒。
- 帳號資料：永遠不提供 UUID／refresh token；`token.read` 是唯一能拿到存取權杖的路，且預設關閉。

**限制（原型的已知問題）：**
- 插件看得到的東西，一旦同時授權了 `net.http`，就可能被傳出去；請把這兩種權限分開看待。
- 同一個 Windows 使用者下的其他程式本來就讀得到啟動器的資料；沙盒只約束「插件」，不是作業系統層級的隔離。
- `net.http` 與 `token.read` 會在介面執行緒上等待（最多 12／10 秒），期間畫面會暫時無回應。
- 沒有防 DNS 重新綁定（網域比對是以名稱為準）。
- 沒有插件簽章／來源驗證／市集；目前只靠「使用者自己放進資料夾」。

## 加新 API 的流程（給開發者）
1. 在 `Perm`（`PluginModel.cs`）新增權限常數與說明（如需要）。
2. 在 `PluginGate.Methods` 登記方法 → 需要的權限。**沒登記就一律拒絕。**
3. 在 `PluginRuntime.Handle` 加對應的 `case`（資料只回傳最小必要集合、驗證所有輸入長度）。
4. 在 `Prelude` 的 `ieml` 物件加包裝函式，並更新這份文件。
