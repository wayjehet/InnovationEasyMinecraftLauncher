using System.Windows;
using IEML.Core.Contracts;
using IEML.Core.Models;
using IEML.Core.Services;
namespace IEML.App;

public record StructItem(string Display, string Key);

public partial class SeedMapPage : UserControl
{
    readonly ISeedMapService _svc = new SeedMapService();
    readonly List<StructureHit> _hits = new();

    public SeedMapPage(InstanceConfig? inst)
    {
        InitializeComponent();
        StructBox.ItemsSource = SeedMapService.StructureTypes.Select(t => new StructItem(t.Display, t.Key)).ToList();
        StructBox.SelectedIndex = 0;
        VersionBox.Text = inst?.McVersion ?? "";
        Loaded += (_, _) =>
        {
            if (!SeedMapService.IsAvailable())
                StatusText.Text = "找不到 ieml_cubiomes.dll，請先依 native\\README.md 編譯並放到啟動器資料夾。";
        };
    }

    bool ReadInputs(out long seed, out int cx, out int cz, out int r)
    {
        seed = 0; cx = cz = r = 0;
        if (string.IsNullOrWhiteSpace(VersionBox.Text)) { StatusText.Text = "請輸入 MC 版本（例如 1.21.1）"; return false; }
        if (!int.TryParse(CxBox.Text, out cx) || !int.TryParse(CzBox.Text, out cz)) { StatusText.Text = "座標必須是整數"; return false; }
        if (!int.TryParse(RadiusBox.Text, out r) || r < 16 || r > 20000) { StatusText.Text = "半徑請輸入 16～20000"; return false; }
        seed = SeedMapService.ParseSeed(SeedBox.Text);
        return true;
    }

    async void Find_Click(object s, RoutedEventArgs e)
    {
        if (!ReadInputs(out var seed, out var cx, out var cz, out var r)) return;
        if (StructBox.SelectedItem is not StructItem sel) return;
        string key = sel.Key, display = sel.Display, mc = VersionBox.Text.Trim();
        StatusText.Text = "搜尋中…";
        try
        {
            // 大範圍可能要算一陣子，放到背景執行緒避免卡住介面
            var hits = await Task.Run(() => _svc.FindStructures(seed, mc, key, cx, cz, r));
            _hits.Clear(); _hits.AddRange(hits);
            HitList.ItemsSource = hits.Select(h =>
            {
                var dist = (int)Math.Sqrt((double)(h.X - cx) * (h.X - cx) + (double)(h.Z - cz) * (h.Z - cz));
                return $"X {h.X,7}   Z {h.Z,7}   距離 {dist} 格";
            }).ToList();
            StatusText.Text = $"找到 {hits.Count} 個「{display}」（種子數值：{seed}）";
        }
        catch (Exception ex) { StatusText.Text = "查詢失敗：" + ex.Message; }
    }

    async void Biome_Click(object s, RoutedEventArgs e)
    {
        if (!ReadInputs(out var seed, out var cx, out var cz, out _)) return;
        var mc = VersionBox.Text.Trim();
        try
        {
            var b = await Task.Run(() => _svc.GetBiome(seed, mc, cx, cz));
            StatusText.Text = $"({cx}, {cz}) 的群系：{b}";
        }
        catch (Exception ex) { StatusText.Text = "查詢失敗：" + ex.Message; }
    }

    void Copy_Click(object s, RoutedEventArgs e)
    {
        var i = HitList.SelectedIndex;
        if (i < 0 || i >= _hits.Count) { StatusText.Text = "請先在清單選一筆"; return; }
        Clipboard.SetText($"{_hits[i].X} {_hits[i].Z}");
        StatusText.Text = $"已複製座標 {_hits[i].X} {_hits[i].Z}";
    }
}
