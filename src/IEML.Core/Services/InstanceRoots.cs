using System.Text.Json;
namespace IEML.Core.Services;

/// 一個「實例目錄」：裡面每個子資料夾（GUID 命名）就是一個實例。
public sealed class InstanceRoot
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public override string ToString() => $"{Name}（{Path}）";
}

/// 實例目錄清單（可添加多個）。預設目錄永遠存在（%AppData%\InnovationEasyLauncher\instances），不能移除。
/// 設定存在 %AppData%\InnovationEasyLauncher\roots.json。
/// - 列出實例：掃描所有目錄。
/// - 找某個實例的資料夾：依序在每個目錄裡找 &lt;guid:N&gt;；找不到就當作會建立在「目前使用的目錄」(Active)。
/// - 移除目錄只是不再掃描，不會刪除任何檔案。
public static class InstanceRoots
{
    sealed class Store
    {
        public List<InstanceRoot> Extra { get; set; } = new();
        public string? Active { get; set; }
    }

    static readonly object Gate = new();
    static Store? _store;
    static string FilePath => System.IO.Path.Combine(AppPaths.Root, "roots.json");

    public static event Action? Changed;

    static Store Load()
    {
        lock (Gate)
        {
            if (_store is not null) return _store;
            try { if (File.Exists(FilePath)) _store = JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)); }
            catch { }
            _store ??= new Store();
            _store.Extra ??= new();
            return _store;
        }
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    static string Norm(string p)
    {
        try { return System.IO.Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return p.Trim(); }
    }

    static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    public static InstanceRoot Default => new() { Name = "預設", Path = AppPaths.Instances };

    public static IReadOnlyList<InstanceRoot> All
    {
        get
        {
            var s = Load();
            var list = new List<InstanceRoot> { Default };
            lock (Gate) list.AddRange(s.Extra.Select(r => new InstanceRoot { Name = r.Name, Path = r.Path }));
            return list;
        }
    }

    /// 新實例會建立在這個目錄。
    public static InstanceRoot Active
    {
        get
        {
            var s = Load();
            var act = s.Active;
            if (!string.IsNullOrEmpty(act))
            {
                var hit = All.FirstOrDefault(r => Same(r.Path, act));
                if (hit is not null) return hit;
            }
            return Default;
        }
    }

    public static bool IsDefault(InstanceRoot r) => Same(r.Path, AppPaths.Instances);

    /// 添加一個目錄（不存在會嘗試建立）。名稱留空＝用資料夾名。
    public static InstanceRoot Add(string path, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路徑是空的");
        var full = Norm(path);
        if (All.Any(r => Same(r.Path, full))) throw new InvalidOperationException("這個目錄已經在清單裡了");
        Directory.CreateDirectory(full);
        var root = new InstanceRoot
        {
            Name = string.IsNullOrWhiteSpace(name) ? (System.IO.Path.GetFileName(full) is { Length: > 0 } n ? n : full) : name.Trim(),
            Path = full
        };
        var s = Load();
        lock (Gate) { s.Extra.Add(root); Save(); }
        Changed?.Invoke();
        return root;
    }

    /// 從清單移除（不刪檔案；該目錄裡的實例不再顯示）。預設目錄不能移除。
    public static void Remove(string path)
    {
        if (Same(path, AppPaths.Instances)) throw new InvalidOperationException("預設目錄不能移除");
        var s = Load();
        lock (Gate)
        {
            s.Extra.RemoveAll(r => Same(r.Path, path));
            if (s.Active is not null && Same(s.Active, path)) s.Active = null;
            Save();
        }
        Changed?.Invoke();
    }

    public static void SetActive(string path)
    {
        var s = Load();
        lock (Gate) { s.Active = Same(path, AppPaths.Instances) ? null : Norm(path); Save(); }
        Changed?.Invoke();
    }

    /// 某個實例的資料夾：已存在就回傳實際位置；不存在（新實例）就回傳「目前使用目錄」底下的位置。
    public static string DirFor(Guid id)
    {
        var n = id.ToString("N");
        foreach (var r in All)
        {
            var p = System.IO.Path.Combine(r.Path, n);
            if (Directory.Exists(p)) return p;
        }
        return System.IO.Path.Combine(Active.Path, n);
    }
}
