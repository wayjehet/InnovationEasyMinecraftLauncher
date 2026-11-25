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
- 待實測：1) 下載 .tar.gz 解壓後 exe 與 DLL 的位置、陶瓦能否啟動 2) 陶瓦在 Windows 是否需要管理員權限（HMCL 的做法是一般權限啟動，Windows 下 `--hmcl` 行程會 fork 子行程作為真正的工作行程，所以 `Process` 物件不一定代表實際行程，結束靠 `/panic`） 3) 房主流程：進世界→對區域網路開放→開房→host-ok 4) 加入後「陶瓦聯機大廳」是否出現在多人遊戲列表（官方說從這裡加入會略過正版驗證）。

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
- 陶瓦 Release 附件檔名已核對（v0.4.2 共 15 個附件，Windows 為 `terracotta-0.4.2-windows-x86_64-pkg.tar.gz`／`-windows-arm64-pkg.tar.gz`，格式是 **.tar.gz**，原本只認 .exe/.zip 會失敗）。已加 `ExtractTarGz`（`TarReader`＋`GZipStream`，只收一般檔案、保留相對路徑、擋路徑穿越、200 MB 上限）。尚未實測：tar 裡實際有哪些檔（是否含 `VCRUNTIME140.DLL`、有無子資料夾，`FindExe` 會遞迴找 `terracotta*.exe`）。找不到仍會提示手動放進 `tools\terracotta\`。
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


## Update 9 — 接上 Copilot 的主畫面
- 倉庫改由 GitHub main 為準（Copilot 依預覽做了「階段頁」：設置／下載中心的覆蓋頁、`DownloadStageControl`、主題配色）。
- 地圖入口：下載中心分頁列新增「地圖」，開啟 `MapsWindow`（對象是「安裝到實例」選的實例）。原本在舊「下載」選單的 `OpenMapsCommand` 項目已隨主畫面改版消失，`OpenMaps` 命令仍留在 `MainViewModel`（未使用）。
- `.github/workflows` 同時有 `Build.yml` 與 `build.yml`（只差大小寫）：同一個 push 會跑兩次，建議刪一個；Copilot 版沒有「注入 CURSEFORGE_API_KEY」的步驟。


## Update 10 — 登入快取加密（安全修正，未編譯、未實測）
- 問題：Update 登入修正後，`Authenticate()` 讓 CmlLib 把 Microsoft/Xbox/Minecraft 權杖以**明文 JSON** 存到 `%AppData%\Roaming\InnovationEasyLauncher\ms\<id>.json`，複製走就能在別處使用。
- 修正：`MicrosoftSessions.Open(id)` 回傳 `LoginScope`（務必 using）：把 DPAPI 加密的 `ms\<id>.dat` 解到 `%LocalAppData%\InnovationEasyLauncher\tmp\*.tmp` 給函式庫用，Dispose 時重新加密並覆寫 0 後刪除暫存檔；啟動時清掉殘留暫存檔；舊的明文 `.json` 自動遷移後刪除。`AccountService` 兩處改用 scope。
- 限制：擋不住同一 Windows 使用者下的惡意程式；SSD 無法保證覆寫抹除。
- 待實測：加入帳號 → `ms\` 只有 `.dat`（沒有 `.json`）；第二次啟動靜默登入；複製 `.dat` 到別台電腦應該解不開並要求重登。


## Update 11 — 安全強化（未編譯、未實測）
- `Secrets.cs`（新）：DPAPI 的 optionalEntropy 依用途分開（登入快取／CurseForge 金鑰／帳號 refresh token），`UnprotectWithLegacy` 可讀舊版（entropy=null）並由呼叫端重新加密；`Zero()` 抹除 byte[]。登入快取解密／加密的位元組、CF 金鑰的位元組用完即抹除。**限制**：權杖在 CmlLib 內部是 string，無法抹除；entropy 在程式裡不是祕密，只做用途隔離。舊的（entropy=null）`ms\*.dat` 解不開會被刪除並要求重登（登入快取加密尚未正式發佈，影響極小）。
- `DownloadPolicy.cs`（新）：https 網域白名單（forgecdn.net／curseforge.com／modrinth.com／github.com／githubusercontent.com／gitlab.com，含轉址後的最終網址）；.mrpack 內的下載網域限縮到規格允許的 cdn.modrinth.com／github.com／raw.githubusercontent.com／gitlab.com；整合包 overrides 的可執行檔（.exe .bat .cmd .ps1 .vbs .vbe .wsf .msi .scr .com .lnk .hta .reg）一律略過；自帶 .jar 只在遊戲日誌警告。
- `CurseForgeService`：新增 `HashOf`（SHA-1，沒有才 MD5）與 `DownloadVerifiedAsync`（白名單＋強制雜湊＋.part 驗證後改名）；模組、整合包本體、整合包內模組、地圖下載都改走它；舊的未驗證 `DownloadAsync` 已刪除。`MapDownload` 多了 `Hash`，`DownloadFileAsync` 多了 `hash` 參數。
- `ModrinthService.DownloadAsync`：改為強制 SHA-1、網域白名單、.part 驗證；`.mrpack` 本體現在也驗證 SHA-1（原本傳 null）。
- 啟動參數：`TerracottaService`、`EasyTierService` 的 CLI 呼叫改用 `ArgumentList`；以管理員身分執行的 PowerShell 指令列有路徑字元檢查（那一段維持 `UseShellExecute=true` + `runas`，ArgumentList 在該模式下的行為沒把握，未更動）。遊戲本體由 CmlLib 自己組 `ProcessStartInfo`，無法改；`ExtraJvmArgs` 是使用者自己填的設定，沒有加驗證。
- 刻意沒做：封鎖整合包內的 .jar（會讓許多正常整合包無法安裝）。
- 注意：缺雜湊會直接拒絕安裝，如果某些舊檔案 CurseForge 沒給雜湊，會出現「沒有官方檢查碼可驗證」的錯誤。


## Update 12 — 自動更新（未編譯、未實測）
- `UpdateService`（Core，新）：`CheckAsync`（GitHub `releases/latest`，只看正式版）、`DownloadAndVerifyAsync`（網域白名單、SHA-256、選用 ECDSA P-256 簽章、防 Zip Slip）、`ApplyAndRestart`（背景 PowerShell：等關閉→exe 改 .old→複製→失敗還原→重啟，全用 ArgumentList）、`CleanupOld`；`UpdateSettings`（update.json：自動檢查、上次檢查、略過版本）。
- `MainViewModel`：`CheckUpdateCommand`、`AutoCheckUpdateAsync`（每 12 小時最多一次）；`MainWindow` 的 `Loaded` 觸發；`LauncherSettingsWindow` 多「檢查更新」按鈕與「啟動時自動檢查更新」。
- `build.yml`：Release 多附 `.sha256`，有設定 `UPDATE_SIGNING_KEY` secret 就附 `.sig`。**`UpdateService.PublicKeyBase64` 目前是空的＝只驗 SHA-256**；啟用步驟見 `docs/UPDATE.md`。
- 待實測：用舊版（1.1.0）檢查到新 Release → 下載 → 換檔重啟；故意改壞 zip 應被拒絕；`digest` 欄位在 API 沒有時要有 `.sha256` 檔；安裝資料夾不可寫時還原。
- 注意：版本比較用 `System.Version`，Release tag 要像 `v1.2.0`；csproj 的 `<Version>` 要跟 tag 一致，否則會一直提示更新或永遠不提示。


## Update 13 — 自動更新收尾＋拖放匯入（未編譯、未實測）
- 版本號：csproj `<Version>` 1.1.0 → 1.2.1，另加 `<InformationalVersion>1.2.1EF</InformationalVersion>`；`UpdateService.TryParseVersion` 改成只取開頭的數字（v1.2.1EF → 1.2.1），字母後綴不再導致解析失敗。**字母不同、數字相同的版本不會互相提示更新。**
- 刪除重複的 `.github/workflows/Build.yml`（保留有 Release／checksum／簽章的 `build.yml`）。GitHub 倉庫那邊也要刪掉大寫的那個。
- `MainWindow.xaml.cs`：「關於」文字少了大括號（顯示成字面的 `AppInfo.Repo`）已修；`CleanupOld()` 改在每次啟動時呼叫（原本只有檢查更新時才清 `.old`）。
- 拖放匯入（採納朋友的建議：資源包 zip 可以直接拉進去，第三方有時抓不到）：`ContentWindow` 整個視窗可拖放 `.zip`（資源包／光影）與 `.jar`（模組）。`InstalledContentService.Import`：檢查副檔名、是有效 zip、資源包根目錄要有 `pack.mcmeta`、光影要有 `shaders/`；不覆蓋同名檔（加 (1)）；先寫 `.part` 再改名。`.zip` 預設當資源包，目前選的是光影才當光影。匯入後自動切到「已安裝管理」。
- 待實測：拖資源包 zip → 出現在清單並在遊戲內可選；多包一層資料夾的 zip 應被拒絕並說明；同名檔案不覆蓋；沒選實例時的提示。


## Update 14 — 合併兩個版本（InnovationLauncher 1.2.1EF ＋ InnovationEasyMinecraftLauncher-main 1.1.0）
- 以 InnovationLauncher（含 Update 10–13：登入快取加密、下載驗證、自動更新、拖放匯入）為基底；舊版（main）沒有任何獨有的程式碼變更。
- 從舊版帶進來：`native/ieml_cubiomes.dll`（種子地圖用）。`.gitignore` 加 `!native/ieml_cubiomes.dll` 例外，否則提交時會被忽略。
- 不採用：舊版大寫的 `.github/workflows/Build.yml`（和 `build.yml` 重複且功能較少）。GitHub 倉庫上如果還在，要手動刪除。
- 未編譯、未實測：這次合併沒有 .NET 環境，只做了靜態檢查（引用的成員、命令名稱、套件參考皆存在）。請先 `dotnet build` 一次。

## Update 15 — 設置頁崩潰修正
- 原因：設置頁「強調色／圖標／外框」色碼輸入框每按一次鍵就把文字丟給 `ColorConverter.ConvertFromString`，輸入到一半或刪除時（例如 `#00F`、空白）會丟 `FormatException`；加上整個程式沒有全域例外處理，錯誤沒人接，視窗就直接消失。
- 修正：色碼先用 `TryParseColor` 驗證；「開啟素材資料夾」加 try/catch；`ApplyTheme` 改同步寫檔並吞例外；輸入框預設顯示目前主題色。
- 新增 `App.xaml.cs` 全域例外處理：介面執行緒的未處理例外會寫入 `%AppData%\InnovationEasyLauncher\crash.log` 並跳提示、程式繼續執行；其他執行緒的例外只記錄。
- 未編譯、未實測。


## Update 16 — 介面改版規劃：少用彈出視窗（**尚未實作，只是規劃**）

### 背景（使用者回饋）
- 實例設置、模組與內容等都是獨立的彈出視窗，一個開過一個容易搞不清楚自己在哪一層，使用上不適應。
- 實例設置應該把「模組、光影、資源包、伺服器」也包含進來，而不只是基本欄位。
- 「更多」頁的按鈕希望固定排好，不要隨視窗大小跑位。

### 現況（改版前）
- `InstanceSettingsWindow`（xaml 51 行＋cs 119 行）是彈出視窗，只有：名稱、最大記憶體、Minecraft 版本、載入器、載入器版本（可重新整理）、Java 路徑、額外 JVM 參數、快捷啟動卡片（新增／刪除），按鈕為「保存」「取消」。
- `ContentWindow` 是另一個彈出視窗，有「搜尋與安裝」「已安裝管理」兩個分頁；已安裝管理已能依類型（`ContentKind`：模組／光影／資源包／整合包）切換，並支援拖放匯入。
- `DownloadStageControl`（下載頁，已嵌在中欄）也有「已安裝」分頁，**和 ContentWindow 的已安裝管理功能看起來重複**。
- `ServerWindow`（伺服器查找）、日誌、種子地圖、地圖管理、帳號、陶瓦、EasyTier、公開伺服器仍是彈出視窗，由 `MainViewModel` 的 `Open*Command` 開啟。
- 主畫面已有「頁面」機制：`MainWindow.xaml` 的 `StageOverlay`（中欄，`Panel.ZIndex=50`）搭配 `StageActions`（`WrapPanel`，`ItemWidth=220`、`ItemHeight=72`）、`ShowStage()`、`StageBack_Click`；設置、下載、P2P、更多、音樂都走這條路。`DownloadStageControl` 就是嵌入此處的 `UserControl` 範例。
- 「更多」頁（`MoreStage_Click` → `AddStageAction`）用 `WrapPanel` 流動排版，視窗大小改變時按鈕位置會跟著變。

### 規劃
1. **實例設置改成頁面，內含分頁**：基本（現有欄位）｜模組｜光影｜資源包｜伺服器｜快捷卡片。
   - 模組／光影／資源包：把 `ContentWindow` 抽成 `UserControl`（比照 `DownloadStageControl`），各分頁鎖定一種 `ContentKind`，沿用既有搜尋、安裝、啟用／停用、刪除、更新、拖放匯入。
   - 整併重複功能：每個實例的內容只在實例設置頁管理；下載頁專心做「建立實例、全域搜尋、匯入整合包、地圖」。
2. **彈出視窗只留小東西**：確認對話框、登入、選 Java、選檔案等。日誌、種子地圖、地圖管理、帳號、陶瓦、EasyTier、公開伺服器之後逐步改成頁面。
3. **「更多」固定排列**：把 `WrapPanel` 換成固定格子（2 欄 × 3 列，格子大小固定），順序不變，例如：伺服器查找／遊戲日誌、種子地圖／地圖管理、帳號管理／（預留）。

### 建議的實作順序（每階段都要能編譯並可單獨回退）
1. 「更多」固定排列（改動最小，先做）。
2. 實例設置頁：先只搬「基本」與「快捷卡片」，並決定保存方式與返回行為。
3. 搬入模組、光影、資源包分頁（抽 `ContentWindow` 為 `UserControl`），再處理下載頁「已安裝」重複功能。
4. 伺服器分頁（等下方問題確認）。
5. 其他大型彈出視窗改頁面。

### 設計限制與待決定
- 中欄約 551 px 寬（視窗預設 980×640，欄寬比約 311:551:117），放模組清單偏窄。選項：放大預設視窗、或頁面開啟時暫時蓋住左欄。
- 切換頁面時未儲存的設定如何處理：自動儲存，或切換時提示。
- 頁面的返回路徑：實例設置頁內的分頁切換不應被「← 返回」打斷；返回應回到主畫面。
- **待確認**：「伺服器」分頁指的是「這個實例的伺服器清單（收藏、一鍵加入）」，還是日後的「開服系統」（見 `SERVER-PROPOSAL.md`）？目前假設前者，尚未確認。

### 風險
- 沒有編譯環境，介面大改風險高；務必分階段、每階段先 `dotnet build` 並實測再繼續。
- 抽出 `UserControl` 時，注意 `ContentWindow` 內使用 `Window` 專屬成員（`Owner`、`DialogResult`、`Close()`）與 `MessageBox` 的 owner 參數，需改為相對於宿主視窗。
- 這份只是規劃，尚未改任何程式碼。


## Update 17 — 帳號顯示與換實例時的帳號（未編譯、未實測）
- 問題 1：主畫面「使用帳號」下拉框顯示 `IEML.Core.Models.Account`（型別名稱）而不是帳號名。修正：`Account` 覆寫 `ToString()` 回傳 `DisplayName`（`Models.cs`）。`DarkComboBox` 範本的選取項顯示沒吃到 `DisplayMemberPath` 的真正原因未查，之後可改用明確的 `ItemTemplate`。
- 問題 2：換實例後，帳號仍是舊實例的／不是使用者目前選的。修正（`MainViewModel.cs`）：
  - 新增 `_currentAccountId`＝使用者目前選的帳號（`OnSelectedAccountChanged` 在非 null 時記錄；清單重新整理造成的 null 不算）。
  - `OnSelectedInstanceChanged`：優先選 `_currentAccountId` 對應的帳號，並由 `OnSelectedAccountChanged` 綁到新實例；剛開啟、還沒選過時才退回該實例原本綁的帳號，再不行用第一個帳號。
  - `Launch` 改成 `RunLaunch(SelectedInstance.Id, SelectedAccount?.Id)`：以畫面上選的帳號啟動，不再依賴磁碟上的 `AccountId`（原本 `BindAccountAsync` 是 fire-and-forget，沒有等待也沒有鎖）。
- 設計變更：帳號選擇現在是「全域目前帳號」，換實例不會換帳號；快捷卡片仍用卡片自己的帳號。若想改回「每個實例各自記帳號」，把 `OnSelectedInstanceChanged` 還原成直接取 `v.AccountId`、`Launch` 傳 `null` 即可。
- 待實測：選帳號 A → 切到另一個實例，下拉框仍是 A，啟動用 A；重新整理（新增實例）後帳號不跳掉；刪除目前帳號後退回第一個帳號。
- 其他已知（尚未處理）：深色樣式只涵蓋主畫面，TextBox／ComboBox／CheckBox 在下載頁等仍是淺色預設，文字難讀；CF 金鑰在新的設置頁沒有入口（`LauncherSettingsWindow` 沒有按鈕呼叫它）；設置頁配色計畫改成 R、G、B 拉條並讓每個區塊可調。


## Update 18 — 深色輸入框、頁面蓋住左欄、頁面動畫（未編譯、未實測）
- `DownloadStageControl.xaml`：`UserControl.Resources` 加 `TextBox`／`CheckBox` 隱含樣式（深色底、白字、青色游標，**只作用在下載頁**，其他視窗不變）；9 個唯讀下拉框改套 `DarkComboBox`。三個可編輯下拉（`VersionBox`、`LoaderVersionBox`、`FilterMcBox`）仍是預設淺色（`DarkComboBox` 不支援可編輯）。
- `DarkComboBox` 選取項不吃 `DisplayMemberPath`（帳號下拉顯示型別名稱的同一個問題）：改在 `FilterOption`（record）與 `InstanceConfig` 覆寫 `ToString()` 回傳 `Name`，之後新增物件型別的下拉項目也要記得覆寫。
- `MainWindow.xaml`：`StageOverlay` 改成 `Grid.Column="0" Grid.ColumnSpan="2"`，頁面蓋住左＋中欄（約 862px），右欄保留。**啟動進度條在左欄，頁面開著時看不到**；之後可在頁面底部加細狀態列。
- 新檔 `Motion.cs`：`FadeSlideIn`（淡入＋右滑 14px、250ms、CubicEase）、`FadeOut`（140ms 後執行回呼）；`SystemParameters.ClientAreaAnimation` 關閉時一律跳過。`ShowStage` 呼叫 `FadeSlideIn`，`StageBack_Click` 改成淡出後才 Collapsed。
- 還沒做的動畫（UI-SPEC「動畫」一節）：按鈕滑過／按下過渡、卡片進場與拖動、右欄圖標放大、進度條流光、列表依序淡入、toast、空狀態浮動。
- 待實測：下載頁輸入框／下拉框文字清楚；切換頁面有淡入滑入、返回有淡出；覆蓋頁蓋住啟動按鈕後右欄仍可切換；開啟 Windows「減少動畫」時無動畫但功能正常。


## Update 19 — 按鈕與卡片動畫（未編譯、未實測）
- `Theme.xaml`：`ArtButton`／`CardButton` 滑過改成平滑上浮（120ms 上、180ms 回，CubicEase）；`ArtButton` 按下縮到 96%；`NavButton` 滑過圖示放大 1.1 倍、左側亮條淡入淡出；`DarkProgress` 加流光（白色漸層由左掃到右，1.4 秒循環，EventTrigger Loaded 啟動）。範本裡用 `Tt`／`Sc`／`NavSc`／`ShimmerTt` 命名的 transform 當動畫目標。
- `Motion.cs`：`FadeSlideIn` 加 `delayMs`；新增 `Stagger(Panel)`（清單依序淡入，每項 45ms、上限 360ms）與 `CardIn(el, index)`（浮起 14px＋淡入＋96%→100%，300ms，每張錯開 60ms；結束後清掉動畫與 transform，不影響拖拉）。系統「減少動畫」時 `Motion` 內的動畫一律跳過。
- `MainWindow.xaml(.cs)`：卡片 Border 加 `Loaded="Card_Loaded"`（呼叫 `Motion.CardIn`），滑過邊框變青色；「P2P」「更多」頁項目呼叫 `StaggerActions()`。
- 限制：Theme.xaml 裡的 Storyboard（按鈕滑過、流光）不會看「減少動畫」設定，仍會播放；發光 Effect 仍是瞬間切換（共用的 Effect 資源無法動畫）；流光一直在跑（Indicator 寬度為 0 時看不到）。
- 待實測：`Storyboard.TargetName` 指到 ControlTemplate 內的 Freezable（Tt／Sc）能否編譯；進度條流光是否被圓角外框裁切；卡片進場後拖拉正常；重新整理實例時卡片會重播進場。
- 還沒做：toast、空狀態浮動、進度條數值平滑。


## Update 20 — 補完動畫：提示條、空狀態浮動、進度條平滑（未編譯、未實測）
- `Motion.cs`：新增 `Float`（空狀態文字上下浮動，±5px、2.2 秒、SineEase 循環）、`Toast`（滑入 220ms → 停留 → 淡出 260ms）、`SmoothProgress` 附加屬性（`local:SmoothProgress.Value`：數值增加時 250ms 滑過去，變小或「減少動畫」時直接跳）。
- `MainWindow.xaml`：進度條改綁 `local:SmoothProgress.Value`（原本 `Value=`）；新增 `ToastHost`（Panel.ZIndex 100、蓋在所有頁面上、不攔截滑鼠）；空狀態文字命名 `EmptyHint`。
- `MainWindow.xaml.cs`：監聽 `MainViewModel.Status`，以字串判斷：開頭「錯誤」或含「失敗」→ 紅框提示 4.5 秒；開頭「已啟動」「已新增」「目前已是最新」→ 青框 2.2 秒。**以字串比對，Status 文字改了這裡要跟著改。** 公開方法 `ShowToast(text, error)` 可供其他地方呼叫；連續觸發以最新的為準。
- 待實測：啟動進度條是否平滑前進、重設時是否直接歸零；提示條在頁面開著時可見；空狀態浮動只在沒有卡片時看到；ProgressBar 的 `BeginAnimation` 與綁定同時存在時，動畫結束後 Value 是否停在目標值（HoldEnd，預期可以）。
- UI-SPEC「動畫」一節現已全部有對應實作。


## Update 21 — 實例設置改成頁面、「更多」固定格子（未編譯、未實測）
Update 16 規劃的第 1～3 階段一次做完（第 4 階段「伺服器」分頁沒做，見下）。
- **「更多」固定排列**：`MoreStage_Click` 改用 `UniformGrid`（2 欄 × 3 列，高度 3×84），順序：伺服器查找／遊戲日誌、種子地圖／地圖管理、帳號管理／（預留空格）。放在新的 `StageHost`，不再用會流動的 WrapPanel，視窗縮放不會跑位。
- **覆蓋頁多一個固定高度容器**：`MainWindow.xaml` 的 `StageOverlay` 內，`StageScroll`（原本的 ScrollViewer＋WrapPanel）與新的 `StageHost`（ContentControl）二選一；`ShowStage` 會重設成預設的 ScrollViewer。需要自己管捲動的頁面（實例設置、更多）放 `StageHost`。
- **實例設置頁**：新檔 `InstanceSettingsPage.xaml(.cs)`（UserControl），分頁：基本｜模組｜光影｜資源包｜快捷卡片。「基本」「快捷卡片」沿用舊視窗邏輯（載入器版本清單、Java 選擇、換載入器警告、卡片新增刪除）；底部「保存／取消」。`MainViewModel.OpenSettingsFor` 改呼叫 `MainWindow.ShowInstanceSettingsPage`（回傳 true＝要存檔）。
  - **取消／按「← 返回」會還原修改**（舊視窗的雙向綁定其實取消了也會改到 Config）：開頁時用 JSON 備份，`Discard()` 用反射寫回。
  - 舊的 `InstanceSettingsWindow` 已刪除。
- **ContentWindow → ContentView**：邏輯搬到新的 `ContentView`（UserControl，深色輸入框／清單／分頁／下拉），`ContentWindow` 變成只包一層的薄視窗（還能開，背景改深色）。新增 `ContentView.LockKind(kind)`：隱藏類型下拉，鎖定成單一類型。實例設置頁的模組／光影／資源包三個分頁各放一個 ContentView，**第一次切到該分頁才建立**（避免一開頁就連線三次）。搜尋、安裝、已安裝管理（啟用／停用／刪除／更新）、拖放匯入都沿用；這三個分頁的變更直接生效，不用按保存。
- `Theme.xaml`：新增 `DarkTabControl`／`DarkTabItem`（底線式分頁，非全域，頁面用 BasedOn 套用）。
- **沒做／待決定**：「伺服器」分頁（仍待確認是「實例的伺服器清單」還是「開服系統」）；下載頁（`DownloadStageControl`）的「已安裝」分頁與實例設置頁的內容管理功能重複，還沒整併；日誌、種子地圖、地圖管理、帳號、陶瓦、EasyTier、公開伺服器仍是彈出視窗。
- 待實測：1) 實例設置頁開啟、切分頁、保存後主畫面更新；2) 取消後名稱等欄位確實還原；3) 三個內容分頁各自只管一種類型、拖放 .jar／.zip 匯入仍可用（`Window_PreviewDrop` 仍用 `Tabs.SelectedIndex == 1` 判斷是否在「已安裝」分頁；ContentView 內部的 Tabs 沒變）；4) 內容頁裡深色 ComboBox 的選取項是否顯示名稱（FilterOption 有覆寫 ToString）；5) `UniformGrid` 在 `StageHost` 內是否撐滿寬度；6) 「更多」頁按鈕點下去仍會開對應視窗；7) `Style="{x:Null}"` 在 UserControl.Resources 有隱含樣式時能否還原成預設（可編輯下拉用）。


## Update 22 — 實例設置「伺服器」分頁（未編譯、未實測）
確認：「伺服器」＝**這個實例記錄的伺服器清單**（不是開服系統）。
- `Models.cs`：新增 `InstanceServer { Name, Address }`；`InstanceConfig.Servers`（`List<InstanceServer>`）。舊的 instance.json 沒有這欄位會得到空清單，不影響舊資料。`AutoJoinServer` 沿用，現在由這個分頁設定。
- `InstanceSettingsPage`：新分頁「伺服器」（在「模組／光影／資源包」之後、「快捷卡片」之前）。功能：新增（名稱可留空、位址必填、不可重複）、刪除、複製位址、「設為自動加入／取消自動加入」（★，同時只能一個）、「更新狀態」（`ServerPingService.PingAsync`，含 SRV；顯示在線人數／延遲／版本／MOTD）。第一次切到分頁會自動查一次全部。**要按「保存」才寫入實例**（取消會還原）。新類別 `ServerRow`（放在同檔）。
- `LaunchService`：`ServerIp`／`ServerPort` 改成把 `AutoJoinServer` 拆成 host 與 port（沒寫埠＝25565）。**之前把「host:port」整串塞進 ServerIp，帶埠的位址會連不上。** `ServerPort` 是 CmlLib `MLaunchOption` 的屬性，編譯時請確認名稱。
- 沒做：不讀寫遊戲本身的 `servers.dat`（遊戲內多人遊戲清單不會跟這份同步）。要同步的話需要 NBT 讀寫，且重寫時會丟掉伺服器圖示等欄位，所以先不動。「一鍵加入」目前是「啟動時自動加入」，沒有「選一個伺服器直接啟動」的按鈕。全域收藏（`servers.json`、更多 → 伺服器查找）仍獨立存在，沒有互通。
- 待實測：自動加入真的進到伺服器（含帶埠位址）；新增／刪除／保存／取消還原；切分頁自動查詢不卡 UI；`ServerRow` 內 `Run` 綁定顯示 ★ 與名稱。


## Update 23 — 下載頁整併「已安裝」、伺服器「啟動並加入」（未編譯、未實測）
- **下載頁（`DownloadStageControl`）**：移除「已安裝」分頁與 `InstalledPanel`／`RefreshInstalled_Click`（它只顯示項目數量，沒有管理功能，實際管理在實例設置頁的模組／光影／資源包分頁）。原本藏在裡面的「匯入本機整合包」改成頁首的獨立按鈕「匯入整合包」（遊戲／內容／匯入整合包／地圖）；匯入結果改用對話框顯示（不再有狀態列）。`ImportLocal_Click` 本身邏輯沒改，匯入期間沒有進度提示（按鈕按下後畫面看起來沒反應直到跳出對話框）。
- **啟動並加入**：實例設置 →「伺服器」每列新增「啟動並加入」。按下＝跟「保存」同一流程（含換載入器確認，按「否」就不啟動）→ 關閉設置頁 → 直接啟動這個實例並進入該伺服器，**只這一次**，不改變 ★ 自動加入設定。
  - `ILaunchService.LaunchAsync`／`LaunchService` 新增 `string? serverOverride`（優先於 `cfg.AutoJoinServer`）。
  - `InstanceSettingsPage.LaunchServer` → `MainWindow.ShowInstanceSettingsPage` 回傳 `(bool Saved, string? Server)` → `MainViewModel.OpenSettingsFor` 在保存、重新整理後呼叫 `RunLaunch(inst.Id, SelectedAccount?.Id, server)`。啟動用的是畫面上目前選的帳號（跟主畫面開始鍵一致）。
- 沒做：主畫面沒有「選伺服器直接啟動」的入口（目前只能從實例設置進去）；若要加，建議在開始鍵旁做下拉，列出該實例的 `Servers`，直接呼叫 `RunLaunch(..., server)`。
- 待實測：帶埠位址進得去；「啟動並加入」後設置頁關閉、進度條開始跑；匯入整合包按鈕仍能匯入；下載頁沒有殘留對 `InstalledStatus` 等名稱的引用（已用 grep 確認 .xaml/.cs 都沒有）。


## Update 24 — 空狀態新增按鈕、啟動鍵旁的伺服器下拉（未編譯、未實測）
- **空狀態**：`EmptyHint` 從單純文字改成 `StackPanel`（說明＋置中的「＋ 新增卡片」按鈕），沒有卡片（`Cards.Count == 0`）時才顯示，仍會浮動（`Motion.Float`）。右下角原本的「＋ 新增卡片」按鈕也一起改成新指令。
- **`AddCardCommand`**（`MainViewModel`）：直接用「目前選的帳號」（沒選就用第一個）為目前實例新增一張快捷卡片，存檔、重新整理，狀態列顯示「已新增卡片：帳號名」（會跳提示條）。**不再開實例設置頁**；要換卡片綁的帳號到實例設置 → 快捷卡片。沒有實例或沒有帳號時用狀態列提示。位置由既有的預設排列決定（沒擺過位置的卡片）。
- **啟動鍵旁 ▾**：左欄啟動鍵改成一列（啟動＋小按鈕「▾」）。按 ▾ 彈出選單（`ServerMenu_Click`，程式碼動態建立 `ContextMenu`），列出目前實例的 `Servers`，點一個＝`LaunchToServerCommand` → `RunLaunch(..., server)`，啟動並直接加入（只這一次，不動 ★ 自動加入）。實例沒有伺服器時選單顯示一行灰字提示。選單背景／前景手動設成深色；項目滑過的高亮仍是 WPF 預設樣式（可能偏淺色）。
- 待實測：空狀態按鈕新增卡片後卡片出現且有進場動畫；▾ 選單位置（`Placement=Top`）與深色是否順眼；選了伺服器後是否真的進得去；新增卡片後空狀態消失。
- 仍沒做：見上一輪清單（頁面化剩下的彈出視窗、`level.dat` 世界資訊、種子地圖圖形化、CurseForge 單檔搜尋、`servers.dat` 同步等）。


## Update 25 — 彈出視窗整合成頁面（1.3.0 UI 更新主軸；未編譯、未實測）
**十個獨立視窗全部改成嵌在覆蓋頁裡的 UserControl**（類別／檔名 `XxxWindow` → `XxxPage`）：`LogPage`、`SeedMapPage`、`MapsPage`、`AccountPage`、`TerracottaPage`、`RoomPage`、`PublicServerPage`、`ServerPage`（伺服器查找）、`LauncherSettingsPage`、`CurseForgePage`。`ContentWindow` 已刪除，改直接用 `ContentView`。**仍是彈出視窗（刻意保留，小東西）**：`NewInstanceWindow`、`JavaPickerWindow`、`InstancePickerWindow`、各種確認對話框／檔案選擇器／登入流程。
- **頁面容器**：`MainWindow.ShowPage(title, footer, page, push=false, scroll=false)` 把頁面放進 `StageHost`，回傳 `Task`（覆蓋頁關閉或被別的頁面取代時完成，所以 `OpenAccounts`／`OpenContent`／`OpenCurseForge`／`OpenLauncherSettings` 仍能在關閉後 `RefreshAsync`）。`push=true` 疊在目前頁面上，「← 返回」會回上一頁（伺服器查找 → 公開伺服器用這個）；沒有上一頁時關閉覆蓋頁。`scroll=true` 外包 ScrollViewer（陶瓦、EasyTier、啟動器設定這類內容高的頁面）。`StageBack_Click` 關閉時會把 `StageHost.Content` 設 null，頁面的 `Unloaded` 負責取消事件訂閱／停計時器（原本的 `Closed +=` 全部改成 `Unloaded +=`；LogPage 的計時器、Terracotta／Room 的事件訂閱靠這個）。
- **深色樣式**：新檔 `PageStyles.xaml`（TextBox／PasswordBox／CheckBox／ListBox／ComboBox／TabControl／ProgressBar／Separator），每個頁面的 `UserControl.Resources` 合併它。原本寫死的 `Gray`／`#FF00AAAA` 改成 `MutedBrush`／`AccentBrush`。可編輯下拉用 `Style="{x:Null}"` 還原預設。
- **進入點**：`MainViewModel` 的 `OpenServers／OpenLog／OpenCurseForge／OpenMaps／OpenAccounts／OpenLauncherSettings／OpenSeedMap／OpenTerracotta／OpenRoom／OpenContent` 都改走 `ShowPage`（部分由 `void` 改成 `Task`）；下載頁的「地圖」按鈕、「更多」頁的各項也都是頁面。
- 注意：頁面是「取代」目前的覆蓋頁內容，不會疊在下載頁等上面（只有 `push` 才會疊）。從下載頁按「地圖」→ 返回 → 回到主畫面，不是回下載頁。陶瓦頁的版權標示仍在 `TerracottaPage.xaml`（授權條款要求，不要移除）。
- 待實測：1) 能不能編譯（所有 `Window.GetWindow(this)` 取代處、`UserControl` 沒有的成員，如有人用到 `Title`／`Close()`）；2) 各頁在深色背景下文字／輸入框是否可讀、DataGrid 之類沒用到；3) 日誌頁會即時更新、返回後不再更新（Unloaded）；4) 陶瓦／房間頁開房、狀態更新、返回再進入；5) 伺服器查找 → 公開伺服器 → 返回；6) 地圖管理的匯入／備份／刪除對話框 owner 正確；7) 帳號頁的登入流程（Microsoft 登入視窗是否仍能正常彈出）；8) 啟動器設定頁高度與捲動。


## Update 26 — 小對話框也收進主視窗（未編譯、未實測）
`NewInstanceWindow`、`InstancePickerWindow`、`JavaPickerWindow` 刪除，改成 `NewInstancePanel`、`InstancePickerPanel`、`JavaPickerPanel`（UserControl），顯示在主視窗最上層的「對話框層」。
- **`MainWindow.ShowModal(title, build, width)`**：`build` 收到 `close(bool)` 讓內容自己關閉，回傳 `Task<bool>`（true＝確定）。層在 `ModalLayer`（ZIndex 200，半透明遮罩＋置中的 `ModalBox`），**Esc＝取消**。已有對話框開著時再呼叫，舊的會以 false 結束。對話框內容用 `PageStyles.xaml` 的深色樣式。
- 呼叫端：`AddInstance`（拿 `panel.InstanceName／McVersion／Loader`）、`OpenInstancePicker`、`InstanceSettingsPage.PickJava_Click`（現在是 async）。選實例面板的「新增實例」會先關掉自己再開新增對話框（避免疊兩層）；「刪除實例」仍是原本的指令（含它自己的確認對話框）。
- 新增實例面板：版本清單取得失敗時改用對話框提示（原本會直接丟例外）。
- **還是系統視窗的**：`MessageBox` 確認／提示、檔案／資料夾選擇器（`OpenFileDialog`／`OpenFolderDialog`）、Microsoft 登入（瀏覽器／系統流程）。確認對話框之後可做成同一套 `ShowModal` 的「確認面板」（刪除帳號、刪除地圖、更換載入器、刪除實例等，散在很多檔案）。
- 待實測：新增實例對話框開啟、版本載入、建立後清單更新；Esc 關閉；遮罩下面的頁面不會被點到（遮罩 Rectangle 會吃掉滑鼠事件，預期如此）；選 Java 對話框（620 寬）在實例設置頁內能開、選完寫回；開著對話框時提示條仍在對話框下面（ZIndex 100 < 200）。


## Update 27 — 確認／提示改成視窗內面板（未編譯、未實測）
- 新增 `Dialogs`（`Dialogs.cs`）：`ConfirmAsync(title, message, yes, no)`、`InfoAsync(title, message)`、`ChoiceAsync(title, message, params buttons)`（回傳按鈕索引，Esc＝-1）。內部走 `MainWindow.ShowModal`，第一個按鈕是主要動作（放最右、實色），其餘用 `GhostButton`。主視窗還沒載入時退回系統 `MessageBox`。`App.xaml.cs` 啟動早期的警告仍用 `MessageBox`（那時主視窗還沒出現）。
- 已換掉的：刪除實例（`MainViewModel`）、刪除帳號（`MainViewModel`、`AccountPage`）、刪除地圖（`MapsPage`）、刪除已安裝內容（`ContentView`）、更換載入器確認（`InstanceSettingsPage`）、有新版本（`MainViewModel`，三個按鈕：下載並更新／稍後／略過這個版本）；提示類（CurseForge 需手動下載、匯入整合包結果、找不到實例、無法開啟資料夾／網頁）用 `_ = Dialogs.InfoAsync(...)`。
- `InstanceSettingsPage.Save_Click` 改成 `async void`＋`SaveAsync()`；「啟動並加入」`await SaveAsync()` 後才檢查 `_saved`。
- 限制：同一時間只有一個對話框，**在對話框開著時再開另一個，舊的會以「取消」結束**。已知情況：選實例面板裡按「刪除實例」→ 確認面板出現時選實例面板會被關掉。`NewInstancePanel` 取不到版本清單的提示仍是系統 `MessageBox`（避免把自己關掉）。
- **Microsoft 登入維持 WebView2 內嵌視窗**（`JELoginHandler.Authenticate()` 預設；文件確認用 WebView2，不是預設瀏覽器）。改用預設瀏覽器需要 `XboxAuthNet.Game.Msal` 加上通過 Mojang 審核的 Azure Client ID（審核表單 `https://aka.ms/mce-reviewappid`）；使用者目前無法申請，先不做。之後若有 Client ID：在啟動器設定加選填欄位，填了走 `new JELoginHandlerBuilder().WithOAuthProvider(new MsalCodeFlowProvider(app))`，沒填維持現狀。注意 MSAL 自己的權杖快取會繞過 `MicrosoftSessions` 的 DPAPI 加密設計，需要另外處理。
- 待實測：各刪除確認、更新對話框三個按鈕、更換載入器確認後保存；Esc 取消；刪除實例從選實例面板觸發的行為。


## Update 28 — 項目詳情頁：點模組 → 列出所有版本 → 選一個安裝（未編譯、未實測）
（註：Update 17、18 是先前另一個對話做的，程式已在這份專案裡，但文字原本沒進 HANDOFF，已補在 Update 19 前面。）
- **新增共用控制項 `ProjectDetailView`**（`ProjectDetailView.xaml(.cs)`）：覆蓋在搜尋結果上的詳情頁。內容：標題、簡介、「安裝到：實例（版本 · 載入器）」、版本清單（版本號、正式／測試／早期、支援的 MC 版本、載入器、日期、下載數）、更新內容摘要、必要前置（文字）與可選前置（勾選）、「安裝此版本」。上方兩個勾選：「只顯示符合目前實例的版本」（預設勾，整合包不套用）、「自動安裝必要前置」（預設勾）。事件 `BackRequested`（← 返回結果，由宿主切換顯示）、`Installed`。入口 `ShowAsync(IVersionSource, ContentHit, InstanceConfig? target)`（Update 29 起版本來源是 `IVersionSource`，Modrinth 與 CurseForge 各自實作）。
- **Core**：`ModrinthService` 改成 `partial`，新方法在 `ModrinthVersions.cs`（不動 `IModrinthService`／`Contracts.cs`）：`GetVersionsAsync(projectId, mc?, loader?)`（`/project/{id}/version`，新→舊）、`GetProjectTitlesAsync(ids)`（`/projects?ids=`，顯示前置名稱）、`InstallVersionAsync(instanceId, kind, projectId, versionId, installRequired, optionalProjects)`。型別 `ContentVersion`、`VersionDep` 定義在同檔。
  - 安裝順序：必要前置（有勾才裝）→ 使用者勾的可選前置（裝不起來只回警告，不中斷）→ 本體（沿用既有 `DownloadAsync`：白名單＋SHA-1 驗證）→ 用 sha1 查 `POST /version_files`，**刪除同專案的其他版本檔案**（避免同一個模組放兩個版本）；清理失敗只回警告。
  - 前置模組仍是「該實例可用的最新版」（`InstallProject`），不鎖定前置的版本。整合包：下載該版本的 .mrpack 走 `ImportMrpackAsync` 建立新實例。
- **接入兩處**：
  1. `ContentView`（實例設置的模組／光影／資源包分頁、模組與內容頁）：根改成 `Grid`，`MainDock`（原內容）＋ `Detail`；結果清單 `ResultsList` 點選項目 → 開詳情（原本的「安裝」按鈕仍是直接裝最新版，不會觸發選取）。
  2. 下載頁「內容」分頁：`Detail` 放在 `ContentPanel` 內、`Grid.RowSpan="3"`；結果清單改深色並點選開詳情。**順便補上「安裝到」下拉（`TargetBox`）**：原本「安裝到實例」只在「遊戲」分頁，內容分頁看不到，安裝目標是不是你要的完全看運氣；現在兩個下拉雙向同步（`DownloadStageControl.Detail.cs`）。
- 限制：（Update 29 已補上 CurseForge 版本選擇，見下。）
- 待實測：1) 能不能編譯（`ModrinthVersions.cs` 的 JsonElement 用法、`ProjectDetailView` 與 `PageStyles.xaml` 合併）；2) 點項目後詳情頁蓋住搜尋頁、返回後回到原本的搜尋結果與捲動位置；3) 預設勾選「只顯示符合目前實例」時清單正確，取消勾選顯示全部；4) 裝舊版本後，資料夾裡新版本的檔案被刪、舊版保留；5) 可選前置勾選後確實安裝；6) 整合包選特定版本建立實例；7) 下載頁兩個「安裝到」下拉同步。
- 還沒做：前置模組鎖定版本、詳情頁顯示專案圖示／長說明。


## Update 29 — CurseForge 版本選擇（未編譯、未實測）
- **`IVersionSource`**（`Core/Services/VersionSource.cs`）：`ListVersionsAsync(hit, target, matchTarget)`、`GetProjectTitlesAsync(ids)`、`InstallVersionAsync(target, hit, version, installRequired, optionalProjects)`。`ModrinthService`（`ModrinthVersions.cs` 內加了轉接方法）與 `CurseForgeService`（新檔 `CurseForgeVersions.cs`）都實作它；`ProjectDetailView.ShowAsync` 改收 `IVersionSource`，不再認得具體服務。
- **CurseForge**：`CurseForgeService` 改成 `partial`。
  - 列版本：`GET mods/{id}/files`（pageSize 50，最多 3 頁），篩選用 `gameVersion` 與 `modLoaderType`（沿用 `LoaderFilter`：NeoForge 1.20.1 用 Forge、Quilt 另外加 Fabric）；過濾 `isAvailable=false`；`gameVersions` 陣列裡數字開頭的當 MC 版本、`Forge／Fabric／NeoForge／Quilt` 當載入器；`releaseType` 1／2／3＝正式／測試／早期；`ContentVersion.Number`＝displayName、`Name`＝fileName。前置：`relationType` 3＝必要、2＝可選。
  - 前置模組名稱：`POST mods { modIds }`。
  - 安裝：必要前置（沿用 `InstallProjectAsync`，裝該實例可用的最新版）→ 勾選的可選前置 → 本體（`downloadUrl` 空就走 `download-url`，作者不開放第三方下載會丟明確訊息請手動下載）→ **清理舊版本：取得該專案所有檔案的檔名，刪掉資料夾裡同名（含 .disabled）而不是剛裝的檔案**。限制：作者改過檔名或使用者手動改名的舊檔找不到（不用 CurseForge 的 fingerprint／murmur2）。
  - 整合包：`InstallModpackAsync` 新增 `int? fileId`（指定整合包檔案，null＝原本的「挑最新正式版」）。
- **下載頁**：「來源」選 CurseForge 時，點項目也會開詳情頁（用 `_curseForge`）；要有 CurseForge 金鑰（目前入口見 `CurseForgePage`／啟動器設定頁）。`ContentView`（實例設置分頁）仍只有 Modrinth（它本來就沒有 CurseForge 搜尋）。
- 待實測：1) CurseForge 詳情頁版本清單與篩選（取消「只顯示符合」能看全部）；2) 沒金鑰時的錯誤訊息；3) 選特定版本安裝、必要前置自動裝；4) 舊版本檔案被清掉；5) 整合包選特定檔案建立實例；6) 沒有 `downloadUrl` 的檔案顯示手動下載提示。

## Update 30 — 配色改成 R、G、B 拉條，每個部分都能調（未編譯、未實測）
- **範圍**：7 個部分，全部有 R／G／B 三條拉條（0–255）：強調色、視窗背景、面板／卡片底色、外框與分隔線、主要文字、圖標／次要文字、強調色上的文字（主按鈕字色）。對應 Theme.xaml 的 `AccentBrush`、`WindowBg`、`PanelBg`、`LineBrush`、`TextBrush`、`MutedBrush`、`OnAccentBrush`；強調色同時改 `GlowEffect`／`SoftGlowEffect` 的發光顏色。
- **新檔**：`ThemeEditor.cs`（`ThemeSettings`＝七個色碼的資料與預設值；`ThemeEditor`＝設置頁的編輯控制項：每部分一列「色塊＋名稱＋色碼」，點開展開三條拉條，拖動即時套用）、`MainWindow.Theme.cs`（`LoadTheme`／`ApplyTheme`／`SetBrush`／`SetEffectColor`／延遲存檔；從 `MainWindow.xaml.cs` 搬出並刪掉舊的 `AddColorControl`／`TryParseColor`／`ThemeData`）。
- **行為**：以前是把資源換成新刷子（只有之後建立的畫面看得到，`StaticResource` 的視窗背景、面板不會變）；現在**就地修改現有刷子的 `Color`**（刷子被凍結才改成換新刷子），所以拖動時整個視窗即時變色。存檔延遲 0.5 秒（拖動中不寫檔）；`theme.json` 欄位改成 `Accent／Window／Panel／Line／Text／Muted／OnAccent`，舊檔的 `Icon` 會讀成 `Muted`，缺的欄位用預設值。設置頁保留「強調色預設」六個色塊（只改強調色）與「恢復預設配色」；色碼輸入框已移除（輸入到一半的色碼崩潰問題不復存在）。
- **限制**：程式碼裡寫死的顏色不會跟著變（例如輸入框底 `#FF131313`、部分面板 `#FF141414`／`#FF1C1C1C`、`#FF888888` 灰字、`Gray`）；設得太接近（例如文字色＝背景色）會看不見，用「恢復預設配色」還原。Slider 還是 WPF 預設樣式（淺色軌道）。
- 待實測：拖動時視窗背景／面板／文字／發光即時變色；重開程式配色保留；舊的 theme.json 能讀；`SetBrush` 的刷子沒被凍結（若被凍結，StaticResource 處要重開才會變，需要把 Theme.xaml 的刷子改成 `x:Shared` 或用 DynamicResource）。


## Update 31 — 每個部件都能自訂圖片（素材換皮重做；未編譯、未實測）
- **起因**：原本只有 `ArtButton` 一種模板吃圖，且只有主畫面 6 顆有指定檔名；其他按鈕、面板、輸入框、分頁、進度條都不吃圖，換圖要重開，`Assets/README.txt` 列的 `avatar_frame`／`avatar_default`／`card`／`btn_instance_*` 其實沒接到畫面。
- **新檔 `Skin.cs`**：
  - `SkinManager`（單例）：`Slots`（部位清單，key＝檔名不含 .png）、`Get(key)`（快取）、`Import`（任何格式轉 PNG 寫進 `%AppData%\InnovationEasyLauncher\Assets\`）、`Clear`／`ClearAll`、`Options`／`SetOptions`（`skin.json`：`Slice` 九宮格邊距、`Pixel` 像素風）、`ExportPack`／`ImportPack`（zip；匯入只收根目錄、檔名是已知部位的 png 與 skin.json）、`Start()`（`FileSystemWatcher` 監看素材資料夾，400ms 防抖後 `Reload()`）、`Changed` 事件。**不再需要重開才生效。**
  - `SkinBorder : Border`：可直接取代 `Border`。`Slot`（部位名）、`State`（`_hover`／`_pressed`／`_disabled`，沒對應圖就退回一般圖）、`Fit`（Fill／Cover／Contain）、唯讀 `HasImage`。有圖就畫圖（取代底色與邊框，`OnRender` 不呼叫 base），沒圖就畫原本的 Border 樣子；`Slice>0` 時走九宮格（9 個 `CroppedBitmap`，快取在 `SkinSurface`）。Loaded／Unloaded 訂閱 `Changed`，避免洩漏。
  - `HideWhenBakedConverter`：按鈕用 `ArtProps.File` 指定「圖上已畫字」的圖且圖存在 → 隱藏程式畫的文字與裝飾；沒指定 File 的通用按鈕（`btn_primary`／`btn_ghost`）文字一律由程式畫在圖上。
- **部位（23 個，另加按鈕狀態圖）**：背景與標誌（`background`、`logo`、`avatar_frame`）、面板（`panel_main`、`card`、`modal`、`menu`）、按鈕（`btn_primary`、`btn_ghost`、`btn_launch`、`btn_instance_settings`、`btn_instance_pick`、`btn_settings`、`btn_download`、`btn_p2p`、`btn_more`、`btn_music`；全部含 `_hover`／`_pressed`／`_disabled`）、輸入與控制項（`input`、`dropdown`、`tab`、`tab_selected`、`progress_track`、`progress_fill`）。要新增部位：在 `SkinManager.Slots` 加一行，再把對應的 `Border` 換成 `<local:SkinBorder Slot="…">`。
- **模板改動**：
  - `Theme.xaml`：`ArtButton`（預設 Slot＝`btn_primary`，所有沒指定樣式的 Button 都吃它）、`GhostButton`（`btn_ghost`，改成 Grid＋`SkinBorder`＋Label，透明度改套在 Root）、`NavButton`（新：右欄按鈕以前完全不吃圖）、`DarkProgress`（`PART_Track`／`PART_Indicator` 改 `SkinBorder`，有填充圖時隱藏光澤動畫）、`DarkComboBox`（`dropdown`、彈出清單 `menu`）、`ContextMenu`（`menu`）、`DarkTabItem`（`tab`，選中時 Slot 換成 `tab_selected`）。新增資源 `HideWhenBaked`。舊的 `Fallback`＋`Image`＋`Asset` 轉換器寫法只剩沒人用的 `CardButton`。
  - `PageStyles.xaml`：`TextBox`／`PasswordBox` 加了自訂模板（`SkinBorder` Slot＝`input`＋`PART_ContentHost`），順便加鍵盤焦點時邊框變強調色。檔頭新增 `xmlns:local`。
  - `MainWindow.xaml`：背景（漸層當預設底，`background` 蓋上去，`Fit=Cover`）、主外框（`panel_main`）、頭像外框（`avatar_frame`，疊在頭像上）、快捷卡片（`card`）、對話框 `ModalBox`（`modal`）改 `SkinBorder`；「實例設置」「實例選擇」指定 `btn_instance_settings`／`btn_instance_pick`；`ArtProps.File` 值改成不含 .png（程式兩種都接受）。
  - `MainWindow.xaml.cs`：標誌改 `ApplyLogo()`，訂閱 `SkinManager.Changed`；啟動時 `SkinManager.Instance.Start()`。
- **介面 `SkinEditor.cs`**（設置頁兩個入口都有：右欄「設置」與「更多 → 啟動器設定」，後者取代舊的素材文字清單與 `OpenAssets_Click`）：依群組列出所有部位，每列縮圖＋狀態（自訂／exe 旁素材／預設）＋「選圖」「清除」；有圖的部位多一列「九宮格邊距（px）」與「像素風」；按鈕有三種狀態圖的子列；上方「開啟素材資料夾／匯出素材包／匯入素材包／全部清除」。
- **行為變更**：`avatar_default.png` 從來沒接到畫面，已從清單移除；`Assets/README.txt` 由 `Skin.cs` 的部位清單重新產生。通用按鈕（`btn_primary`）有放圖後，所有按鈕底都變成那張圖，文字仍由程式畫（顏色用 `OnAccentBrush`，圖太亮要自己調）。
- **限制**：還不能換圖的部件：CheckBox、ScrollBar、Slider、ListBox 項目、Toast 提示條、更新對話框以外的系統視窗（檔案選擇器等）；進度條光澤動畫在有填充圖時關閉；`CardButton` 沒人用、沒改；九宮格切片用 `CroppedBitmap`，縮放後相鄰切片之間可能有細縫（可試「像素風」）；啟動時 `FileSystemWatcher` 失敗會退回重開才生效。
- 待實測：1) 能不能編譯（`SkinBorder` 的 `OnRender` 覆寫、`DependencyProperty` 宣告、XAML 內 `<local:SkinBorder.Background>` 與 `<Border.Style>` 寫在 `local:SkinBorder` 內是否被接受、`MultiBinding` 的 `RelativeSource` 屬性寫法）；2) 沒放任何圖時外觀與 Update 30 完全一樣；3) 選圖後立刻套用、清除後恢復；手動把檔案丟進資料夾也會更新；4) 九宮格邊距、像素風；5) 按鈕三種狀態圖；`btn_launch` 等「圖上畫字」的按鈕有圖時文字消失；6) 輸入框模板：多行 TextBox（`AcceptsReturn`）的捲動與焦點邊框、密碼框；7) 進度條填充圖寬度變化時的顯示；8) 匯出／匯入素材包；9) 對話框 `modal` 圖與 `Esc`／遮罩行為不受影響；10) 主題配色拉條仍正常（`SkinBorder` 沒圖時走 base，應該同前）。


## Update 32 — 陶瓦聯機改成隨安裝包內附（未編譯、未實測）
- **核對下載檔**：使用者提供 `terracotta-0.4.2-windows-x86_64-pkg.tar.gz`（SHA-256 `07ebe139…45f0`），內容只有最外層兩個檔：`terracotta-0.4.2-windows-x86_64.exe`（9.4 MB，PE x64）與 `VCRUNTIME140.DLL`（97 KB，PE x64）；exe 確實匯入 `VCRUNTIME140.dll`，所以 DLL 必須和 exe 同資料夾（`ExtractTarGz` 與 `FindExe` 已涵蓋）。
- **內附**：兩個檔原樣放進 `third_party/terracotta/`，另有 `NOTICE.txt`（版權、版本、SHA-256、原始碼網址、上游 AGPL 例外條款全文）。`.gitattributes` 標成 binary。上游 `LICENSE`（AGPL-3.0 全文，34522 位元組，SHA-256 `4df3c306…a3b`，由使用者下載提供、逐位元組複製）已放在 `third_party/terracotta/LICENSE`；上游的例外條款只寫在其 README，不在 LICENSE 內，已收進 `NOTICE.txt`。
- **授權依據**：上游 README 的例外條款 (1)「以打包方式包含未經修改的二進位且沒有靜態／動態鏈接」與 (2)「透過 HTTP API 互動並在介面明顯處標示版權」，兩者皆符合；我們的專案維持 MIT。只要不改動這兩個檔就不會被 AGPL 涵蓋；日後要換版本，請連 NOTICE 一起更新。
- **程式**：`IEML.App.csproj` 新增 `<Content Include="..\..\third_party\terracotta\*" …>`，建置與發佈（含 `publish.ps1`、GitHub Actions 的 `dist/*` 壓縮）都會輸出到 `third_party\terracotta\`。`TerracottaService`：`FindExe() ?? InstallBundled() ?? DownloadAsync()`；`InstallBundled()` 只在 x64 使用，把內附檔案複製到 `tools\terracotta\`（不直接在程式資料夾執行，避免自動更新換檔時被鎖住），失敗或 ARM 就退回下載。
- 待實測：1) 發佈輸出真的有 `third_party\terracotta\*` 2) 第一次開 P2P → 陶瓦不下載就能啟動 3) 刪掉 `tools\terracotta` 再開，會重新複製 4) 刪掉內附資料夾後會退回下載 5) 發行包大小增加約 9.5 MB。


## Update 33 — 第一次 CI 編譯的錯誤修正（未再編譯）
- **GitHub Actions 第一次編譯**（Update 17 之後累積的所有改動第一次實際編譯）回報：7 個頁面的 `.xaml.cs`（`LauncherSettingsPage`、`LogPage`、`MapsPage`、`PublicServerPage`、`SeedMapPage`、`ServerPage`、`TerracottaPage`）用了 `UserControl` 卻沒有 `using System.Windows.Controls;`（CS0246）→ 已補上。另外掃過所有 `.cs` 檔的 WPF 與常用命名空間（Controls、Media、Input、Threading、Imaging、Animation、Data 等），沒有發現其他缺漏。
- 三個 CS8826 警告：`MainViewModel` 的 `OnMusicVolumeChanged`、`OnSelectedInstanceChanged`、`OnSelectedAccountChanged` 參數名稱是 `v`，MVVM Toolkit 產生的宣告是 `value` → 已改成 `value`（本體一併改名）。
- 注意：CS0246 這類「找不到型別」的錯誤會讓編譯器停在早期階段，修掉之後**可能還會冒出新的語意錯誤**（API 名稱不對、型別不符等）；請再跑一次編譯，把新的錯誤清單貼回來。
- 手機流程：倉庫的 `build.yml`／`unzip.yml` 已改成可手動執行並可帶版本標籤發佈；編譯失敗時錯誤清單會整理在該次執行的摘要頁。
- README 版本徽章（`README.md`、`README.en.md` 第 5 行）：兩份本來就都不是寫死的，都是 shields.io 的 `github/v/release` 動態徽章（只有 label 文字不同）。繁中與英文顯示不同版本（1.2.3 對 1.3.0）是因為 GitHub 的圖片代理與 shields 各自快取了兩個不同網址的圖；兩個網址都加上 `&cacheSeconds=300`（新網址＝新快取鍵，會立刻重抓，之後最多 5 分鐘更新）。


## Update 34 — 啟動器版本號與設置頁預設色塊（未編譯、未實測）
- **版本號一直是 1.2.1EF 的原因**：版本只寫在 `IEML.App.csproj`（`<Version>1.2.1</Version>`、`<InformationalVersion>1.2.1EF</InformationalVersion>`），而 Release 標籤是另外手動推的（已到 v1.3.0）；CI 編譯時沒有用標籤覆蓋，所以發佈出去的程式永遠顯示 csproj 的舊值。更糟的是：程式版本 1.2.1 低於最新 Release v1.3.0，自動更新會一直提示「有新版本」。
- 修正：1) `build.yml`（倉庫的 `.github/workflows/build.yml`，需用網頁編輯器貼上新版）在有標籤時，把標籤傳給 `dotnet publish`：`-p:Version=<標籤開頭的數字部分>`（例如 `v1.3.1EF` → `1.3.1`）與 `-p:InformationalVersion=<去掉 v 的完整標籤>`；2) `IEML.App.csproj` 改成 `1.3.1`／`1.3.1`（本機編譯用，之後下一個要發佈的標籤請用 `v1.3.1` 或更大；`v1.3.0` 已經存在不能重發）。README 的「發佈新版本」兩個語言都改寫。
- **設置頁「強調色預設」六個色塊顏色都一樣**（使用者截圖）：色塊原本是 `Button`，全域 `ArtButton` 樣式的範本忽略 `Background`，全部變成青色 → 改成 `Border`（有顏色、圓角、滑鼠變手指、提示色碼），點擊事件改 `MouseLeftButtonUp`。
- 觀察（使用者截圖）：設置頁的七個顏色目前顯示 `#FFFFFF`（主要文字是 `#010000`），主要文字接近黑色導致各部分的名稱在深色背景上幾乎看不見——應該是之前試拉條留下來的（存在 `theme.json`）；按「恢復預設配色」即可還原。之後可考慮：編輯器的名稱文字不要用可被調整的 `TextBrush`，或在把文字色調到和背景太接近時給提示。



## Update 35 — 配色全面生效、公開伺服器解析修正、自訂圖片改下拉、版本號（未編譯、未實測）
- **配色只有文字生效**：文字是 `DynamicResource`，其他（背景、面板、邊框、按鈕）都是 `StaticResource`，換色時資源被換成新刷子，舊引用不會更新。→ 所有 `.xaml` 內 `WindowBg/PanelBg/LineBrush/TextBrush/MutedBrush/AccentBrush/OnAccentBrush/GlowEffect/SoftGlowEffect` 的 `StaticResource` 一律改 `DynamicResource`（共 153 處）。設置頁內程式建立的元件改用 `Dyn(...)`（`SetResourceReference`）。對話框等「每次開啟才建立」的 `FindResource` 保持不變。
- **公開伺服器搜尋 0 筆的原因**：官方回應是 `{data:{items:[ServerSummary],page,totalPages,total}}`，位址在 `displayAddress{host,port}`／`primaryConnection`，人數與版本在 `primarySnapshot{outcome,playersOnline,playersMax,versionName}`；舊的 `Walk()` 要求同一物件同時有 name 與 address，所以永遠找不到。→ `PublicServerService` 改成依官方 OpenAPI 解析（保留舊 `Walk` 當備援），請求加上 `edition=java&limit=25`，錯誤回應會顯示 `error.message`，新增 `LastTotalPages/LastTotal` 給分頁用。待實測：實際回應欄位是否與文件一致。
- **自訂圖片**：`SkinEditor` 改成 ComboBox（`DarkComboBox` 樣式）選部位，只顯示該部位的縮圖／選圖／清除／九宮格／三種狀態；● 表示已有圖。
- **版本號**：`build.yml` 在 zip 內的版本沒有標籤覆蓋邏輯，而 `unzip.yml` 匯入時會刪掉 `.github`，所以倉庫的 workflow 不會更新。**必須用網頁編輯器把新的 `.github/workflows/build.yml` 貼到倉庫**。新版：標籤或手動輸入的 tag → `-p:Version=<數字>`、`-p:InformationalVersion=<完整標籤>`，並關閉 `+commit` 後綴。`csproj` 改 1.3.2。
- 使用者回報：陶瓦聯機正常，會跳防火牆提示（符合預期）。

## Update 36 — 下拉選單空白（未編譯、未實測）
- **深色下拉看不到選中的字**：`Theme.xaml` 的 `DarkComboBox` 範本的 `ContentPresenter` 沒綁 `ContentTemplateSelector`／`ContentStringFormat`；`DisplayMemberPath` 是靠 `ItemTemplateSelector` 實作的，所以排序／分類／載入器／執行端（都用 DisplayMemberPath）選中後一片空白。→ 補上 `ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"` 與 `ContentStringFormat`。
- **白色空下拉（MC 版本）**：原本 `Style="{x:Null}"` 還原成系統白底，且沒有任何項目。→ 新增深色可輸入樣式 `DarkEditableComboBox`（`PART_EditableTextBox`＋箭頭＋彈出清單），`McBox`、`LoaderVerBox` 改用；`ContentView.LoadMcVersionsAsync()` 用 `VersionService.ListAsync(false)` 填入正式版清單（離線退回內建清單），不改目前輸入框文字。
- 待實測：1) 四個篩選下拉顯示「相關度／全部…」 2) MC 版本可輸入也可展開選 3) 實例設置的載入器版本下拉外觀。

## Update 37 — 啟動器圖示（未編譯、未實測）
- 使用者提供的 1080×1080 青色「ZE」標誌 → 四周加 8% 黑邊後縮成 256px，輸出 `src/IEML.App/app.ico`（16/24/32/48/64/128/256 多尺寸）；`Assets/app_icon.png` 為預覽用副本（程式沒用到）。
- `IEML.App.csproj`：`<ApplicationIcon>app.ico</ApplicationIcon>`（exe／檔案總管圖示）＋ `<Resource Include="app.ico"/>`；`MainWindow.xaml` 加 `Icon="app.ico"`（視窗左上角與工作列）。
- 注意：單檔發佈後 Windows 檔案總管有時會快取舊圖示，換名稱複製一份或重開檔案總管即可看到新的。

## Update 38 — 多個實例目錄、實例設置只管理已安裝內容（未編譯、未實測）
- **實例目錄**：新檔 `IEML.Core/Services/InstanceRoots.cs`（`roots.json` 存 `Extra` 目錄清單與 `Active`）。預設目錄 `%AppData%\InnovationEasyLauncher\instances` 永遠在、不能移除。`InstanceService.GetInstanceDir` 改成 `InstanceRoots.DirFor(id)`：依序在每個目錄找 `<guid:N>`，找不到（新實例）就用「使用中」目錄；`ListAsync` 掃描所有目錄（不存在的目錄略過、重複 Id 去重）。`IInstanceService` 簽章沒動。
- **UI**：1) 新增實例面板多一個「實例目錄」下拉＋「＋ 新增目錄」（`OpenFolderDialog`，.NET 8 內建）；按「建立」時 `SetActive` 選的目錄（之後預設）。2) 「啟動器設定」新增「實例目錄」區：列出每個目錄（名稱、路徑、使用中標記）、設為使用中／開啟／移除（移除只是不再掃描，不刪檔），＋新增；變更後呼叫 `MainViewModel.RefreshInstancesPublicAsync()` 重新整理。
- 改掉寫死 `AppPaths.Instances` 的地方：`ContentView.InstDir`、`InstanceSettingsPage`（換載入器檢查 mods）。
- **實例設置的模組／光影／資源包分頁**：新增 `ContentView.ManageOnly()`——隱藏「搜尋與安裝」，只留「已安裝管理」（啟用/停用、刪除、檢查更新、開啟資料夾、拖檔匯入）。搜尋與下載留在主畫面的下載頁。
- 待實測：1) 新增目錄後實例清單出現該目錄內既有實例 2) 在外接目錄建立實例並啟動（`MinecraftPath` 用實際目錄）3) 移除目錄後實例消失、檔案還在 4) 目錄不存在（硬碟沒插）不報錯 5) 實例設置分頁只剩已安裝管理。
- 限制：實例不能在目錄之間搬移（要搬請手動搬資料夾再重開）；Mods/地圖等其他功能都透過 `GetInstanceDir`，應該自動跟著實際位置。

## Update 39 — 右欄按鈕再按一次收回頁面（未編譯、未實測）
- `MainWindow.xaml.cs`：新增 `_navKey`（目前覆蓋頁是哪個右欄按鈕開的）與 `ToggleNav(key)`。設置／下載／P2P／更多／音樂五個 `*Stage_Click` 開頭都呼叫它：同一個按鈕、覆蓋頁還開著 → 清掉返回堆疊後走 `StageBack_Click` 整個收回；按別的按鈕則直接切換。覆蓋頁遮住左＋中欄，所以頁面開著時只有右欄按鈕按得到，不會有「別處開的頁面」讓 `_navKey` 過期。
- 順手修競態：`_closeToken`——淡出關閉途中又開新頁面時，舊的淡出完成回呼不再把新頁面關掉（`ShowStage` 會取消待關閉）。實例設置頁的關閉回呼同樣處理。
- 待實測：1) 設置／下載／P2P／更多／音樂各按兩次會開→關 2) 下載→進 CurseForge 子頁→再按「下載」會整個收回 3) 開著設置時按下載會切換而不是關閉 4) 快速連按不會閃退或卡住（淡出途中再開）。


## Update 40 — 小控制項的過渡動畫（未編譯、未實測）
- **缺口**：按鈕、右欄、卡片本來就有動畫，但其他小控制項都是瞬間切換（勾選、列表、捲軸、滑桿、輸入框焦點、下拉的滑過／展開、右鍵選單）。
- **做法**：不去動共用的刷子（共用刷子會被凍結，也不能對它做顏色動畫），而是在控制項上**疊一層「滑過／選取／焦點」的圖層**，只對它的 `Opacity`／`ScaleTransform` 做動畫；顏色全部用 `DynamicResource`，配色改了會即時跟著變。時間 0.1–0.26 秒，`CubicEase`／`BackEase`（彈出效果）。
- **新檔 `Controls.xaml`**（`App.xaml` 在 `Theme.xaml` 之後合併）：
  - `CheckBox`：方框淡入青色底、勾號彈性縮放跳出（`BackEase`）、滑過有光圈、按下微縮。
  - `ListBoxItem`：滑過淡入白色底、選取淡入青色底並跳出左側亮條（`ScaleY` 彈出）。
  - `ScrollBar`（垂直＋水平）：細 11px、無箭頭，滑塊平時半透明、滑過／拖動淡入變亮。
  - `Slider`：青色已填軌道、圓形滑塊（有柔光），滑過放大 1.25 倍、拖動放大 1.5 倍。ThemeEditor 的 R／G／B 拉條與音樂音量都會套用。
- **改現有檔**：
  - `Theme.xaml`：`ArtButton` 發光改成獨立圖層（`GlowLayer`）淡入／淡出（原本是瞬間套用 `GlowEffect`；有換皮圖片時 `HasImage` 會隱藏這一層）；`GhostButton` 滑過的青色邊框改成光圈淡入；`DarkComboBox` 外框光圈淡入、展開時箭頭旋轉 180°、清單 `PopupAnimation="Fade"`；`DarkComboBoxItem`、`MenuItem` 的高亮底色改成淡入淡出。
  - `PageStyles.xaml`：`TextBox`／`PasswordBox` 焦點光圈改成淡入（範本根改成 `Grid`＋`Ring`）；移除會蓋掉全域 `CheckBox` 樣式的區域樣式（同樣移除 `ContentView.xaml`、`DownloadStageControl.xaml` 裡的）。
- **還沒做**：`CardButton`／`NavButton` 的發光（`SoftGlowEffect`）仍是瞬間套用、`TabItem` 選取底線滑動、`DarkEditableComboBox` 的過渡、`RadioButton`／`ToggleButton`。
- **待實測／可能的問題**：1) `Controls.xaml` 能否載入（樣式找不到資源、`Command` 靜態引用）；2) 預設的 `ScrollBar`／`Slider`／`ListBoxItem` 是全域隱含樣式，會影響所有頁面，確認 `ScrollViewer`、各清單的選取外觀沒有跑版（特別是 `ListBoxItem` 的內邊距與背景）；3) 勾選框載入時，已勾選的會播一次勾選動畫；4) 想調快慢：在 `Controls.xaml`／`Theme.xaml` 搜尋 `0:0:0.` 即可。


## Update 41 — 下載中心「遊戲」分頁重構：選版本 → 設定 → 真的安裝（未編譯、未實測）
- **舊版的問題**：一個頁面塞了 5 個輸入／下拉（可編輯的版本下拉、載入器、載入器版本、「安裝到實例」、快照勾選）＋摘要；「載入器版本」其實只是一個假欄位（永遠是「自動使用最新穩定版」）；「安裝到實例」放錯分頁；最嚴重的是**「建立實例」只寫一個設定檔，進度條直接跳 100%，遊戲檔案其實要等第一次啟動才下載**。
- **新設計（新檔 `GameInstallView.xaml(.cs)`，取代 `DownloadStageControl` 的 `GamePanel`；`x:Name` 仍是 `GamePanel`，分頁切換邏輯不用改）**：
  1. **步驟 1 選版本**：搜尋框（輸入 `1.20`、`24w` 即時篩選）＋「正式版／快照／舊版」三個分類鈕＋版本清單（版本號、類型、發佈日期，新→舊）。點一個版本進入步驟 2。
  2. **步驟 2 設定並安裝**：「← 換版本」；實例名稱（依版本與載入器自動命名，例如 `Minecraft 1.21.1`、`Fabric 1.21.1`；使用者自己改過就不再覆蓋；重名自動加 `(2)`）；載入器用按鈕選（原版／Fabric／Quilt／NeoForge／Forge）——**進入時並行查詢四種載入器在這個 MC 版本有沒有可用版本，沒有的標灰**；載入器版本下拉（只列真的存在的版本，預設選最新穩定版；原版時隱藏）；安裝摘要與實例目錄；「建立並安裝」。
  3. **安裝**：先建立實例（立刻通知主畫面刷新），再**真的下載**遊戲檔案、載入器與所需檔案，有進度條與階段文字（含百分比），可「取消」。取消或預先下載失敗時，實例仍已建立，第一次啟動會再補下載（畫面上會明說）。完成後出現「再建一個」。
- **Core 新增**：`GameInstaller`（`GameInstaller.cs`，`PrepareAsync(instanceId, progress, ct)`：檢查 Java → `InstallAsync(MC 版本)`（Forge／NeoForge 安裝程式要先有原版）→ `LoaderService.EnsureInstalledAsync` → `InstallAsync(載入器 versionId)`。邏輯與 `LaunchService` 的安裝部分一致，但不需要帳號、不啟動遊戲；沿用 `SharedCache.Apply`）。`VersionService.ListDetailedAsync()` ＋ `GameVersionInfo`（`Id`／`Type`／`Released`，日期用反射讀 CmlLib 的 `ReleaseTime`，不綁死型別；離線退回內建常用版本）。`ILaunchService`／`Contracts.cs` 沒動。
- **`DownloadStageControl` 的變動**：移除舊遊戲分頁的程式（`LoadVersionsAsync`、`UpdateSummary`、`Create_Click`、`UpdateLoaderVersionHint` 等）；`InstanceBox` 改成一個**不顯示**的 `ComboBox`，只負責和內容分頁的「安裝到」（`TargetBox`）同步、並給地圖與搜尋當「目前安裝目標」；新增事件 `InstanceCreated`，`MainWindow` 訂閱後呼叫 `Vm.RefreshInstancesPublicAsync()`。`GamePanel` 與 `ContentPanel` 現在包在同一個 `Grid` 裡（避免 `DockPanel` 對非最後子元素預設靠左、不會填滿的問題）。
- **沒做／已知限制**：實例目錄（`InstanceRoots`）只顯示目前使用中的那個，沒有在這裡切換（要換請用主畫面的「新增實例」面板）；快照與舊版沒有載入器版本可選時只能建立原版；整合包、地圖、內容搜尋維持原樣。
- **待實測**：1) 能否編譯（`GameInstallView` 的 tuple 與 `Task.WhenAll`、`CmlLib` 的 `InstallAsync(versionId)` 對載入器版本 id 是否可用）；2) 步驟 1 篩選與點選、換分類；3) 步驟 2 載入器標灰是否正確（尤其網路慢時的等待）；4) 預先安裝的進度與取消；5) Forge／NeoForge 安裝完成後第一次啟動不再重複下載；6) 建立後主畫面實例清單立即出現新實例。


## Update 42 — 建立實例：新增「愚人節」版本分類（未編譯、未實測）
- 「遊戲」分頁步驟 1 的分類鈕多了**「愚人節」**（正式版／快照／愚人節／舊版）。官方版本清單沒有愚人節這個類型（它們全是 `snapshot`），所以在 `GameVersionInfo`（`VersionService.cs`）用**名單＋命名規則**辨認：
  - 名單：`15w14a`、`1.RV-Pre1`、`3D Shareware v1.34`、`20w14infinite`、`22w13oneBlockAtATime`、`23w13a_or_b`、`24w14potato`、`25w14craftmine`。
  - 規則：非正式版，且名稱符合「第 13／14 週、後綴不只一個字母」（`^\d{2}w1[34][a-z]{2,}`）或含 `_or_`——正常快照（`24w14a`）與測試版（Combat Test）不會被誤判；之後新的愚人節版本（例如 `NNw14xxx`）多半會自動歸進來。漏掉或誤判就把版本號加進（或移出）`AprilFoolsIds`。
- 愚人節版本只出現在「愚人節」分類，不再混進「快照」；`TypeText` 顯示「愚人節版本」。選到愚人節分類時，清單下方會提醒「愚人節版本是玩笑版本，通常不能搭配載入器或模組」；載入器本來就會因為查不到版本而標灰，只能建立原版。
- 限制：離線時版本清單退回內建的常用正式版，沒有愚人節版本（畫面上會說明）；有空格的版本名（`3D Shareware v1.34`）能否正常下載安裝，要實測。



## Update 43 — 補完四個缺口：啟動進度條、遊戲頁切換實例目錄、發光／分頁底線動畫、詳情頁（未編譯、未實測）
1. **頁面蓋住左欄時看不到啟動進度條**：`MainWindow.xaml` 覆蓋頁內層 Grid 新增第 4 列 `StageStatusBar`（`Border` ＋ 狀態文字 ＋ `DarkProgress`，綁 `Status`／`ProgressPercent`）。預設 Collapsed，`IsBusy`（啟動、下載更新）為 true 時才顯示。
2. **「遊戲」頁可切換實例目錄**：`GameInstallView` 步驟 2 取代舊的 `RootText`，改成「實例目錄」下拉（`RootBox`，`InstanceRoots.All`）＋「＋ 新增目錄」（`OpenFolderDialog` → `InstanceRoots.Add`）；安裝摘要多一行「建立位置」；按「建立並安裝」前 `InstanceRoots.SetActive(選的目錄)`，之後 `GameInstaller` 用 `GetInstanceDir` 會找到實際位置。
3. **動畫**：`CardButton` 的發光改成獨立 `GlowRing` 圖層（細邊框＋`SoftGlowEffect`），滑過用 `Opacity` 淡入 0.16 秒／淡出 0.24 秒，移除原本滑過時瞬間設定 `Root.Effect` 的 Setter（有自訂圖片的卡片也一樣會有一圈發光邊框）。`DarkTabItem` 的選取底線改成獨立 `Underline`（`ScaleX` 從 0.2 長到 1＋淡入，取消選取時收回），不再瞬間換 `BorderBrush`（`Bd` 的 `BorderThickness` 保留，版面不變）。`NavButton` 本來就沒有發光效果（只有左側亮條與縮放，已是動畫），沒動。
4. **專案詳情頁**：
   - **前置模組鎖版本**：每個前置（必要／可選）一列：名稱＋版本下拉（預設「自動（最新相容版）」，展開時才查該專案的版本清單；可選前置一指定版本會自動勾選）。安裝時把「專案 id → 版本 id／CurseForge 檔案 id」傳給 `IVersionSource.InstallVersionAsync(..., ct, pinnedVersions)`（新增的可選參數，預設 null）。`ModrinthService.InstallProject`、`CurseForgeService.InstallProjectAsync` 新增 `pinned` 參數：有指定就取該版本／檔案（`version/{id}`、`mods/{pid}/files/{fid}`），沒有就維持原本「最新相容版」邏輯；遞迴前置也會帶著。
   - **專案圖示**：標題左邊顯示 `ContentHit.IconUrl`（只接受 https，失敗就不顯示）。
   - **長說明**：`IVersionSource.GetLongDescriptionAsync`（介面預設回傳 null）；Modrinth 取 `project/{id}` 的 `body`（Markdown），CurseForge 取 `mods/{id}/description`（HTML），統一用 `DescriptionText.Clean` 轉成純文字（最多約 2000 字）；「▸ 專案說明」按鈕展開，高度上限 150，可捲動。
- **待實測**：1) 專案能編譯（`file ??= throw`、介面預設方法、`ModrinthService.InstallProject` 的 `using var keepDoc`）；2) 啟動遊戲時開著任何頁面，底部出現進度列、完成後消失；3) 遊戲頁選另一個目錄安裝，實例出現在該目錄；4) 卡片滑過發光淡入淡出、分頁底線從中間長出；5) 詳情頁指定前置版本後，資料夾裡裝的是指定的那一版；6) 說明文字轉換的可讀性（特別是含很多 HTML 的 CurseForge 專案）。
- 限制：指定的前置版本不會檢查和實例 MC 版本／載入器是否相容（下拉會依實例篩選，但直接指定就照你的）；`CurseForge` 前置版本下拉是「最多 150 個檔案」的上限。

## Update 44 — build.yml 換成加強版
- `.github/workflows/build.yml` 採用使用者提供的版本（含 Update 35 的版本號邏輯）：標籤改用環境變數傳入（不直接把 `${{ }}` 內插進腳本，較安全）；`dotnet publish` 的輸出另存 `build.log` 並在失敗時 `exit`；新增「Summarize build errors」步驟——編譯失敗時把所有 `: error ` 行去重後（最多 200 條）寫進這次執行的「摘要」頁，手機上可直接複製。
- 注意：`unzip.yml` 匯入時仍會刪掉 `.github`，倉庫裡的 workflow 要用網頁編輯器貼。

## Update 45 — 移除所有「Beta」標示（未編譯、未實測）
- 視窗標題、主畫面標誌旁的 Beta 徽章、啟動器設定「關於」的版本文字與橘色「Beta 測試版」提示（改成一句灰色的「遇到問題請附上遊戲日誌回報」）、`AppInfo.Version` 讀不到時的備援值（`0.1.0-beta` → `1.0.0`）、Modrinth 的 User-Agent、兩份 README 的狀態徽章與說明，全部去掉 Beta。
- 沒動的：`VersionService` 裡的「舊版 Beta／Alpha」是 Minecraft 的舊版本類型名稱，不是啟動器的狀態。


## Update 46 — 「更多」新增小遊戲「地城探險」（像素小肉鴿；未編譯、未實測）
- 之前另一個對話寫到一半被用量限制打斷（`RoguelikeGame.cs` 沒存下來），這裡重寫完成。**「更多」頁預留的那一格**現在是「地城探險」，點下去用 `ShowPage(push: true)` 開新頁，「← 返回」回到更多。
- **新檔**：`RoguelikeGame.cs`（純邏輯，不碰 UI）、`RoguelikePage.cs`（`PixelArt` 8×8 像素小圖、`RoguelikeView` 畫面、`RoguelikePage` 操作頁；**純程式碼建 UI，沒有 XAML**）。
- **玩法**：15×11 地圖，5 層，回合制。方向鍵／WASD 移動，撞到怪物＝攻擊；空白＝等待；H＝喝藥水（+6 生命，花一回合）；R＝重來；下方也有按鈕。走到樓梯下樓（回復 3 生命）；第 5 層沒有樓梯，打倒**凋零骷髏王**就通關。每次進層重新隨機生成（4～6 個房間＋走廊）。
- **怪物**：殭屍（慢，每兩回合動一次）、骷髏（同列同行 5 格內直線就射箭，每兩回合一箭，傷害 2）、苦力怕（靠到周圍一格先「嘶嘶」蓄力，下一回合爆炸 5 傷害；躲開或先打倒；蓄力時閃白光）、凋零骷髏王（24 血，靠近時先舉劍一回合、下一回合才揮下 4 傷害，退開就能躲；每 6 回合召喚一隻骷髏）。傷害都會被防禦減免，最少 1。
- **道具**（撿到即生效，藥水進背包）：藥水（每層至少一瓶）、劍（攻擊 +1，上限約 5）、護甲（防禦 +1，上限 3）、金蘋果（最大生命 +3 並補滿）。起手：12 生命、攻擊 2、1 瓶藥水。
- **畫面**：`RoguelikeView : FrameworkElement`，`OnRender` 用 `DrawImage` 畫預先做好的凍結 `BitmapSource`（最近鄰縮放），放在 `Viewbox` 裡自動縮放；受傷時整個畫面閃一層紅；怪物血條；結束畫面（倒下／通關）顯示層數與擊殺，並記「本次最高層數」（只在這次執行期間，不存檔）。
- **沒做**：存檔、排行榜、視野／迷霧、音效、行動裝置觸控以外的操作優化。難度沒有實際玩過，數值（怪物血量、傷害、道具機率）都集中在 `RoguelikeGame.cs` 的 `MakeMob`／`RollKind`／`RollLoot`／`EnemyTurn`／`BuildFloor`，之後直接調。
- 待實測：能否編譯（`PixelArt.Make` 的目標型別 `new() {…}`、`RelayCommand(() => ShowPage(...))` 轉 `Action`、`FormattedText` 的字型）；鍵盤焦點（開頁後要能直接按方向鍵，點按鈕後焦點仍在頁面，按鈕設了 `Focusable=false`）；各層是否都生成得出樓梯／王、玩家出生處是否永遠和樓梯連通（走廊依序連接各房間，理論上連通）；第 5 層的王與召喚是否過難。

- **Update 46 補充：玩家頭像**。目前選的帳號是**正版（Microsoft）**且有 UUID 時，地城探險的玩家改用自己的皮膚頭像（`https://mc-heads.net/avatar/{uuid}/32`，與主畫面頭像同一個第三方來源，32px＝8×8 放大 4 倍剛好一格，加黑框）；**離線帳號、沒網路、服務失敗、還沒載入完**都維持預設像素小人（不會報錯）。`RoguelikePage(Account? account)`，入口傳 `Vm.SelectedAccount`；頭像在頁面載入後非同步抓，載好才換，同一個 UUID 本次執行內快取（`HeadCache`）。只在開頁時讀目前帳號，遊戲中途切帳號不會即時更新。


## Update 47 — 「地城探險」改成即時彈幕肉鴿（類 Brotato；未編譯、未實測）
Update 46 的回合制地城整個換掉（`RoguelikeGame.cs`、`RoguelikePage.cs` 全部重寫；`PixelArt` 的 8×8 像素圖保留給敵人與玩家用，道具圖已不再使用）。入口還是「更多 → 地城探險」。
- **玩法**：場地 640×400（邏輯像素，`Viewbox` 自動縮放）。**WASD／方向鍵移動，自動瞄準最近的敵人射箭**；P 暫停、R 重來。共 10 波，每波 `20 + (波數-1)×2.5` 秒（上限 45 秒），撐過就進**商店**，最後第 10 波要**擊敗凋零骷髏王**才通關。敵人出生前會先在地上閃紅色 X（約 0.8 秒）。
- **敵人**：殭屍（追著撞，接觸傷害，有 0.8 秒冷卻）；骷髏（保持距離射箭，第 6 波起三連發）；苦力怕（衝向玩家，靠近 36 以內停住閃白光 0.55 秒後爆炸，範圍 62）；**王**（450 血，一圈彈幕／瞄準五連發／偏移一圈＋三連發輪流，血量低於一半射更快、環更密；每 9 秒召喚 3 隻殭屍；接觸傷害）。數值隨波數成長（血量 ×(1+0.4×(波數-1))）。
- **玩家**：20 生命、傷害 6、射速 2／秒、射程 230、移速 150；暴擊 10%（傷害 ×2、金色箭）；被打後 0.4 秒無敵（閃爍）；護甲公式 `傷害×15/(15+護甲)`，最少 1。
- **材料與商店**：擊殺掉綠色材料（靠近自動吸、波結束時全部自動收走）。每波結束回復 30% 最大生命，商店隨機 4 項強化（傷害／射速／射程／最大生命／移速／護甲／拾取範圍／再生／重弩／疾風弓／龜殼／雙重射擊（最多 4 支）／穿透箭（最多 4）），價格隨波數 +12%／波；可「重抽」（費用 2＋波數/2，每次 +1）。部分道具有代價（例如重弩降射速、龜殼降移速）。
- **玩家頭像**：維持 Update 46 補充的行為（正版帳號用 mc-heads.net 頭像加黑框，其餘預設小人）。
- **技術**：`RoguelikeGame` 是純模擬（`Update(dt, ix, iy)`，不碰 UI）；`RoguelikeView : FrameworkElement` 每幀 `InvalidateVisual`，用 `DrawingContext` 畫；`RoguelikePage` 在 `Loaded` 訂閱 `CompositionTarget.Rendering`、`Unloaded` 取消訂閱（離開頁面遊戲就停），`dt` 上限 0.05 秒；商店與結束畫面用 WPF 控制項疊在上面（按鈕 `Focusable=false`，鍵盤焦點留在頁面）。
- **沒做**：存檔／排行榜、音效、多種武器（目前只有弓，靠商店疊強化）、角色選擇、觸控操作。難度完全沒實際玩過，數值集中在 `RoguelikeGame.cs`（`MakeEnemy`、`RollKind`、`Director`、`BossAct`、`BuildOffers`）。
- 待實測：能否編譯（`Freeze<T>` 泛型輔助、`RoguelikeView` 為 internal 但被 public 頁面的私有欄位使用）；進頁面後方向鍵能直接移動（焦點）；離開再進頁面不會有兩個遊戲迴圈；第 10 波王的彈幕是否太難、1～9 波是否太簡單；商店按鈕按下後畫面更新與材料扣除。


## Update 48 — 插件系統原型（Jint 沙盒；未編譯、未實測）
- **入口**：「更多」頁新增「插件」（格子改 2×4，`Rows = 4`）→ `PluginsPage`：插件清單、「啟用」開關、逐項權限勾選（⚠ 標示 `token.read`／`net.http`）、插件提供的頁面按鈕、最近紀錄（含被拒絕的呼叫）。插件資料夾 `%AppData%\InnovationEasyLauncher\plugins\`，第一次會自動放 `hello-sample`（預設停用）。完整文件：`docs/PLUGINS.md`。
- **新增套件**：`IEML.App.csproj` 加 `Jint 4.*`（BSD-2；純 .NET）。第一次編譯請確認 Jint 4.x 的 API：`new Engine(o => { o.Strict(); o.TimeoutInterval(); o.LimitMemory(); o.LimitRecursion(); o.MaxStatements(); })`、`engine.SetValue`、`engine.Execute`、`engine.Invoke(name, args)`、`JsValue.IsNull()/IsUndefined()`、`Engine.Dispose()`。
- **新檔**：Core：`Plugins/PluginModel.cs`（`Perm` 權限常數與說明、`PluginManifest`＋`Validate`、`PluginState`、`PluginStore`：`plugins.json`、`Discover`，**manifest.id 必須等於資料夾名**）、`Plugins/PluginGate.cs`（★唯一權限閘口★）。App：`PluginRuntime.cs`（每個插件一個 Jint 引擎、`Host`、`Handle`、JS 前置腳本 `Prelude`）、`PluginManager.cs`（載入／啟停／授權／事件／能力實作／範例插件）、`PluginViews.cs`（`PluginPageView` 把 JSON 元件樹畫成控制項；`PluginsPage` 管理頁）。
- **沙盒設計**：引擎沒有開 CLR（`AllowClr` 不呼叫）、沒有檔案／網路／程序。唯一出口 `__host(method, json)`，**每次呼叫都先 `PluginGate.Authorize`**（預設拒絕：未啟用、方法不在表中、權限沒宣告或沒授權都拒絕）；失敗會變成 JS 的 `Error`（`{"__error":…}`）。`delete g.__host` 只是美觀，真正的保護是閘口。權限＝manifest 宣告 ∩ 使用者勾選；插件更新後新增的權限不會自動授權；撤銷立即生效。限制：2 秒逾時、32 MB、遞迴 120、30 萬條陳述式；彈窗標題加「插件「名稱」：」前綴；`net.http` 只允許 HTTPS＋manifest 網域、不跟轉址、≤ 1 MB、10 秒。
- **頁面**：宣告式，插件回傳 JSON 元件樹（heading／text／button／input／checkbox／list／row／column／separator；深度 ≤ 8、≤ 500 個、文字 ≤ 2000 字），不能碰 WPF 物件。按鈕 → `onAction(action, values)` → 重新 `render(values)`。
- **事件**：`launcher.ready`（`MainWindow` Loaded 之後）、`instance.launch`（`MainViewModel.RunLaunch` 開頭，只給 name／mcVersion／loader）。要有 `hooks` 權限才會收到。
- **VM**：新增 `GetSelectedAccountToken()`（背景執行緒取得、最多等 10 秒，離線帳號回傳 null）給 `token.read` 用。⚠ 若剛好需要互動登入，視窗可能要等逾時後才出現。
- **已知限制／待處理**：`net.http`、`token.read` 會阻塞介面執行緒（最多 12／10 秒）；沒防 DNS 重新綁定；沒有插件簽章／市集；`ui.confirm` 等彈窗共用 `Dialogs`（同時只有一個對話框，後開的會取消先開的）；插件資料（`data.json`）存在插件資料夾內，插件資料夾若被別的程式竄改不在防護範圍。
- 待實測：1) 能否編譯（Jint 版本、`JsonNode` 的隱含轉換、`PluginRuntime.Handle` 內 switch 區塊的區域函式）；2) 範例插件：啟用 → 勾權限 → 開頁面 → 輸入名字按「打招呼」→ 提示條 → 「問個問題」彈窗 → 計數持久化；3) 撤銷 `ui.toast` 後再按按鈕 → 紀錄出現「被拒絕」且不會當掉；4) 手寫無窮迴圈的插件 → 2 秒後中止、顯示「執行錯誤」；5) manifest 的 id 與資料夾名不同 → 顯示「無法載入」；6) 啟動實例時範例插件收到事件。


## Update 49 — 修正：啟動時出現「找不到 ShimmerTt 名稱」（GitHub issue #2；未編譯、未實測）
- **症狀**（使用者回報）：啟動器開啟時跳出「發生未預期的錯誤，但啟動器會繼續執行。無法在 System.Windows.Controls.ControlTemplate 的名稱範圍內找到 'ShimmerTt' 名稱」，並寫入 crash.log。
- **原因**：Update 19 的進度條流光（`Theme.xaml` 的 `DarkProgress`）用 `ControlTemplate.Triggers` 裡的 `EventTrigger`（Loaded）啟動 Storyboard，目標寫成範本內有名字的 **Freezable**（`<TranslateTransform x:Name="ShimmerTt">`）。這種寫法在 EventTrigger 下找不到名稱，進度條一載入就丟例外（全域例外處理接住所以沒有直接關閉，但流光動畫不會動）。
- **修正**：目標改成有名字的 **UIElement**（`Shimmer` 這個 Border），用屬性路徑 `(UIElement.RenderTransform).(TranslateTransform.X)` 動畫，並移除 transform 上的 `x:Name`。
- **其他同名寫法**：`Theme.xaml` 的 `ArtButton`／`CardButton`（`Tt`、`Sc`）與 `NavButton`（`NavSc`）也是以名稱指定 Freezable 目標，但它們用的是**屬性觸發器**（`IsMouseOver`／`IsPressed` 的 `EnterActions`／`ExitActions`），機制不同，`Controls.xaml` 裡也有同樣寫法；滑過按鈕若也出現「找不到名稱」，改成同樣的屬性路徑寫法即可（例如 `Root` 的 `(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)`）。
- 待實測：啟動不再跳錯誤；進度條在有進度時流光掃過；滑過按鈕、右欄圖示動畫正常。建議發 1.3.3 修正版並回覆 issue。
