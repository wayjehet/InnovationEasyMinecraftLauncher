# 自動更新

啟動器啟動時（最多每 12 小時一次）會問 GitHub 這個倉庫的最新**正式版** Release；有新版本時跳出視窗，使用者按「是」才會下載，**不會靜默更新**。也可以在「設置 → 檢查更新」手動檢查，或關掉自動檢查。

## 安全檢查
1. 只從 `wayjehet/InnovationEasyMinecraftLauncher` 的 Release 下載，網域限定 https 的 GitHub（含轉址後的最終網址）。
2. 驗證 zip 的 SHA-256（GitHub API 的 `digest` 或 Release 裡的 `.sha256` 檔），不符就刪檔並中止。
3. （建議啟用）驗證 ECDSA P-256 簽章。簽章用的私鑰只放在 GitHub Secrets，公鑰寫在 `UpdateService.PublicKeyBase64`。**這能擋住「GitHub 帳號或 Release 被竄改」的情況**，只有 SHA-256 做不到（雜湊和檔案放在同一個地方）。
4. 解壓時檢查路徑（防 Zip Slip），並確認有 `InnovationEasyMinecraftLauncher.exe`。
5. 換檔前把舊 exe 改名為 `.old`，複製失敗會自動還原；結果記在 `%LocalAppData%\InnovationEasyLauncher\update\<版本>\apply-update.log`。

## 啟用簽章（一次性）
在自己的電腦用 PowerShell 7 執行：

```powershell
$k = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::NamedCurves.nistP256)
$k.ExportPkcs8PrivateKeyPem()                          # ← 整段貼到 GitHub：Settings → Secrets → Actions → UPDATE_SIGNING_KEY
[Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo())   # ← 貼到 UpdateService.PublicKeyBase64
```

私鑰不要放進倉庫、不要貼給任何人（包括 AI）。之後每次推 `v*` 標籤，workflow 會產生 `.sha256` 與 `.sig` 一起放進 Release。
**啟用後**，沒有簽章的舊 Release 會被拒絕更新；公鑰一旦發佈就不能換（換了舊版本就無法更新），請妥善備份私鑰。

## 版本號命名
更新程式只看開頭的數字：`v1.2.1EF`、`v1.2.1-beta` 都讀成 `1.2.1`，後面的字母會被忽略。
- Release 的 tag 要跟 csproj 一致：`<Version>1.2.1</Version>`（數字）＋ `<InformationalVersion>1.2.1EF</InformationalVersion>`（畫面上顯示的完整名稱）。
- 因為字母被忽略，`1.2.1EF` 與 `1.2.1EG` 會被當成同一版，**不會互相提示更新**。要讓舊版收到更新，數字一定要變大（例如 `1.2.2`）。

## 限制
- 沒有程式碼簽署憑證時，Windows SmartScreen 可能仍會對新下載的 exe 顯示警告；這跟更新機制無關。
- 如果安裝資料夾不可寫（例如放在 `Program Files`），換檔會失敗並還原，請把啟動器放在自己的資料夾。
- 只更新啟動器本身；使用者資料在 `%AppData%`，不會被動到。
