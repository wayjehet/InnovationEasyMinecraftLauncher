# DownloadManager 整合說明（給接手的 Claude）

新增檔案（沒動 `Contracts.cs`）：`src/IEML.Core/Services/Download/` 下的 `DownloadTask.cs`、`DownloadEngine.cs`、`DownloadManager.cs`。
**沒有編譯過**（網頁版環境沒有 .NET），請先 `dotnet build` 修錯。

## 行為
- 全域單例 `DownloadManager.Instance`；所有任務共用 `MaxParallel`（預設 4，1–16）。
- 下載到 `<dest>.part` → 驗證 SHA-1（有給才驗）→ 改名成 dest。中斷時 `.part` 保留。
- **暫停** = 中止連線但保留 `.part`；**繼續** = 用 HTTP Range 從 `.part` 接著下載（伺服器不支援 Range 就從頭覆蓋）。
- **取消** = 刪除 `.part`，`Completion` 變成 canceled。
- **重試**（預設 3 次，`SetLimitsAsync` 可改）：429 依 Retry-After（最多等 60 秒，沒給就 5 秒）；5xx、網路錯誤、下載不完整、30 秒沒資料、SHA-1 不符 → 指數退避 2/4/8 秒；403、404 等 4xx 不重試，直接失敗並顯示原因。
- 暫停中的任務，`await task.Completion` 會一直等到繼續為止（例如整合包安裝會跟著暫停）。
- `Retry(task)` 會建立一個新任務（舊任務的 `Completion` 已經結束，無法復活）。
- 設定存在 `%AppData%\InnovationEasyLauncher\download.json`。

## 要改的呼叫端（把各服務自己的下載換成 DownloadManager）
所有服務只負責提供：網址、目的路徑、標題、來源名稱、SHA-1。

1. `ModrinthService.DownloadAsync(url, dest, sha1, ct)` 整個換成：
   ```csharp
   static Task DownloadAsync(string url, string dest, string? sha1, CancellationToken ct) =>
       DownloadManager.Instance.DownloadAsync(url, dest, Path.GetFileName(dest), "Modrinth", sha1, ct: ct);
   ```
   `using IEML.Core.Services.Download;`
2. 整合包安裝（Modrinth `.mrpack`、CurseForge）裡的 `SemaphoreSlim gate` + `Task.Run` **拿掉**，直接 `tasks.Add(DownloadAsync(...))` 再 `await Task.WhenAll(tasks)`；並行數交給全域上限。傳入 `group: 整合包名稱`，下載中心就能分組顯示。
3. `CurseForgeService` 的下載（它有自己的 `.part` 寫法）同樣換成 `DownloadManager.Instance.DownloadAsync(..., "CurseForge", sha1, group, ct)`。
4. `JavaService`（Adoptium zip）、`LoaderService.RunInstaller` 下載安裝程式 jar、`TerracottaService`／`EasyTierService` 下載執行檔：換成 `DownloadManager.Instance.DownloadAsync(...)`，來源名稱分別寫 "Adoptium"、"NeoForge"/"Forge"、"Terracotta"、"EasyTier"。有 digest 就傳 sha（SHA-256 的不要傳進 sha1 參數，沿用原本的驗證）。
5. **不能走 DownloadManager 的**：CmlLib 下載原版 libraries／assets（它自己下載）、Forge/NeoForge 安裝程式 jar 執行時自己抓的函式庫。這兩塊下載中心只能顯示「安裝中」文字，沒辦法暫停或限速。

## 下載中心頁面（UI 要做的）
- 資料來源：`Snapshot()` 取得清單；`TaskAdded` 加項目；每個 `DownloadTask` 的 `PropertyChanged` 更新進度；`TaskChanged` 更新狀態；`TasksCleared` 重新載入。事件都在背景執行緒，更新集合前要切回 UI 執行緒。
- 顯示：標題、來源、群組、狀態、進度條（`Percent`）、`BytesDone / BytesTotal`、`SpeedBps`、`Eta`、`Error`（含重試次數）。
- 按鈕：每個任務「暫停／繼續／取消」（失敗或取消的「重新下載」）；頁首「全部暫停／全部繼續／清除已完成」；總速度用 `TotalSpeedBps`。
- 設置頁「下載設定」：最大並行數、失敗重試次數 → `SetLimitsAsync`。

## 測試清單
1. 下載一個大檔，下載中按暫停 → 狀態變暫停、速度歸零、`.part` 還在；按繼續 → 從原位置接著下載（不是從 0%）。
2. 暫停整合包安裝 → 所有進行中的檔案都停；繼續後整合包完成。
3. 下載到一半關網路 → 顯示「重試 1/3」，網路恢復後自動接上；超過次數才顯示失敗。
4. 故意給錯誤 SHA-1 → 刪除 `.part`、重下、最後失敗並顯示「檢查碼不符」。
5. 下載中按取消 → `.part` 被刪除，呼叫端收到 `OperationCanceledException`。
6. 並行數設成 2 → 同時只有 2 個任務在跑，其餘排隊。
