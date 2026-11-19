# Innovation Easy Minecraft Launcher（IEML）

一個以 .NET 8 + WPF 撰寫的 Minecraft Java 版啟動器，以 Windows 為主要目標平台。

> ⚠️ **Beta / 開發中**
> 專案目前處於早期開發階段，功能陸續加入中，尚未經過完整測試，可能有錯誤。歡迎回報問題。

## 功能

- **實例管理**：多個獨立實例，各自的版本、載入器、Java、記憶體與 JVM 參數
- **帳號**：離線帳號與 Microsoft 正版登入，帳號可綁定實例；主畫面快速啟動卡片可用不同帳號啟動同一實例
- **載入器**：Fabric、Quilt、Forge、NeoForge（含 1.20.1 舊命名）
- **Java 管理**：自動偵測本機 Java，找不到合適版本時從 Adoptium 下載
- **內容安裝**：
  - Modrinth：模組、光影、資源包、整合包（`.mrpack`）
  - CurseForge：模組、光影、資源包、整合包（需自備 API 金鑰）
  - 本機匯入整合包（CurseForge `.zip` / Modrinth `.mrpack`）
  - 模組自動安裝必要相依；下載後檢查 SHA-1
- **伺服器**：伺服器查找（Server List Ping、SRV 解析）、收藏、公開伺服器搜尋
- **連線房間**：透過 [EasyTier](https://github.com/EasyTier/EasyTier) 建立虛擬區域網路，與朋友連線
- **種子地圖**：以 [Cubiomes](https://github.com/Cubitect/cubiomes) 查詢結構與群系（需自行編譯原生 DLL，見 `native/README.md`）
- **遊戲日誌**：即時顯示遊戲輸出，並自動遮蔽存取權杖
- **音樂播放**：主畫面內建簡易音樂播放器
- **共用快取**：不同實例共用 libraries / assets，節省空間與下載時間

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
HANDOFF.md     開發進度與待實測清單
```

技術：.NET 8、WPF、CommunityToolkit.Mvvm、CmlLib.Core 4.x、NAudio。

## 已知限制

- 專案仍在早期階段，部分功能尚未實測（詳見 `HANDOFF.md` 的待實測清單）
- 介面仍使用 WPF 預設樣式，之後會美化
- 目前僅支援 Windows

## 回報問題

請到本倉庫的 Issues 回報，並附上：

- 啟動器版本與 Windows 版本
- 使用的 Minecraft 版本與載入器
- 「遊戲日誌」視窗的內容（權杖已自動遮蔽，貼出前仍請再檢查一次）

## 授權

本專案以 [MIT 授權](LICENSE) 釋出。

## 致謝

CmlLib.Core、Fabric、Quilt、Forge、NeoForge、Modrinth、CurseForge、EasyTier、Cubiomes、Adoptium 等專案與服務。

本專案與 Mojang Studios、Microsoft 無關，亦非其官方產品。Minecraft 是 Mojang Studios 的商標。
