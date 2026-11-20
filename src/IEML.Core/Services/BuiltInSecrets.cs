namespace IEML.Core.Services;

/// 打包時內建的金鑰。倉庫裡一律保持空字串，絕對不要把真的金鑰提交進 git。
/// 官方發布版由 GitHub Actions（.github/workflows/build.yml）在編譯前，把 Secret「CURSEFORGE_API_KEY」
/// 取代到下面這一行；自己編譯的版本沒有內建金鑰，使用者要在設定頁填自己的。
/// 注意：內建的金鑰仍可能被人從 exe 裡解出來，所以請用「專案專用金鑰」，並準備好可以隨時更換。
public static class BuiltInSecrets
{
    public const string CurseForgeKey = "";
}
