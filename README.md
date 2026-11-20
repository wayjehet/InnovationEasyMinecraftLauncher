# Innovation Easy Minecraft Launcher（IEML）

一個以 .NET 8 + WPF 撰寫的 Minecraft Java 版啟動器，以 Windows 為主要目標平台。

> ⚠️ **可能不穩定**
> 部分功能尚未經過完整測試，可能不穩定或有錯誤。歡迎回報問題。

> 🤖 **AI 協同製作聲明**
> 本專案大部分程式碼、文件與介面預覽由 AI（Claude）協助撰寫，再由作者整合與測試。詳見下方〈[AI 協同製作提醒](#ai-協同製作提醒)〉。

## 功能

- **實例管理**：多個獨立實例，各自的版本、載入器、Java、記憶體與 JVM 參數
- **帳號**：離線帳號與 Microsoft 正版登入，帳號可綁定實例；主畫面快速啟動卡片可用不同帳號啟動同一實例
- **載入器**：Fabric、Quilt、Forge、NeoForge（含 1.20.1 舊命名）；實例設置可挑選載入器版本（自動＝最新穩定版，也可手動輸入），換載入器種類時會提醒模組可能失效
- **Java 管理**：自動偵測本機 Java，找不到合適版本時從 Adoptium 下載
- **內容安裝**：
  - Modrinth：模組、光影、資源包、整合包（`.mrpack`）
  - CurseForge：模組、光影、資源包、整合包（需自備 API 金鑰）
  - 本機匯入整合包（CurseForge `.zip` / Modrinth `.mrpack`）
  - 模組自動安裝必要相依；下載後檢查 SHA-1；優先安裝正式版
  - Modrinth 搜尋篩選：分類、載入器（光影為 Iris / OptiFine / Canvas）、執行端（客戶端／伺服器）、Minecraft 版本；排序（相關度、下載數、追蹤數、最新、最近更新）；分頁載入更多
  - 已安裝內容管理：列出模組／光影／資源包，勾選＝啟用／停用（檔名加 `.disabled`，不刪檔）、刪除、檢查並更新到新的正式版
  - 地圖：匯入 zip／資料夾、貼 zip 直連網址下載、搜尋 CurseForge 地圖分類（需 API 金鑰）；自動找出 `level.dat` 所在資料夾放進 `saves`，不會覆蓋同名地圖；可備份成 zip、刪除
- **伺服器**：伺服器查找（Server List Ping、SRV 解析）、收藏、公開伺服器搜尋
- **連線房間**：透過 [EasyTier](https://github.com/EasyTier/EasyTier) 建立虛擬區域網路，與朋友連線
- **種子地圖**：以 [Cubiomes](https://github.com/Cubitect/cubiomes) 查詢結構與群系（需自行編譯原生 DLL，見 `native/README.md`）
- **遊戲日誌**：即時顯示遊戲輸出，並自動遮蔽存取權杖
- **音樂播放**：主畫面內建簡易音樂播放器
- **共用快取**：不同實例共用 libraries / assets，節省空間與下載時間
- **下載管理器**（`Services/Download/`，整合中）：暫存檔 `.part` 驗證後改名、HTTP Range 暫停／繼續、自動重試、全域並行上限；呼叫端替換步驟見 [`docs/DOWNLOAD-INTEGRATION.md`](docs/DOWNLOAD-INTEGRATION.md)

## 系統需求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（從原始碼建置）或 .NET 8 Desktop Runtime（執行已發佈版本）

## 從原始碼建置

```powershell
git clone <本倉庫網址>
cd InnovationLauncher
dotnet build
dotnet run --project src/IEML.App
```

發佈成單一 exe：

```powershell
./publish.ps1                 # 需要使用者安裝 .NET 8 Desktop Runtime
./publish.ps1 -SelfContained  # 內含 runtime，檔案較大
```

輸出在 `dist\`。

### 種子地圖（選用）

種子地圖需要 `ieml_cubiomes.dll`。依 [`native/README.md`](native/README.md) 編譯後放到 `native\`，建置時會自動複製到輸出資料夾。沒有這個 DLL 時其他功能不受影響。

## 使用說明

### CurseForge API 金鑰

CurseForge 的搜尋與整合包安裝需要你自己的 API 金鑰：

1. 到 [console.curseforge.com](https://console.curseforge.com) 申請
2. 在啟動器的 CurseForge 視窗上方貼上並儲存

金鑰只存在你的電腦，以 Windows 帳號（DPAPI）加密，不會寫進程式碼。匯入 Modrinth `.mrpack` 不需要金鑰。

作者不開放第三方啟動器下載的檔案，啟動器**不會繞過**限制，會列在實例資料夾的 `manual-downloads.txt`，請到 CurseForge 網站手動下載。

### 資料位置

所有資料存放在 `%AppData%\InnovationEasyLauncher`：

| 路徑 | 內容 |
| --- | --- |
| `accounts.json` | 帳號清單（重新整理權杖以 DPAPI 加密） |
| `instances\<id>\` | 每個實例的設定、`mods`、`shaderpacks`、`resourcepacks`、`saves` |
| `shared\` | 共用的 libraries / assets 快取 |
| `tools\` | 載入器安裝程式、EasyTier 等工具 |
| `java\` | 自動下載的 Java |

## 專案結構

```
src/
  IEML.Core/   不含 UI：資料模型、Contracts（服務介面）、Services（實作）
  IEML.App/    WPF 介面
native/        Cubiomes 的 C 包裝與編譯說明
docs/          介面預覽（ui-preview，用瀏覽器開）、下載管理器整合說明
HANDOFF.md     開發進度與待實測清單
```

技術：.NET 8、WPF、CommunityToolkit.Mvvm、CmlLib.Core 4.x、NAudio。

## 已知限制

- 專案仍在早期階段，部分功能尚未實測（詳見 `HANDOFF.md` 的待實測清單）
- 部分程式碼由 AI 在沒有編譯環境的情況下撰寫，第一次建置可能需要修正編譯錯誤
- 下載管理器尚未接到所有下載呼叫端；CmlLib 下載的原版檔案與 Forge / NeoForge 安裝程式的函式庫不經過它
- CurseForge 視窗仍是簡單搜尋，尚無 Modrinth 那樣的篩選
- 介面仍使用 WPF 預設樣式，之後會美化
- 目前僅支援 Windows

## AI 協同製作提醒

本專案由作者與 AI 助手（Anthropic Claude）協同製作。使用或貢獻前請知道：

- **AI 會出錯**：AI 產生的程式碼可能有錯誤、過時的 API 用法或安全疑慮。許多部分是憑記憶撰寫、**尚未編譯或實測**，進度與待驗證項目見 `HANDOFF.md`。
- **請自行審查**：重要功能（Microsoft 登入、權杖加密、檔案下載與解壓、以管理員權限啟動 EasyTier）在日常使用前，建議先閱讀原始碼。
- **外部服務細節可能有誤**：EasyTier 參數、公開伺服器 API 欄位、Modrinth／CurseForge 回應格式等，AI 可能記錯，請以官方文件為準。
- **貢獻請標明**：若你的 Pull Request 大量使用 AI 產生，請在說明中註明，並確認你已實際建置與測試。
- **授權與來源**：AI 產生的內容可能與既有程式碼相似；如發現侵權疑慮，請開 Issue 告知，我們會儘快處理。
- **不收集資料**：AI 協作僅發生在開發階段，啟動器本身不會把你的帳號、權杖或日誌送給任何 AI 服務。

## 回報問題

請到本倉庫的 Issues 回報，並附上：

- 啟動器版本與 Windows 版本
- 使用的 Minecraft 版本與載入器
- 「遊戲日誌」視窗的內容（權杖已自動遮蔽，貼出前仍請再檢查一次）

## 授權

本專案以 [MIT 授權](LICENSE) 釋出。

## 致謝

CmlLib.Core、Fabric、Quilt、Forge、NeoForge、Modrinth、CurseForge、EasyTier、Cubiomes、Adoptium 等專案與服務，以及協助開發的 AI 工具。

本專案與 Mojang Studios、Microsoft 無關，亦非其官方產品。Minecraft 是 Mojang Studios 的商標。
