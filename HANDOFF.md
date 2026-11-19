# Innovation Easy Minecraft Launcher — Handoff

Stack: .NET 8, WPF + CommunityToolkit.Mvvm, CmlLib.Core 4.x. Windows-first.

## Layout
- `src/IEML.Core` — no UI. Models, `Contracts/` (all service interfaces), `Services/` (implementations).
- `src/IEML.App` — WPF UI. Currently: instance list, account picker (binds account -> instance), Launch button stub.

## Data (`%AppData%/InnovationEasyLauncher`)
- `accounts.json` — list of `Account` (refresh tokens DPAPI-encrypted)
- `instances/<guid>/instance.json` + `mods/ shaderpacks/ resourcepacks/ saves/`
- `shared/` assets/libs cache, `tools/` easytier-core.exe, `music/`

## Done
Models, AppPaths, JsonStore, AccountService, InstanceService, UI skeleton, instance settings window + quick-launch cards (InstanceConfig.QuickCards; ILaunchService.LaunchAsync has accountIdOverride).

## TODO (each is an interface in Contracts.cs; implement independently)
1. `ILaunchService` — vanilla + offline DONE (LaunchService.cs, uncompiled). TODO: Fabric/Forge, Microsoft session, shared assets cache.
2. `IAccountService.AddMicrosoftAsync` — Microsoft login.
3. `IModrinthService` — search/install mods, shaders, modpacks (.mrpack -> new instance).
4. `IServerPingService` — Server List Ping over TCP (+ SRV lookup).
5. `IEasyTierService` — spawn easytier-core as child process, room name/secret, return virtual IP.
6. `ISeedMapService` — Cubiomes native DLL via P/Invoke.
7. `IMusicService` — NAudio.

Rule: depend on interfaces in `Contracts`, don't change signatures without updating this file.

## Update 3
Done (uncompiled): version picker + new-instance dialog (VersionService), delete instance/account buttons,
instance settings (version/Java/JVM args), ServerPingService (Server List Ping + favorites in servers.json, no SRV yet),
MusicService (NAudio) + music bar in main window.
Remaining: Fabric/Forge, Microsoft login, Modrinth, EasyTier, Cubiomes seed map, Java management, shared assets cache, UI polish, logging.

## Update 4
Done (uncompiled): Fabric/Quilt via official meta profile JSON (LoaderService), Modrinth search/install for mods,
shaders, resource packs (+required deps, sha1 check) and .mrpack modpacks (ModrinthService), ContentWindow UI, loader pickers.
NeoForge/Forge: installer-jar based (LoaderService), uncompiled/untested.
Still TODO: Microsoft login, EasyTier, Cubiomes, Java mgmt, shared cache, UI polish, logging.

## Update 5 — Microsoft login
IAccountService: AddMicrosoftAsync (interactive) + GetSessionAsync(id) (offline, or Microsoft silent->interactive). Contract changed: added GetSessionAsync.
MicrosoftSessions.cs: one CmlLib JELoginHandler + cache file per account at %AppData%/InnovationEasyLauncher/ms/<id>.json.
UI: "新增正版帳號" button; account lists show (正版)/(離線). Uncompiled against CmlLib.Core.Auth.Microsoft 3.x - fix API names if needed.


## 網頁版 Claude 追加：EasyTier 連線房間（待辦 1，未編譯、未實測）
**沒有編譯環境，全部憑記憶寫的，請先 `dotnet build` 修正錯誤。Contracts.cs 沒有動。**

新增檔案：
- `src/IEML.Core/Services/EasyTierService.cs`：實作 `IEasyTierService`
  - 第一次使用時從 GitHub Releases 最新版下載 `easytier-windows-<x86_64|arm64>-*.zip`，解壓到 `tools\easytier\`（有 sha256 digest 就驗證）
  - `StartAsync`：寫入 `room.args`（每行一個參數）→ 以 UAC（runas）啟動 `tools\easytier\easytier-run.ps1` → 腳本讀完參數檔就刪除（密碼不留在磁碟）→ 啟動 easytier-core（`--dhcp --peers <節點> --rpc-portal 127.0.0.1:<隨機埠>`）
  - 因為未提升權限的程式不能結束已提升權限的行程，停止方式 = 寫 `stop.flag`，腳本偵測到就結束 core；啟動器關閉（父行程 PID 消失）時腳本也會結束 core
  - 虛擬 IP：每秒呼叫 `easytier-cli --rpc-portal 127.0.0.1:<埠> node`，用 regex 抓私有 IP（先要求帶 /遮罩，15 秒後放寬），最多等 60 秒
  - 額外公開成員（不在介面裡）：`StatusChanged` 事件、`CurrentRoom`、`VirtualIp`、`ToInviteCode()`、`TryParseInviteCode()`（邀請碼格式 `房號:密碼`，只允許英數 _ -）
  - 預設節點 `tcp://public.easytier.cn:11010`（`DefaultPeer`）
- `src/IEML.App/RoomWindow.xaml` / `.xaml.cs`：開房、加入（貼邀請碼）、顯示房號／密碼／虛擬 IP／邀請碼＋複製按鈕、停止連線、狀態文字

修改既有檔案（只加不改）：
- `MainViewModel.cs`：新增 `_easyTier` 欄位與 `OpenRoomCommand`
- `MainWindow.xaml`：在「模組與內容」按鈕後加「連線房間」按鈕

**需要實測／可能要修的地方（依可疑程度）：**
1. `easytier-cli node` 的輸出格式、`--rpc-portal` 參數名稱（可能是 `-p`）、IP 欄位長相 → 改 `ParseIp` / `TryGetIpAsync`
2. easytier-core 的參數名（`--network-name` `--network-secret` `--peers` `--rpc-portal` `--dhcp`）以實際版本 `--help` 為準
3. GitHub 壓縮檔命名規則（`easytier-windows-x86_64-v*.zip`）與壓縮檔內是否有 `easytier-cli.exe`
4. 提升權限的 PowerShell 行程：`Process.HasExited` / `WaitForExitAsync` 可能拋例外（已 try/catch 當作仍在執行）
5. `public.easytier.cn:11010` 公共節點目前是否可用；之後可讓使用者自訂節點（`EasyTierRoom.PeerUrl`）
6. 房主與朋友都要各自跑一次 UAC；Windows 防火牆可能跳詢問

測試清單：開房 → 出現房號／密碼／虛擬 IP → 另一台輸入邀請碼加入 → 互相 ping 虛擬 IP → 房主單人世界對區域網路開放，朋友用 `虛擬IP:埠` 連線 → 按停止後 easytier-core.exe 確實消失（工作管理員）→ 直接關啟動器後 core 也消失。


## 網頁版 Claude 追加：公開伺服器搜尋（未編譯、未實測）
- 新檔 `src/IEML.Core/Services/PublicServerService.cs`：`PublicServerService.SearchAsync(keyword, page)`，呼叫 NewMinecraftServers 公開 API `GET https://newminecraftservers.net/api/v1/servers?page=&q=`（免金鑰、唯讀；每頁最多 25 筆、頁碼 1–100；未知／重複參數會被拒絕，所以只送 q 與 page）。內建 60 秒快取。
- 資料是站方快取（30–60 秒）、不是即時 ping；要即時狀態請對結果的 `Address` 再呼叫 `ServerPingService.PingAsync`。
- **回應欄位名稱沒查到**（拿不到完整 OpenAPI），目前用寬鬆解析（遞迴找同時有 name 與 address/ip/host 的物件）。請先實測一次回應，必要時改 `Walk()`。
- 錯誤（含 429）會直接丟例外，呼叫端請顯示在 `Status`。
- 還沒接到 UI（`ServerWindow` 尚未加搜尋框）；`IServerPingService.SearchAsync` 的語意是「搜尋收藏」，沒有改。
- 不採用 Gemini 提供的 FindMCServer 爬 HTML 版本：網址有誤、備援解析是空的，且是偽裝瀏覽器爬網頁。


## 網頁版 Claude 追加：公開伺服器 UI（未編譯、未實測）
- 新檔 `src/IEML.App/PublicServerWindow.xaml` / `.xaml.cs`：關鍵字搜尋（Enter 也可）、上一頁／下一頁（每頁 25 筆）、清單、「即時查詢」（對選取項呼叫 `ServerPingService.PingAsync` 補人數與延遲）、「加入收藏」（`SaveFavoriteAsync`）、「複製位址」；錯誤顯示在視窗內狀態列。
- 修改 `ServerWindow.xaml` / `.xaml.cs`：加「瀏覽公開伺服器」按鈕與 `Browse_Click`（只加不改）。
- 仍待實測：`PublicServerService` 的回應欄位解析（見上一節）。


## 網頁版 Claude 追加：遊戲日誌視窗（未編譯、未實測）
- 新檔 `src/IEML.Core/Services/GameLog.cs`：靜態類別，記憶體內保留最近 5000 行；`Start(Process, 實例名, 機密…)` 會設定 stdout/stderr 轉向、啟動行程並讀取輸出（機密字串如存取權杖會被遮蔽成 `***`）；有 `LineAdded` 事件、`Snapshot()`、`Clear()`、`Append()`。
- 修改 `LaunchService.cs`：只把最後的 `process.Start();` 換成 `GameLog.Start(process, cfg.Name, session.AccessToken);`（`ILaunchService` 簽名沒動）。
- 新檔 `src/IEML.App/LogWindow.xaml` / `.xaml.cs`：等寬字體、清除、複製全部、自動捲動；背景輸出先排隊，每 200 ms 批次寫入畫面；超過 60 萬字元會裁掉前半。
- 修改 `MainViewModel.cs`（`OpenLogCommand`）、`MainWindow.xaml`（「遊戲日誌」按鈕）。
- 注意：stdout 以 UTF-8 解碼；新版 Java 在中文 Windows 可能輸出系統碼頁（cp950），若看到亂碼請改 `GameLog.Start` 裡的編碼。啟動前的錯誤（下載、載入器安裝）仍然只顯示在主畫面 `Status`，沒有寫進日誌。
- 待實測：CmlLib 的 Process 預先設定的 StartInfo 是否與這裡的轉向設定衝突。


## 網頁版 Claude 追加：Java 管理（待辦 2，未編譯、未實測）
選擇順序（使用者指定：本機 > 下載、符合 > 較高）：
0. 實例設置手動指定的 `JavaPath`（存在就直接用）
1. 本機已安裝且版本**符合**
2. 本機已安裝且版本**較高**（取最接近的）。例外：需求為 Java 8 的舊版**不使用較高版本**（舊版 Forge/LaunchWrapper 在 Java 9+ 常壞），這是我加的安全限制，要拿掉就改 `JavaService.ResolveAsync` 的 `if (req > 8)`
3. 已下載到 `%AppData%\InnovationEasyLauncher\java\<版本>\` 的符合版本
4. 從 Adoptium（Temurin）下載符合版本的 JRE（`api.adoptium.net/v3/binary/latest/<版本>/ga/windows/<x64|aarch64>/jre/hotspot/normal/eclipse`），解壓到上述資料夾；**沒有校驗碼檢查**

- 新檔 `src/IEML.Core/Services/JavaService.cs`：`GetRequiredMajorAsync(mc)`（讀 Mojang 版本 JSON 的 `javaVersion.majorVersion`，連不上用對照表推測；需求 16 時下載改抓 17）、`DetectLocalAsync()`（JAVA_HOME、PATH、Program Files 各家 JDK、官方啟動器 runtime；用 `release` 檔或 `java -version` 判斷版本）、`ResolveAsync(cfg, progress, ct)`。
- 修改 `LaunchService.cs`：啟動前呼叫 `ResolveAsync`，`MLaunchOption.JavaPath = java.Path`；載入器安裝期間暫時把 `cfg.JavaPath` 設成選好的路徑，結束後還原（避免把自動選的路徑存進實例設定；`LoaderService` 沒動）。選到的 Java 會寫進遊戲日誌第一行。
- 新檔 `JavaPickerWindow.xaml` / `.xaml.cs`：顯示該實例 MC 版本需要的 Java、列出本機偵測結果、重新偵測、使用選取的、清除成「自動」。
- 修改 `InstanceSettingsWindow.xaml` / `.xaml.cs`：Java 路徑欄位旁加「選擇…」按鈕。
- 待實測：1) Mojang 版本 JSON 的欄位讀取 2) Adoptium 下載網址是否仍有效、解壓後資料夾結構 3) `java -version` 在 Oracle javapath 捷徑上的行為 4) 未下載 Java 時，啟動進度條階段文字是否正常顯示。


## 網頁版 Claude 追加：待辦 1–4 一批完成（全部未編譯、未實測；Contracts.cs 沒動）
新增檔案：
- `Core/Services/SharedCache.cs`：共用 libraries / assets / resources / runtime 到 `shared\`。**versions 不共用**（LoaderService 會把 Fabric/Quilt profile 與 Forge/NeoForge 安裝結果寫進實例自己的 versions）；**Forge/NeoForge 不共用**（官方安裝程式把函式庫裝進實例資料夾）。用到 `MinecraftPath.Library/Assets/Resource/Runtime` 四個可寫屬性，**名稱是憑記憶，請以 CmlLib 4.1 實際 API 為準**。
- `Core/Services/SrvResolver.cs`：用 `dnsapi.dll` 的 `DnsQuery_W` 查 `_minecraft._tcp.<host>` SRV；失敗回 null。
- `Core/Services/SeedMapService.cs`（實作 `ISeedMapService`）＋ `native/ieml_cubiomes_shim.c` ＋ `native/README.md`：Cubiomes 的 C 包裝（`ieml_find_structures`、`ieml_biome_at`）；**DLL 需自行編譯**，步驟見 native/README.md。Cubiomes 函式名稱（`str2mc`、`getStructureConfig`、`getStructurePos`、`isViableStructurePos`、`biome2str`、`applySeed`）憑記憶寫，編譯不過請對照其標頭檔。
- `Core/Services/AccountNames.cs`：離線名稱驗證（3–16 英數底線）與 `RenameOfflineAsync`（Id 不變，實例綁定保留；直接讀寫 accounts.json，沒走 `_lock`）。
- `App/SeedMapWindow.xaml(.cs)`：種子、MC 版本（預設選取實例）、結構種類、中心、半徑；搜尋結果依距離排序，可查中心點群系、複製座標。非數字種子用 Java `String.hashCode()`。
- `App/AccountWindow.xaml(.cs)`：新增離線／正版、重新命名、刪除。

修改既有檔案（只加不改為主）：
- `LaunchService.cs`：`LaunchAsync` 外包一層 try/catch，啟動前錯誤也寫進 `GameLog`（原本內容搬到私有 `LaunchCoreAsync`）；建立 `MinecraftPath` 後加 `SharedCache.Apply(path, cfg.Loader)`。
- `ServerPingService.cs`：`PingAsync` 在沒指定埠時先 `SrvResolver`；握手封包主機名仍用使用者輸入的（`hsHost`）。
- `MainViewModel.cs`：`OpenSeedMapCommand`、`OpenAccountsCommand`、`ProgressPercent`/`IsBusy`；`RunLaunch` 啟動中重複點擊會被擋下並更新進度條。
- `MainWindow.xaml`：「種子地圖」「帳號管理」按鈕、啟動進度條（只在啟動中顯示）。
- `IEML.App.csproj`：若 `native\ieml_cubiomes.dll` 存在就複製到輸出資料夾。
- `ModrinthService.cs`：整合包檔案下載失敗時刪除剛建立的半成品實例。

待辦 4 檢查結果：`ContentWindow` 的類型下拉有「整合包」，`Install_Click` → `InstallAsync` → `InstallModpackAsync`（.mrpack：overrides、client-overrides、下載檔案含 sha1、路徑穿越防護）。已涵蓋。未涵蓋：CurseForge 整合包格式；.mrpack 的 `server-overrides`（不需要）；安裝完成後不會自動選取新實例（主畫面重新整理後出現在清單）。

仍未做：介面美化（仍是 WPF 預設樣式）、Forge 實測、種子地圖的圖形化地圖（目前只有清單）。

實測清單：
1. 同版本建兩個原版／Fabric 實例，第二個啟動不應重新下載 libraries/assets，`shared\` 內有內容。
2. Forge/NeoForge 啟動仍正常（不走共用）。
3. 伺服器查找輸入有 SRV 的網域（不帶埠）能連上。
4. 啟動故意失敗（例如沒選帳號）→ 遊戲日誌出現「啟動失敗」行。
5. 編譯 `ieml_cubiomes.dll` 後，種子地圖查村莊、查群系；與 Chunkbase 對照座標。
6. 帳號管理：改名後實例仍綁定該帳號。


## 網頁版 Claude 追加：CurseForge 整合包線上安裝（未編譯、未實測；Contracts.cs 沒動）
新增：
- `Core/Services/CurseForgeService.cs`：
  - `CurseForgeSettings`：API 金鑰由使用者自己申請並填入，DPAPI 加密存在 `curseforge.json`（`HasKey/GetKey/SetKey`）。**金鑰不寫死在程式裡。**
  - `SearchModpacksAsync(query)`：`GET /v1/mods/search?gameId=432&classId=4471&sortField=2…`，回傳 `ContentHit`（Kind=Modpack）。
  - `InstallModpackAsync(projectId, progress, ct)`：取最新（優先 release）檔案 → 下載整合包 zip → 讀 `manifest.json`（MC 版本、`modLoaders` 的 primary，如 `forge-47.2.0`→Forge/`47.2.0`）→ 建立實例 → 解壓 `overrides`（路徑穿越防護）→ `POST /mods/files` 取下載資訊、`POST /mods` 取分類（6 模組→mods、12 資源包→resourcepacks、6552 光影→shaderpacks、17 地圖→不安裝列入手動）→ 6 併發下載、sha1 驗證。
  - 作者不開放第三方下載的檔案（API 不給 downloadUrl）**不繞過**，列入回傳的 `ManualFiles` 並寫入實例資料夾 `manual-downloads.txt`。失敗／取消會刪掉半成品實例。
- `App/CurseForgeWindow.xaml(.cs)`：金鑰欄位、搜尋、清單、安裝＋進度條，安裝完顯示需手動下載的清單。
修改：`MainViewModel.cs`（`OpenCurseForgeCommand`）、`MainWindow.xaml`（「CF 整合包」按鈕）。
待實測／可能要修：JSON 欄位名稱（`downloadUrl`、`hashes[].algo`、`classId`、`links.websiteUrl`、`releaseType`）；`POST /mods` 與 `POST /mods/files` 的請求格式；NeoForge 1.20.1（`neoforge-47.x`）整合包會在啟動時因 LoaderService 不支援而失敗；只做整合包，CF 的單獨模組／資源包搜尋尚未做。
測試：申請金鑰 → 儲存 → 搜尋 → 安裝一個小型整合包 → 主畫面出現新實例 → 啟動；挑一個有「不開放第三方」模組的整合包確認 manual-downloads.txt。


## 網頁版 Claude 追加：NeoForge 1.20.1、CurseForge 單檔搜尋安裝、本機整合包匯入（未編譯、未實測；Contracts.cs 沒動）
（接續上一個 session token 用完前的最後一步；zip 內原本沒有這批改動，這次重寫。）

**LoaderService.cs**
- 新增 `IsLegacyNeoForge(mc)`：1.20.1 的 NeoForge 用舊命名（版本 `47.1.x`、artifact `net.neoforged:forge`、安裝程式 `forge-1.20.1-47.1.x-installer.jar`）；1.20.2+ 仍是 `net.neoforged:neoforge`。
- `LatestNeoForge` 兩條路：舊命名讀 `.../net/neoforged/forge/maven-metadata.xml`（去掉 `1.20.1-` 前綴）；新命名同前。改用數字比較取最新（`Newest`/`CompareVer`），優先穩定版。
- `LoaderVersion` 一律存 `47.1.106` 這種不含 MC 版本的形式（輸入 `1.20.1-47.1.106` 會自動去前綴，與 CF manifest 的 `neoforge-47.1.106` 一致）。
- `FindVersionId` 改嚴格比對（`-<ver>` 結尾或 `-<ver>-`），避免 `21.1.5` 誤中 `21.1.50`；找不到才用舊的 Contains 備援。
- 待實測：1.20.1 NeoForge 安裝後 versions 資料夾名稱（預期 `1.20.1-forge-47.1.x`）；Java 17。

**ModrinthService.cs**：`InstallModpackAsync` 拆成「下載」+ 公開的 `ImportMrpackAsync(path, fallbackName, ct)`（供本機匯入共用）；名稱優先用 index 的 `name`；失敗／取消刪掉半成品實例。

**CurseForgeService.cs（重構）**
- `SearchAsync(query, kind, mcVersion, loader)`：classId 對應 模組 6／資源包 12／光影 6552／整合包 4471；帶 `gameVersion`；只有模組帶 `modLoaderType`（Forge 1、Fabric 4、Quilt 5、NeoForge 6；**1.20.1 的 NeoForge 改用 Forge 篩選**）。`SearchModpacksAsync` 保留為包裝。
- `InstallFileAsync(instanceId, projectId, kind, progress, ct)`：單檔安裝到 mods／resourcepacks／shaderpacks；選檔順序 release > beta > alpha、同級取最新；模組遞迴安裝必要相依（relationType 3，深度 ≤5）；Quilt 找不到會退回 Fabric；作者不開放第三方下載的檔案不繞過，直接丟出提示。
- `ImportLocalAsync(path, …)`：自動判斷 `manifest.json`（CF，需要金鑰）或 `modrinth.index.json`（.mrpack，不需要金鑰）。
- `InstallFromZipAsync`：線上安裝與本機匯入共用的 CF 整合包安裝流程。
- 待實測：`modLoaderType`／`gameVersion` 查詢參數、`dependencies[].relationType`、`fileDate`／`isAvailable` 欄位。

**UI**：`CurseForgeWindow` 加類型下拉（整合包／模組／光影／資源包）與「匯入本機整合包…」（.zip／.mrpack）；建構子改為 `(IInstanceService, InstanceConfig?)`，`MainViewModel.OpenCurseForge` 傳入目前選取的實例；主畫面按鈕改名「CurseForge」。

## 打包（publish.ps1）
- `./publish.ps1` → 輸出到 `dist\`：framework-dependent 單檔（使用者需要 .NET 8 Desktop Runtime）；加 `-SelfContained` 則內含 runtime（檔案較大）。
- 若 `native\ieml_cubiomes.dll` 存在會一併複製（種子地圖用，編譯方式見 native/README.md）。
- 還沒做：安裝程式／自動更新、圖示、介面美化、Forge 實測、CF 單檔搜尋在介面上與 Modrinth 視窗合併。

建議先 `dotnet build`，優先修：CmlLib 4.x API 名稱、`SharedCache` 的 MinecraftPath 屬性、CF／EasyTier／公開伺服器的欄位名稱。


## 網頁版 Claude 追加：主畫面改版（依使用者設計稿）＋素材槽位＋啟動器設定（未編譯、未實測）
版面比例 左 311 : 中 551 : 右 117（視窗 980x640，可縮放）。
- **左欄**：IEML 標誌、頭像（點了開帳號管理；正版帳號從 mc-heads.net 抓皮膚頭像）、實例名稱／版本／載入器、帳號下拉、進度條與狀態、啟動、〔實例設置〕〔實例選擇〕。
- **中欄**：快捷啟動卡片（WrapPanel，數量不限，超過會捲動；空的時候顯示提示）。
- **右欄**：設置（`LauncherSettingsWindow`）、下載（選單：Modrinth／CurseForge）、P2P（連線房間）、更多（選單：伺服器查找／遊戲日誌／種子地圖／帳號管理）、音樂（彈出播放控制）。
- 新檔：`Theme.xaml`（顏色與 `ArtButton`／`CardButton` 樣式，集中換皮）、`ArtAssets.cs`（素材載入、`AssetConverter`、`ArtProps.File` 附加屬性、`AppInfo` 版本與專案網址）、`InstancePickerWindow`、`LauncherSettingsWindow`（帳號、CurseForge 金鑰、素材資料夾、關於／Beta）、`Assets/README.txt`（素材檔名與建議尺寸）。
- **素材**：PNG 放 `%AppData%\InnovationEasyLauncher\Assets\`（優先）或 exe 旁 `Assets\`，檔名見 `Assets/README.txt`；每張圖拉伸撐滿所屬位置，沒放就用預設樣式；換圖後重開啟動器生效。若背景圖已畫好面板外框，把 `Theme.xaml` 的 `PanelBg`／`LineBrush` 改成 `Transparent`。
- 版本：`IEML.App.csproj` 加 `<Version>0.1.0-beta</Version>`，視窗標題與設定頁顯示 Beta。
- 主畫面不再有的按鈕（新增／刪除帳號、新增／刪除實例）：帳號全部走「帳號管理」，實例走「實例選擇」視窗；`MainViewModel` 的舊指令保留。
- 待實測：ContextMenu 的指令綁定、Popup 的 DataContext 與前景色、`mc-heads.net` 頭像是否載入、DataTrigger 的 `{x:Null}` 判斷（沒圖時是否顯示預設框）。

## 網頁版 Claude 追加：快捷啟動卡片可自由拖拉（未編譯、未實測）
- 中欄改成 Canvas（`MainWindow.xaml` 的 `CardsHost`）：卡片可拖到畫布內任何位置，位置存在 `QuickLaunchCard.X/Y`（`Models.cs`，可為 null＝用預設兩欄排列）。拖拉超過系統的最小拖動距離才算移動；沒拖動＝點一下啟動。
- `CardItem` 由 record 改成 `ObservableObject`（`X`/`Y` 即時更新、`InstanceId`/`CardId` 用來存回實例設定）。啟動改由 `MainWindow.xaml.cs` 的 `Card_Up` 呼叫 `LaunchCommand`（卡片按鈕不再綁 Command，避免拖完又觸發啟動）。
- `MainViewModel`：`SetCardsArea`（視窗縮小時把跑出畫面的卡片拉回，只改畫面不改存檔）、`SaveCardPositionAsync`、`ResetCardLayoutCommand`（設置頁「重設快捷卡片位置」）。`InstanceSettingsWindow` 複製卡片時保留 X/Y。
- 尚未做：卡片重疊時的前後順序（目前以建立順序為準）、吸附格線。
- 待實測：Button 內拖拉是否順暢（滑鼠捕捉）、放開後不會誤觸啟動、縮小視窗後卡片拉回。


## 網頁版 Claude 追加：卡片前後順序（未編譯、未實測）
- 接手 `InnovationLauncher_1.zip`（上一個 session 最後停在「更新 HANDOFF 並打包」，拖拉卡片的程式與說明其實已經完整在 zip 裡）。我做了靜態檢查：所有 XAML 事件處理常式都有對應的 code-behind 方法、主畫面所有 `Binding xxxCommand` 都有對應的 ViewModel 指令，沒發現缺漏（這只是字串比對，不等於能編譯）。
- 補上「按下卡片就拉到最上層」：`CardItem` 加 `Z`（不存檔）、`MainWindow.xaml` 的 `ItemContainerStyle` 加 `Panel.ZIndex` 綁定、`MainWindow.xaml.cs` 的 `Card_Down` 設 `c.Z = ++_zTop`。上一節「尚未做：卡片重疊時的前後順序」現在已處理；吸附格線仍沒做。


## 網頁版 Claude 追加：CurseForge 金鑰「使用者優先、打包內建為輔」（未編譯、未實測）
做法參考 PCL2 / HMCL 的模式（只取「打包時注入 Secret」的概念，**沒有抄它們的程式碼**；PCL2 的授權對大量參考其核心功能有嚴格要求，請不要把 PCL2 程式碼貼進本專案）。
- 新檔 `Core/Services/BuiltInSecrets.cs`：`CurseForgeKey` 在倉庫裡**永遠是空字串**，真的金鑰絕對不要提交進 git。
- `CurseForgeSettings`（`CurseForgeService.cs`）：金鑰優先順序＝使用者自己填的 > 打包內建的；新增 `HasUserKey`、`HasBuiltInKey`、`SourceText`（介面顯示目前用哪種；都沒有時提示到 console.curseforge.com 申請或改用 Modrinth）；`GetKey()` 回傳實際使用的金鑰。
- `CurseForgeWindow.xaml.cs`、`LauncherSettingsWindow.xaml.cs`：狀態文字改用 `SourceText`；清除自己的金鑰後會顯示回到哪種狀態。
- 新檔 `.github/workflows/build.yml`：在 GitHub Actions（windows-latest、.NET 8）執行 `publish.ps1`；有 Secret `CURSEFORGE_API_KEY` 才在編譯前取代 `BuiltInSecrets.cs` 的空字串，沒有就照常編譯；成品以 artifact 上傳。觸發：手動或推 `v*` 標籤。
- 還沒有專案金鑰（要向 CurseForge／Overwolf 申請並通過審核），所以目前行為與以前相同：使用者自己填。內建金鑰仍可能被人從 exe 解出，拿到後請當成可更換的專案專用金鑰。


## 網頁版 Claude 追加：陶瓦聯機（Terracotta）整合，取代 EasyTier 成為「P2P」預設（未編譯、未實測）
**依據官方文件** https://burningtnt.github.io/Terracotta/http-api/ （基於 v0.4.2）。陶瓦是 burningtnt 的專案（基於 EasyTier，已被 HMCL、PCL-CE 等接入），房間碼格式 `U/XXXX-XXXX-XXXX-XXXX`，**與 HMCL／PCL-CE 互通**，且不需自己解析 IP。
**授權**：AGPL-3.0-or-later，附例外條款：「打包／下載未修改的二進位並當獨立行程、透過 HTTP API 互動」不會讓本專案被 AGPL 涵蓋，**但必須在介面明顯處標示陶瓦的版權資訊**（`TerracottaWindow` 頂部已標示，請勿移除）。我們只下載未修改的官方執行檔，不連結、不修改。
- 新檔 `Core/Services/TerracottaService.cs`：下載官方 Windows 版（GitHub Releases latest，檔名含 windows＋架構、接受 .exe/.zip，有 sha256 digest 就驗證；**我沒有確認附件檔名格式**，找不到時會提示手動把 `terracotta*.exe` 放進 `tools\terracotta\`）→ `terracotta.exe --hmcl <port.json>` 啟動（就緒後檔案會寫入 `{"port":N}`，最久等 15 秒）→ 本機 HTTP API：`GET /state` 每 500 ms 輪詢（只處理 `index` 變大的回應，連續失敗 5 次視為行程結束）、`/state/scanning?player=`（房主）、`/state/guesting?room=&player=`（加入）、`/state/ide`（回待機）、`/panic?peaceful=true`（結束）。狀態：waiting / host-scanning / host-starting / host-ok / guest-connecting / guest-starting / guest-ok / exception(type 0–5)。
- 新檔 `App/TerracottaWindow.xaml(.cs)`：玩家名、開房、輸入房間碼加入、狀態說明（中文）、房間碼與直連位址（複製）、成員清單、返回待機。
- 修改 `MainViewModel.cs`：`_terracotta`、`OpenTerracottaCommand`、`Shutdown()`；`MainWindow.xaml`：右欄「P2P」改開陶瓦視窗，原 EasyTier 房間移到「更多 → EasyTier 房間（進階）」；`MainWindow.xaml.cs`：`OnClosed` 呼叫 `Vm.Shutdown()` 結束陶瓦行程。
- EasyTierService／RoomWindow 沒有刪（保留為進階選項）。若之後確定只用陶瓦，可以移除它們。
- 待實測：1) Release 附件命名與 .exe／.zip 格式 2) 陶瓦在 Windows 是否需要管理員權限（HMCL 的做法是一般權限啟動，Windows 下 `--hmcl` 行程會 fork 子行程作為真正的工作行程，所以 `Process` 物件不一定代表實際行程，結束靠 `/panic`） 3) 房主流程：進世界→對區域網路開放→開房→host-ok 4) 加入後「陶瓦聯機大廳」是否出現在多人遊戲列表（官方說從這裡加入會略過正版驗證）。

## 網頁版 Claude 追加（第二輪）：介面改版、Terracotta 修正、預覽稿（全部未編譯、未實測）

### 已完成（都在這份 zip 裡）
- `TerracottaService.cs`：新增 `CleanupStale()`，`EnsureStartedAsync` 重啟前先清掉舊的輪詢／HttpClient／行程（陶瓦異常結束 `stopped` 後再開房不會留殘骸）。
- `Theme.xaml` 全面換成網頁（InnovationExtremeStudio）配色：`WindowBg #0F0F0F`、`PanelBg #1C1C1C`、`LineBrush #333`、強調色 `AccentBrush #00FFFF`、`OnAccentBrush #0F0F0F`。
  - `ArtButton`：青底黑字粗體、圓角 5、滑過發光並上浮 2px；沒指定樣式的 `Button` 也套同一外觀（implicit style，BasedOn ArtButton）。
  - `CardButton`：#1C1C1C 底、#333 邊框，滑過邊框變青＋微發光。
  - `NavButton`：右欄純文字選單鈕，滑過文字變青＋左側亮條。
  - `NavIcon`：向量圖標用的 `Path` 樣式，`Stroke` 綁到所屬 Button 的 `Foreground`（滑過自動變色）。
  - 若 Assets 有放對應 PNG，ArtButton／CardButton 仍優先顯示圖片。
- `MainWindow.xaml`：
  - 背景改成程式繪製的深綠漸層（`#0F1F1A → #12302A → #0F0F0F`）；`Assets\background.png` 存在時蓋過它。
  - 三個面板合成**單一外框＋兩條分隔線**（照使用者草圖）；右欄改 `NavButton`。
  - 右欄「設置／下載／P2P／更多」前面有程式繪製的向量圖標（`Viewbox 24` ＋ `Canvas 24` ＋ `Path`，線寬 2.2）；「音樂」用文字 `♪ 音樂`。
- `MainViewModel.cs`：卡片預設排列改成上排兩張（右邊略低 14px）、第三張置中在下（只影響還沒擺過位置的卡片）。
- `Assets\icon_*.png`（settings／download／p2p／more）：使用者給的圖，**目前程式已不使用**，可刪。
- `.github/workflows/build.yml`：沒進 repo（用 Unzip workflow 時被 `workflows` 權限擋下）。使用者說會從 VS Code 推；Unzip workflow 要用「`unzip -o … -x ".github/*"`＋沒變更就略過 commit」那版。

### 預覽稿（HTML，僅示意，不是 WPF）
https://claude.ai/artifact/TirswMEzdA8uzL7dUzt1tT ——使用者看過，說「還挺好」，這是**下一步要搬進 WPF 的設計依據**。

### 下一步（尚未做，使用者已確認的需求）
1. **啟動時沒有卡片**：中間顯示空狀態與「＋ 新增卡片」。（目前 WPF 已有「還沒有快捷啟動卡片」文字，但沒有按鈕。）
2. **卡片內容**：小頭像＋實例名＋帳號，下面兩顆按鈕「啟動」「設置」（設置 → 卡片設置頁：改實例／帳號、刪除卡片）。卡片仍可拖動、位置要存。
3. **每個功能都是獨立頁面**（不要跳視窗／選單）：點右欄項目時，中欄換成該頁，頁首有「返回」，再點同一項回首頁；右欄該項高亮。頁面：設置、下載、P2P、更多、音樂、卡片設置。
4. **設置頁加顏色設定**：強調色（按鈕／滑過／進度條）、圖標線條色、外框與分隔線色；六個色塊＋自訂取色＋「恢復預設」；要存檔（下次開啟還在）。實作建議：把 `AccentBrush`／`LineBrush`／圖標色改成可動態更新的 `DynamicResource`，存進設定 JSON。
5. **下載頁（使用者特別要求，之前漏了）**：五個分頁 ——
   - 遊戲：左邊版本清單（正式版／快照／舊版切換，顯示發布日期），右邊實例名稱（依版本＋載入器自動命名，可改）、載入器（原版／Fabric／Forge／NeoForge／Quilt；快照與舊版只能原版）、載入器版本下拉、安裝進度條，完成後建立實例並選為目前實例。接現有 `VersionService`、`LoaderService`。
   - 模組／光影／資源包：Modrinth／CurseForge 切換、搜尋、安裝到哪個實例的下拉。接 `ModrinthService`、`CurseForgeService`。
   - 整合包：按鈕是「安裝為新實例」。
6. 現有的 `ContentWindow`、`CurseForgeWindow`、`RoomWindow`／`TerracottaWindow`、`LauncherSettingsWindow` 等視窗，搬成頁面（UserControl）時邏輯盡量沿用，只換外殼。陶瓦頁上必須保留版權標示（AGPL 例外條款）：「陶瓦聯機 Terracotta © burningtnt（AGPL-3.0）」。

### 還沒驗證／已知風險
- 整個專案**從未編譯**。推上 GitHub 後若 Actions 報錯，先看 `Publish` 步驟的 `error CS…`／`error MC…`。
- 陶瓦 Release 附件檔名是猜的（含 windows＋x86_64、.exe/.zip）；找不到會提示手動放 `tools\terracotta\terracotta*.exe`。
- 向量圖標用 `Viewbox` ＋ `Canvas`，`Stroke` 綁 `RelativeSource AncestorType=Button`；若綁定警告或不變色，改成在 `NavButton` 範本裡直接放 `ContentPresenter` 並以 `TextElement.Foreground` 傳遞。
- 使用者希望整體**更專業**；下載頁漏掉實例／載入器就是被點名的例子。搬頁面時注意資訊完整度，不要只放示意項目。


## UI 改版設計稿（網頁版 Claude，尚未搬進 WPF）
- 互動預覽：`docs/ui-preview/ieml-ui-preview.html`（瀏覽器直接開；可拖卡片、切頁面、選載入器版本、項目詳情頁選過往版本、必要／可選前置、顏色設定、動畫）
- 實作說明：`docs/ui-preview/UI-SPEC.md`（版面、頁面對應的 Service、需新增的後端方法、WPF 動畫對照、驗收清單）
- 要做的事：把中欄改成「首頁卡片＋獨立頁面（UserControl）」，右欄切換；新增 `GetVersionsAsync`／`InstallVersionAsync`、前置分類（必要／可選）、載入器版本清單列舉、顏色設定存檔。Contracts.cs 不改，新增方法放新檔案。


## Update 6 — 內容篩選、已安裝管理、載入器版本切換（未編譯、未實測）
**沒有 .NET 環境，請先 `dotnet build` 修正錯誤。**

Contracts.cs（只加不改既有簽章）：`ContentHit` 多了選填的 `Downloads`、`Author`；新增 `ModrinthQuery`、`ContentPage`、`FilterOption`、`UpdateInfo`；
`IModrinthService` 新增 `SearchAsync(ModrinthQuery)`、`GetCategoriesAsync`、`GetLoadersAsync`、`CheckUpdatesAsync`、`ApplyUpdateAsync`。

- `ModrinthService`：facets 支援分類／載入器（光影為 iris/optifine/canvas）／執行端（client/server）／MC 版本；排序（相關度、下載、追蹤、最新、更新）；offset 分頁；
  分類與載入器清單從 `tag/category`、`tag/loader` 取得（失敗就不顯示）；安裝優先挑 release 版；`version_files/update` 檢查更新（只推 release），更新時先下載驗證再刪舊檔，保留 `.disabled` 狀態。
- `InstalledContentService`（新）：列出 mods/shaderpacks/resourcepacks、停用（加 `.disabled`）、刪除。
- `LoaderVersionService`（新）：Fabric/Quilt（meta API）、NeoForge（含 1.20.1 舊命名）、Forge（maven-metadata）可用版本，新→舊、穩定版在前。
- `ContentWindow`：分頁籤「搜尋與安裝」（篩選列、圖示、下載數、載入更多）＋「已安裝管理」（勾選＝啟用／停用、刪除、開資料夾、檢查／全部更新）。
- `InstanceSettingsWindow`：新增「載入器版本」可編輯下拉（自動＝最新穩定版，可手打）；換載入器種類且已有模組時先警告；MC 版本改了而載入器版本不在新清單會清成自動。

可疑處：Modrinth 分類名稱的中文對照表是手寫的（沒對到就顯示原名）；`file` 修飾詞沒用到；CurseForgeWindow 沒有動，仍是舊的單純搜尋。
測試清單：模組→依分類/排序/執行端搜尋→載入更多；光影選 iris；整合包選載入器；已安裝頁停用→遊戲內不載入；檢查更新→全部更新；實例設置換 Fabric 版本→啟動確認用的是選的版本。

## Update 7 — 主畫面照預覽圖重做（未編譯、未實測）
- `Theme.xaml`：新增 GhostButton、FlatButton、DarkProgress、DarkComboBox（只給唯讀下拉用，可編輯的下拉不要套）、深色 ContextMenu/MenuItem；NavButton 改成 12 號字、圖示在上文字在下。
- `MainWindow.xaml`：左欄（圓形頭像＋青色外圈、使用帳號下拉、狀態文字＋常駐進度條、描邊按鈕）、中欄小卡片（拖曳區＋「啟動」「設置」按鈕、右下「＋ 新增卡片」）、右欄圖示在上。
- `MainViewModel`：`CardItem` 加 `SettingsCommand`、`Initial`；`AvatarInitial`；`Status` 預設「待命」；卡片尺寸改 220×104；`OpenSettingsFor(id)`。
- 卡片不再「點一下就啟動」，改成按鈕；拖曳只在上半部。
- 其他視窗（設置、下載、各功能頁）還是預設淺色控制項，尚未統一。
- 登入：改用 `Authenticate()` 並在日誌寫出快取檔大小，待實測是否解決「每次啟動都要重登」。


## Update 8 — 地圖下載（未編譯、未實測）
- `MapService`（新）：列出 `saves` 的地圖（有 `level.dat` 才算）、`ImportZipAsync`（找最淺的 level.dat、防 zip-slip、同名自動加 (2)、失敗清掉半張）、`ImportFolderAsync`、`DownloadAndImportAsync`（zip 直連網址）、`BackupAsync`（壓成 `backups/<名>-時間.zip`）、`Delete`。
- `CurseForgeService`：新增 `SearchWorldsAsync`（classId 17）、`GetWorldDownloadAsync`、`DownloadFileAsync`，以及 `MapHit`、`MapDownload`。作者不開放第三方下載的地圖會提示手動下載再匯入。
- `MapsWindow`（新）：分頁「我的地圖」（匯入 zip／資料夾、網址、備份、刪除、開資料夾）與「CurseForge 地圖」。入口：主畫面「下載」選單 →「地圖」。
- Modrinth 沒有地圖分類，所以不做；Planet Minecraft 等網站沒有公開 API，只能先用瀏覽器下載再匯入。
- 沒做：讀 level.dat 顯示真正的世界名稱／版本／縮圖；遊戲執行中偵測（備份或刪除正在使用的地圖可能失敗）。
