# Innovation Easy Minecraft Launcher（IEML）

**繁體中文** | [English](README.en.md)

[![版本](https://img.shields.io/github/v/release/wayjehet/InnovationEasyMinecraftLauncher?label=%E7%89%88%E6%9C%AC&color=00FFFF&cacheSeconds=300)](https://github.com/wayjehet/InnovationEasyMinecraftLauncher/releases)
[![建置](https://img.shields.io/github/actions/workflow/status/wayjehet/InnovationEasyMinecraftLauncher/build.yml?label=%E5%BB%BA%E7%BD%AE)](https://github.com/wayjehet/InnovationEasyMinecraftLauncher/actions/workflows/build.yml)
[![授權](https://img.shields.io/badge/%E6%8E%88%E6%AC%8A-MIT-blue)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![平台](https://img.shields.io/badge/%E5%B9%B3%E5%8F%B0-Windows-0078D6)
![狀態](https://img.shields.io/badge/%E7%8B%80%E6%85%8B-%E6%AD%A3%E5%BC%8F%E7%89%88-brightgreen)
![AI 協同製作](https://img.shields.io/badge/AI-%E5%8D%94%E5%90%8C%E8%A3%BD%E4%BD%9C-8A2BE2)

一個以 **.NET 8 + WPF** 撰寫的 Minecraft Java 版啟動器，以 Windows 為主要目標平台。

> ⚠️ **可能不穩定**
> 許多功能是在沒有編譯環境下撰寫、尚未完整實測，可能不穩定或有錯誤。歡迎回報問題。

> 🤖 **AI 協同製作聲明**
> 本專案大部分程式碼、文件與介面預覽由 AI（Claude）協助撰寫，再由作者整合與測試。詳見〈[AI 協同製作提醒](#ai-協同製作提醒)〉。

## 目錄

- [功能總覽](#功能總覽)
- [介面導覽](#介面導覽)
- [系統需求](#系統需求)
- [安裝與執行](#安裝與執行)
- [從原始碼建置](#從原始碼建置)
- [使用說明](#使用說明)
- [安全性設計](#安全性設計)
- [專案結構](#專案結構)
- [已知限制](#已知限制)
- [AI 協同製作提醒](#ai-協同製作提醒)
- [回報問題](#回報問題)
- [授權與致謝](#授權與致謝)

## 功能總覽

### 實例與啟動
- **多實例管理**：每個實例有獨立的 Minecraft 版本、載入器、載入器版本、Java 路徑、最大記憶體與額外 JVM 參數。
- **版本清單**：可切換顯示正式版，或連同快照與舊版一起顯示；建立實例時可依版本與載入器自動命名，也可自行改名。
- **載入器**：Fabric、Quilt、Forge、NeoForge（含 1.20.1 舊命名）。
  - 實例設置可挑選載入器版本（「自動」＝最新穩定版，也可手動輸入）。
  - 換載入器種類且已有模組時會先警告；Minecraft 版本改變而載入器版本不在新清單中，會自動改回「自動」。
- **Java 管理**：自動偵測本機 Java；找不到合適版本時從 Adoptium 下載到 `java\`；也可手動指定 Java 路徑。
- **共用快取**：不同實例共用 libraries / assets，節省空間與下載時間。
- **遊戲日誌**：即時顯示遊戲輸出與啟動錯誤，並自動遮蔽存取權杖。

### 帳號
- **離線帳號**：UUID 以 `MD5("OfflinePlayer:" + 名稱)` 產生，與原版一致；可重新命名、刪除。
- **Microsoft 正版登入**：可同時保存多個帳號，各自獨立的登入快取，下次啟動靜默登入。
- 帳號可綁定到實例；主畫面可用不同帳號啟動同一實例。
- 正版帳號會從 mc-heads.net 抓皮膚頭像顯示在主畫面。

### 內容安裝（模組／光影／資源包／整合包）
- **Modrinth**：模組、光影、資源包、整合包（`.mrpack`）。
  - 篩選：分類、載入器（光影為 Iris / OptiFine / Canvas）、執行端（客戶端／伺服器）、Minecraft 版本。
  - 排序：相關度、下載數、追蹤數、最新發布、最近更新；可分頁載入更多。
- **選擇指定版本**：點搜尋結果進入項目詳情頁，列出所有版本（版本號、發布類型、支援的 Minecraft 版本與載入器、日期、下載數）與更新內容；可只顯示符合目前實例的版本，自動安裝必要前置、勾選安裝可選前置。Modrinth 與 CurseForge 皆支援（整合包可選特定版本建立實例）。
- **CurseForge**：模組、光影、資源包、整合包（需自備 API 金鑰）。
- **本機匯入整合包**：CurseForge `.zip`、Modrinth `.mrpack`。
- **相依處理**：模組會自動安裝必要的相依；優先安裝正式版。
- **下載驗證**：必須有官方雜湊（SHA-1，沒有才用 MD5）才會安裝，不符就刪檔並失敗。
- **已安裝內容管理**：列出模組／光影／資源包；勾選＝啟用／停用（檔名加 `.disabled`，不刪檔）；刪除；開啟資料夾；檢查並更新到新的正式版（可「全部更新」，更新時先驗證再刪舊檔，並保留停用狀態）。
- **拖放匯入**：把資源包／光影的 `.zip`、模組的 `.jar` 直接拖進「模組與內容」視窗。
  - 會檢查是否為有效 zip；資源包要有根目錄的 `pack.mcmeta`，光影要有根目錄的 `shaders/`（多包一層資料夾會被拒絕並說明原因）。
  - 不覆蓋同名檔案（自動加 `(1)`、`(2)`…）；先寫 `.part` 再改名，中途失敗不會留下半個檔案。
  - 適合作者不開放第三方啟動器下載、自己用瀏覽器下載的檔案。
- **作者不開放第三方下載的檔案**：啟動器**不會繞過**限制，會列在實例資料夾的 `manual-downloads.txt`，請到 CurseForge 網站手動下載。

### 地圖
- 匯入 zip／資料夾、貼 zip 直連網址下載、搜尋 CurseForge 地圖分類（需 API 金鑰）。
- 自動找出最淺的 `level.dat` 所在資料夾放進 `saves`，防 Zip Slip，不會覆蓋同名地圖（自動加 `(2)`）。
- 可備份成 `backups\<名稱>-時間.zip`、刪除、開啟資料夾。
- 入口：下載頁的「地圖」分頁，或「更多 → 地圖管理」。

### 連線與伺服器
- **陶瓦聯機（Terracotta）**：P2P 預設方案。開房、輸入房間碼加入、顯示狀態與成員、複製房間碼／直連位址；房間碼格式 `U/XXXX-XXXX-XXXX-XXXX`，與 HMCL、PCL-CE 互通。啟動器只下載**未修改**的官方執行檔並以獨立行程執行，透過本機 HTTP API 操作。
- **EasyTier 房間（進階）**：透過 [EasyTier](https://github.com/EasyTier/EasyTier) 建立虛擬區域網路，與朋友連線（會要求系統管理員權限建立虛擬網卡）。
- **伺服器查找**：Server List Ping、SRV 解析、即時狀態查詢、收藏。
- **公開伺服器搜尋**：瀏覽與搜尋公開伺服器（資料來源 newminecraftservers.net），可翻頁、即時查詢、加入收藏、複製位址。

### 種子地圖
- 以 [Cubiomes](https://github.com/Cubitect/cubiomes) 查詢結構座標、查中心點群系、複製座標。
- 倉庫已附編好的 x64 `native\ieml_cubiomes.dll`，沒有這個 DLL 時其他功能不受影響。

### 音樂播放
- 內建簡易播放器：播放／暫停／上一首／下一首、音量，可選擇音樂資料夾（遞迴掃描）。
- 支援 `.mp3`、`.wav`、`.flac`、`.m4a`、`.wma`。

### 外觀與個人化
- 深色主題（`#0F0F0F` 底、`#1C1C1C` 面板、青色 `#00FFFF` 強調色）。
- **主題配色**：在設置頁用 R／G／B 拉條調整七個部分（強調色、視窗背景、面板／卡片底色、外框與分隔線、主要文字、圖標／次要文字、強調色上的文字），拖動時即時套用；可「恢復預設」，下次開啟仍保留（存在 `theme.json`）。
- **素材換皮（自訂圖片）**：視窗背景、標誌、頭像外框、面板、快捷卡片、對話框、選單、各種按鈕（含滑過／按下／停用狀態圖）、輸入框、下拉選單、分頁、進度條都能換成自己的圖。設置頁可直接「選圖」，也可把 PNG 以部位名放進素材資料夾，**變更立刻套用、不用重開**；支援九宮格拉伸與像素風，並可匯出／匯入素材包（zip）分享。沒放的圖使用預設樣式（部位清單與建議尺寸見 `src/IEML.App/Assets/README.txt`）。
- **快捷啟動卡片**：中欄的卡片可自由拖拉到畫布任何位置，位置會存檔；按下的卡片會拉到最上層；每張卡片有「啟動」「設置」按鈕；視窗縮小時跑出畫面的卡片會自動拉回；設置頁可「重設快捷卡片位置」。

### 更新與打包
- **自動更新**：啟動時（最多每 12 小時一次）檢查 GitHub 最新正式版；**一定要使用者同意**才會下載，不會靜默更新。可選「更新」「稍後」「略過這個版本」；可在設置關閉自動檢查，或按「檢查更新」手動檢查。
  - 驗證 SHA-256，可選 ECDSA P-256 簽章；防 Zip Slip；換檔失敗會自動還原；`.old` 備份在下次啟動時清除。
  - 詳見 [`docs/UPDATE.md`](docs/UPDATE.md)。
- **下載管理器**（`Services/Download/`，整合中）：`.part` 暫存驗證後改名、HTTP Range 暫停／繼續、自動重試、全域並行上限；呼叫端替換步驟見 [`docs/DOWNLOAD-INTEGRATION.md`](docs/DOWNLOAD-INTEGRATION.md)。
- **GitHub Actions**：推 `v*` 標籤會自動建置自包含單檔版、壓縮成 zip 並發佈 Release，同時附上 `.sha256`（有設定簽章金鑰時還有 `.sig`）。

## 介面導覽

主畫面為三欄版面（視窗 980×640，可縮放），比例約 **左 311 : 中 551 : 右 117**。

| 區域 | 內容 |
| --- | --- |
| **左欄** | IEML 標誌、頭像（點擊開帳號管理）、實例名稱／版本／載入器、帳號下拉、進度條與狀態文字、**啟動**、〔實例設置〕〔實例選擇〕 |
| **中欄** | 快捷啟動卡片（可拖拉、位置存檔）、「＋ 新增卡片」；沒有卡片時顯示提示 |
| **右欄** | **設置**、**下載**、**P2P**、**更多**、**音樂**；點選後中欄切換成對應的頁面，按「← 返回」回首頁 |

各頁面內容：

- **設置**：帳號管理、CurseForge API 金鑰、主題配色、素材資料夾與素材清單、重設卡片位置、版本資訊、檢查更新／自動更新開關、GitHub 專案頁。
- **下載**：分頁「遊戲」（版本清單＋建立實例）、「內容」（Modrinth／CurseForge 搜尋、選版本與安裝）、「地圖」；頁首有「匯入整合包」按鈕。已安裝的模組／光影／資源包到「實例設置」管理。
- **P2P**：陶瓦聯機、EasyTier 房間。
- **更多**：伺服器查找、遊戲日誌、種子地圖、地圖管理、帳號管理。
- **音樂**：播放控制、音量、音樂資料夾。

## 系統需求

- Windows 10 / 11
- 執行已發佈的自包含版本：**不需要**另外安裝 .NET
- 從原始碼建置：[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 使用 `publish.ps1` 預設輸出（非自包含）時：.NET 8 Desktop Runtime

## 安裝與執行

1. 到本倉庫的 **Releases** 下載最新的 `InnovationEasyMinecraftLauncher-vX.Y.Z-NET8-SelfContained.zip`。
2. 解壓縮到**自己的資料夾**（不要放在 `Program Files`，否則自動更新無法換檔）。
3. 執行 `InnovationEasyMinecraftLauncher.exe`。

Windows SmartScreen 可能對新下載的 exe 顯示警告（專案沒有程式碼簽署憑證），與更新機制無關。

## 從原始碼建置

```powershell
git clone https://github.com/wayjehet/InnovationEasyMinecraftLauncher
cd InnovationEasyMinecraftLauncher
dotnet build
dotnet run --project src/IEML.App
```

發佈成單一 exe：

```powershell
./publish.ps1                 # 需要使用者安裝 .NET 8 Desktop Runtime
./publish.ps1 -SelfContained  # 內含 runtime，檔案較大
```

輸出在 `dist\`。`native\ieml_cubiomes.dll` 存在時會自動複製到輸出資料夾。

### 發佈新版本

1. 推一個版本標籤，例如 `v1.3.1`（或在 Actions 手動執行「Build and Release」並填入版本標籤）。
2. GitHub Actions 會用**標籤**當版本號建置（數字部分＝程式版本，完整標籤去掉開頭的 `v`＝畫面上顯示的版本），並發佈 Release；不需要事先改 `IEML.App.csproj`。
3. `IEML.App.csproj` 的 `<Version>`／`<InformationalVersion>` 只影響本機自己編譯的版本，記得偶爾跟上最新發佈的版本號。

> 更新程式只看版本號開頭的數字：`1.2.1EF` 與 `1.2.1EG` 會被當成同一版，**不會互相提示更新**。要讓舊版收到更新，數字一定要變大。

### 種子地圖 DLL（選用）

倉庫已附 x64 版 `ieml_cubiomes.dll`。要更新或改位元數時，依 [`native/README.md`](native/README.md) 重新編譯。

## 使用說明

### CurseForge API 金鑰

CurseForge 的搜尋與整合包安裝需要 API 金鑰（匯入 Modrinth `.mrpack` 不需要）：

1. 到 [console.curseforge.com](https://console.curseforge.com) 申請。
2. 在「設置」頁或 CurseForge 視窗貼上並儲存。

金鑰只存在你的電腦，以 Windows 帳號（DPAPI）加密，不會寫進程式碼。金鑰優先順序：**使用者自己填的 > 打包內建的**（倉庫裡的內建金鑰永遠是空字串）。

### 登入 Microsoft 帳號

「設置 → 開啟帳號管理 → 新增正版帳號」，依視窗指示登入。登入後下次啟動會靜默登入；如果每次都要重登，請把「遊戲日誌」附在回報裡。

### 把自己下載的檔案加進實例

先在主畫面選好實例，打開「模組與內容」視窗，把 `.jar`（模組）或 `.zip`（資源包／光影）拖進去。`.zip` 預設當資源包；目前選的是光影才會當光影。

### 啟用更新簽章（選用，一次性）

建議啟用，可擋住「GitHub 帳號或 Release 被竄改」的情況。步驟見 [`docs/UPDATE.md`](docs/UPDATE.md)。**私鑰不要放進倉庫，也不要貼給任何人（包括 AI）。**

### 資料位置

所有資料存放在 `%AppData%\InnovationEasyLauncher`：

| 路徑 | 內容 |
| --- | --- |
| `accounts.json` | 帳號清單（重新整理權杖以 DPAPI 加密） |
| `ms\*.dat` | Microsoft 登入快取（DPAPI 加密） |
| `instances\<id>\` | 每個實例的設定、`mods`、`shaderpacks`、`resourcepacks`、`saves`、`manual-downloads.txt` |
| `shared\` | 共用的 libraries / assets 快取 |
| `tools\` | 載入器安裝程式、EasyTier、陶瓦聯機（`tools\terracotta\`）等工具 |
| `java\` | 自動下載的 Java |
| `backups\` | 地圖備份 |
| `Assets\` | 自訂素材 PNG（優先於 exe 旁的 `Assets\`） |
| `theme.json` | 主題配色 |
| `update.json` | 自動更新偏好（是否自動檢查、上次檢查時間、略過的版本） |
| `curseforge.json` | 你的 CurseForge 金鑰（加密） |

另外：
- `%LocalAppData%\InnovationEasyLauncher\tmp\`：登入時的暫存檔（用完立即抹除）。
- `%LocalAppData%\InnovationEasyLauncher\update\`：更新下載與 `apply-update.log`。

## 安全性設計

- **登入憑證**：Microsoft 登入快取（含 Microsoft／Xbox／Minecraft 權杖）以 **Windows DPAPI（目前使用者）加密**後存成 `ms\*.dat`，複製到 USB、別台電腦或別的 Windows 使用者都解不開。登入時才暫時解密成暫存檔，用完立刻重新加密並以 0 覆寫後刪除；上次當機殘留的暫存檔會在下次啟動時清掉。舊版留下的明文 `ms\*.json` 會自動遷移並刪除。
- **用途隔離**：登入快取、CurseForge 金鑰、帳號 refresh token 使用不同的 DPAPI entropy；舊格式可讀並自動升級。解密後的位元組用完即從記憶體抹除。
- **限制**：DPAPI 擋不住「同一個 Windows 使用者身分下執行的惡意程式」，也擋不住有人在你登入的狀態下直接使用你的電腦。請勿在不信任的電腦上登入，離開前記得鎖定畫面。SSD 的磨損平均機制也無法保證覆寫抹除乾淨。
- 啟動遊戲時，存取權杖仍會傳給遊戲處理程序（所有啟動器都是這樣），啟動器的遊戲日誌會遮蔽它。
- 如果懷疑權杖外洩，到 Microsoft 帳戶的安全性頁面登出所有裝置或更改密碼，舊權杖就會失效。
- **下載驗證**：CurseForge／Modrinth 的檔案必須有官方雜湊，網域限定 https 的 CurseForge／Modrinth／GitHub／GitLab（含轉址後的最終網址）；雜湊不符就刪檔並失敗，沒有雜湊就拒絕安裝。雜湊只能證明「檔案沒被竄改」，不能證明「模組本身安全」，請只安裝可信來源的模組。
- **整合包匯入**：路徑穿越（Zip Slip）直接中止；`overrides/` 裡的 `.exe`、`.bat`、`.cmd`、`.ps1`、`.vbs`、`.msi`、`.lnk` 等可執行檔一律不安裝；`.mrpack` 內的下載網域限縮到規格允許的 Modrinth／GitHub／GitLab；整合包自帶、不在下載清單內的 `.jar` 會在遊戲日誌警告（很多正常整合包都會自帶 jar，所以不直接封鎖）。
- **啟動外部程式**：陶瓦與 EasyTier 的指令列參數改用 `ArgumentList`，不拼字串；以管理員身分執行的 PowerShell 路徑會檢查不安全字元。
- **自動更新**：只從官方倉庫 Release 下載；網域白名單；驗證 SHA-256（可選簽章）；沒有「靜默更新」。

## 專案結構

```
src/
  IEML.Core/   不含 UI：資料模型、Contracts（服務介面）、Services（實作）
  IEML.App/    WPF 介面（視窗、主題 Theme.xaml、Assets 素材）
native/        Cubiomes 的 C 包裝、編好的 DLL 與編譯說明
third_party/   隨附的第三方二進位檔（官方陶瓦聯機，未修改）與其授權說明
docs/          介面預覽（ui-preview，用瀏覽器開）、下載管理器整合說明、自動更新說明
.github/       GitHub Actions（建置與發佈）
HANDOFF.md     開發進度與待實測清單
```

技術：.NET 8、WPF、CommunityToolkit.Mvvm、CmlLib.Core 4.x、CmlLib.Core.Auth.Microsoft 3.x、NAudio。

## 已知限制

- 專案仍在早期階段，許多功能尚未實測（詳見 `HANDOFF.md` 的待實測清單）。
- 部分程式碼由 AI 在沒有編譯環境的情況下撰寫，第一次建置可能需要修正編譯錯誤（CmlLib 的 API 名稱最可能出問題）。
- 下載管理器尚未接到所有下載呼叫端；CmlLib 下載的原版檔案與 Forge / NeoForge 安裝程式的函式庫不經過它。
- 缺少官方雜湊的舊檔案會被拒絕安裝（顯示「沒有官方檢查碼可驗證」）。
- 發行包已內附官方陶瓦聯機 v0.4.2（x64，未修改，放在 `third_party\terracotta\`），第一次使用時複製到 `tools\terracotta\`。ARM 電腦或內附檔案被刪掉時，會改從官方 Release 下載（`terracotta-<版本>-windows-<架構>-pkg.tar.gz`）；若官方日後改了附件檔名而找不到，請手動把 `terracotta*.exe`（連同同資料夾的 DLL）放進該資料夾。
- 更新簽章尚未啟用時，只驗證 SHA-256。
- 勾選框、捲軸、滑桿、清單項目、提示條等少數部件還不能換圖。
- 程式碼中少數寫死的顏色（部分輸入框底色、灰字）不會跟著主題配色變；拉條本身仍是 WPF 預設樣式。
- Modrinth 沒有地圖分類，Planet Minecraft 等網站沒有公開 API，地圖只能先用瀏覽器下載再匯入。
- 目前僅支援 Windows。

## AI 協同製作提醒

本專案由作者與 AI 助手（Anthropic Claude）協同製作。使用或貢獻前請知道：

- **AI 會出錯**：AI 產生的程式碼可能有錯誤、過時的 API 用法或安全疑慮。許多部分是憑記憶撰寫、**尚未編譯或實測**，進度與待驗證項目見 `HANDOFF.md`。
- **請自行審查**：重要功能（Microsoft 登入、權杖加密、檔案下載與解壓、自動更新、以管理員權限啟動 EasyTier）在日常使用前，建議先閱讀原始碼。
- **外部服務細節可能有誤**：EasyTier 參數、陶瓦 API、公開伺服器 API 欄位、Modrinth／CurseForge 回應格式等，AI 可能記錯，請以官方文件為準。
- **貢獻請標明**：若你的 Pull Request 大量使用 AI 產生，請在說明中註明，並確認你已實際建置與測試。
- **授權與來源**：AI 產生的內容可能與既有程式碼相似；如發現侵權疑慮，請開 Issue 告知，我們會儘快處理。
- **不收集資料**：AI 協作僅發生在開發階段，啟動器本身不會把你的帳號、權杖或日誌送給任何 AI 服務。

## 回報問題

請到本倉庫的 Issues 回報，並附上：

- 啟動器版本（設置頁的「關於」）與 Windows 版本
- 使用的 Minecraft 版本與載入器
- 「遊戲日誌」視窗的內容（權杖已自動遮蔽，貼出前仍請再檢查一次）

## 授權與致謝

本專案以 [MIT 授權](LICENSE) 釋出。

**陶瓦聯機 Terracotta © burningtnt（AGPL-3.0-or-later，附例外條款）**：發行包以「打包未修改二進位檔」的方式內附官方執行檔（`third_party\terracotta\`，含來源、SHA-256 與授權說明的 `NOTICE.txt`），必要時才從官方 Release 下載；啟動器以獨立行程透過 HTTP API 與它互動，不連結、不修改，並依授權要求在介面明顯處標示其版權資訊。這兩種用法都落在上游 AGPL 例外條款內；上游的 `LICENSE` 全文也一併附在該資料夾。

感謝 CmlLib.Core、Fabric、Quilt、Forge、NeoForge、Modrinth、CurseForge、EasyTier、Terracotta、Cubiomes、Adoptium、NAudio、mc-heads.net 等專案與服務，以及協助開發的 AI 工具。

本專案與 Mojang Studios、Microsoft 無關，亦非其官方產品。Minecraft 是 Mojang Studios 的商標。
