# IEML UI 實作說明（給接手的 Claude）

設計依據：同資料夾的 `ieml-ui-preview.html`（互動預覽，用瀏覽器開）。
要求：只換外殼，沿用現有 Service／ViewModel 邏輯，不改 `Contracts.cs` 簽名（要加方法請新增新介面或新檔案）。

## 版面
- 視窗 980×640，左 311 : 中 551 : 右 117，單一外框＋兩條分隔線。
- 左欄：標誌、頭像（點開帳號管理）、實例名稱／版本／載入器、帳號下拉、狀態＋進度條、啟動、實例設置、實例選擇。
- 中欄：首頁＝快捷卡片畫布；其他功能＝獨立頁面（UserControl），頁首「← 返回」，再點同一個右欄項目也回首頁。
- 右欄：設置／下載／P2P／更多／音樂，目前頁高亮（左側亮條＋文字變強調色）。

## 頁面與後端對照
| 頁面 | 重點 | 沿用 |
|---|---|---|
| 首頁卡片 | 可拖動、位置存 `QuickLaunchCard.X/Y`；空狀態顯示「＋ 新增卡片」；卡片有「啟動」「設置」 | MainViewModel、LaunchService（accountIdOverride） |
| 卡片設置 | 改實例、帳號；刪除卡片 | InstanceService |
| 設置 | 顏色（強調色／圖標色／外框分隔線色，六色塊＋自訂＋恢復預設，存檔）、帳號、CurseForge 金鑰、素材資料夾、關於 | LauncherSettingsWindow 內容搬入 |
| 下載 → 遊戲 | 版本清單（正式／快照／舊版）、實例名稱（版本＋載入器自動命名）、載入器、**載入器版本下拉（依 MC 版本，第一項標最新）**、摘要「將安裝…」、Fabric/Quilt 勾選自動安裝前置（Fabric API）、安裝進度 | VersionService、LoaderService |
| 下載 → 模組／光影／資源包 | Modrinth／CurseForge 切換、搜尋、「安裝到」實例、**自動安裝前置勾選**、列表項目顯示必要前置與可選前置（可選有勾選框）、「詳情」按鈕 | ModrinthService、CurseForgeService |
| 項目詳情頁 | 簡介、**版本清單（可選過往版本，預設只顯示符合目前 MC 版本，可取消勾選）**、前置（必要／可選）、「安裝此版本」 | 需新增，見下 |
| 下載 → 整合包 | 按鈕「安裝為新實例」；詳情頁同樣可選版本 | ModrinthService.InstallModpackAsync、CurseForgeService |
| P2P | 陶瓦：玩家名、開房、加入、房間碼、狀態；**頁面要保留「陶瓦聯機 Terracotta © burningtnt（AGPL-3.0）」標示** | TerracottaService |
| 更多 | 伺服器查找、遊戲日誌、種子地圖、帳號管理、EasyTier（進階） | 既有視窗可先沿用 |
| 音樂 | 播放／暫停／上下首、音量、選資料夾 | MusicService |

## 需要新增的後端（新檔案，不動 Contracts）
- `GetVersionsAsync(projectId, mcVersion?, loader?)`：回傳版本清單（版本號、類型 release/beta/alpha、支援的 MC 版本、日期、檔案、相依）。Modrinth：`/project/{id}/version`（已回傳完整清單，現行程式只取第一筆）；CurseForge：`/mods/{id}/files`。
- `InstallVersionAsync(instanceId, projectId, versionId, installDeps, optionalDeps[])`：安裝指定版本。
- 相依分類：Modrinth `dependency_type` = required / optional；CurseForge `relationType` 3 = 必要、2 = 可選。目前只裝必要的；「自動安裝前置」關閉時只裝主檔；可選前置只裝使用者勾選的。
- 載入器版本清單：Fabric/Quilt 用官方 meta（`.../versions/loader/{mc}`）、NeoForge 用 Maven 版本清單（1.20.1 為舊命名）、Forge 用 promotions_slim.json。`LoaderService` 已有取最新版本的邏輯，要抽出「列出全部」。
- 顏色設定：把 `AccentBrush`／圖標色／`LineBrush` 改成 `DynamicResource`，存進設定 JSON，啟動時載入。

## 動畫（預覽裡已有，WPF 對照）
- 頁面切換：Opacity 0→1 ＋ TranslateTransform.X 14→0，250 ms，EaseOut。
- 卡片進場：Opacity＋ScaleTransform 0.85→1（BackEase），依序延遲 70 ms；拖動中放大 1.04、輕微旋轉、陰影加深。
- 列表項目：由下往上淡入，依序延遲 40 ms。
- 按鈕：滑過上浮 2 px＋發光；按下縮到 0.96。
- 右欄圖標：滑過／高亮時放大 1.15。
- 啟動中：進度條流光（漸層位移動畫）、啟動鈕脈動發光；完成後停止。
- 提示（toast）：由下滑入淡出。
- 空狀態圖示：上下輕微浮動。
- 尊重系統「減少動畫」設定。

## 驗收清單
1. 每個右欄項目可進入／返回，高亮正確。
2. 卡片可拖動、位置重開後保留、拖動後不會誤觸啟動。
3. 載入器版本隨 MC 版本與載入器變化；快照／舊版只能選原版。
4. 詳情頁可選過往版本，安裝的是選到的版本。
5. 關閉自動安裝前置時，不裝必要前置；可選前置只裝有勾的。
6. 顏色設定重開後仍在，「恢復預設」有效。
7. 陶瓦頁版權標示存在。
