using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IEML.Core.Services;
namespace IEML.App;

public enum SkinFit { Fill, Cover, Contain }

/// 一個可以換圖的部位。Key 就是檔名（不含 .png）；States=true 時另外支援 Key_hover／Key_pressed／Key_disabled。
public sealed record SkinSlot(string Key, string Group, string Title, string Hint, bool States = false, bool Baked = false);

/// 每個部位的拉伸設定（存在 skin.json）：Slice＝九宮格邊距（來源圖的像素，0＝整張拉伸）、Pixel＝像素風（不平滑）。
public sealed class SkinOptions
{
    public int Slice { get; set; }
    public bool Pixel { get; set; }
}

/// 素材管理：圖片放在 %AppData%\InnovationEasyLauncher\Assets\（優先）或 exe 旁的 Assets\，檔名＝部位名.png。
/// 監看資料夾，檔案一變動所有畫面立刻更新（不用重開）。
public sealed class SkinManager
{
    public static SkinManager Instance { get; } = new();

    public static readonly (string Suffix, string Title)[] StateInfo =
    {
        ("_hover", "滑過"), ("_pressed", "按下"), ("_disabled", "停用")
    };

    public static readonly SkinSlot[] Slots =
    {
        new("background", "背景與標誌", "視窗背景", "建議 1960×1280，撐滿並裁掉多出的邊"),
        new("logo", "背景與標誌", "左上角標誌", "高約 44，等比縮放"),
        new("avatar_frame", "背景與標誌", "頭像外框", "約 64×64，疊在頭像上，中間要透明"),

        new("panel_main", "面板", "主畫面外框", "三欄底板（980×640），建議用九宮格"),
        new("card", "面板", "快捷啟動卡片", "約 210×104"),
        new("modal", "面板", "對話框", "確認、新增實例等小視窗的底板，建議九宮格"),
        new("menu", "面板", "下拉／右鍵選單", "下拉清單與選單的底板"),

        new("btn_primary", "按鈕", "一般按鈕（全程式通用）", "文字由程式畫，圖上不要有字；可九宮格", States: true),
        new("btn_ghost", "按鈕", "描邊按鈕（全程式通用）", "文字由程式畫，圖上不要有字；可九宮格", States: true),
        new("btn_launch", "按鈕", "啟動鍵", "約 253×83，圖上要自己畫字", States: true, Baked: true),
        new("btn_instance_settings", "按鈕", "實例設置", "約 126×93，圖上要自己畫字", States: true, Baked: true),
        new("btn_instance_pick", "按鈕", "實例選擇", "約 126×93，圖上要自己畫字", States: true, Baked: true),
        new("btn_settings", "按鈕", "右欄「設置」", "圖上要自己畫字與圖示", States: true, Baked: true),
        new("btn_download", "按鈕", "右欄「下載」", "圖上要自己畫字與圖示", States: true, Baked: true),
        new("btn_p2p", "按鈕", "右欄「P2P」", "圖上要自己畫字與圖示", States: true, Baked: true),
        new("btn_more", "按鈕", "右欄「更多」", "圖上要自己畫字與圖示", States: true, Baked: true),
        new("btn_music", "按鈕", "右欄「音樂」", "圖上要自己畫字與圖示", States: true, Baked: true),

        new("input", "輸入與控制項", "輸入框", "文字框、密碼框的底，可九宮格"),
        new("dropdown", "輸入與控制項", "下拉選單（收合時）", "可九宮格"),
        new("tab", "輸入與控制項", "分頁（未選）", "可九宮格"),
        new("tab_selected", "輸入與控制項", "分頁（選中）", "可九宮格"),
        new("progress_track", "輸入與控制項", "進度條軌道", "可九宮格"),
        new("progress_fill", "輸入與控制項", "進度條填充", "有放圖時不顯示光澤動畫；可九宮格"),
    };

    const long MaxFile = 20L * 1024 * 1024;
    static string Dir => ArtAssets.UserDir;
    static string OptionsPath => Path.Combine(Dir, "skin.json");

    readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, SkinOptions> _opts = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? _watcher;
    DispatcherTimer? _debounce;
    DateTime _muteUntil;

    /// 圖片或設定變動時觸發（一定在 UI 執行緒）。
    public event Action? Changed;

    // ---------- 名稱 ----------
    public static string Normalize(string key)
    {
        key = key.Trim();
        return key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? key[..^4] : key;
    }

    public static string BaseKey(string key)
    {
        key = Normalize(key);
        foreach (var (suffix, _) in StateInfo)
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return key[..^suffix.Length];
        return key;
    }

    public static bool IsKnown(string key)
    {
        key = Normalize(key);
        var b = BaseKey(key);
        var slot = Slots.FirstOrDefault(s => s.Key.Equals(b, StringComparison.OrdinalIgnoreCase));
        if (slot is null) return false;
        return b.Equals(key, StringComparison.OrdinalIgnoreCase) || slot.States;
    }

    // ---------- 讀取 ----------
    public BitmapSource? Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = Normalize(key);
        if (_cache.TryGetValue(key, out var v)) return v;
        var bi = ArtAssets.Load(key + ".png");
        _cache[key] = bi;
        return bi;
    }

    public bool HasUserFile(string key) => File.Exists(Path.Combine(Dir, Normalize(key) + ".png"));

    public SkinOptions Options(string key) =>
        _opts.TryGetValue(BaseKey(key), out var o) ? o : new SkinOptions();

    // ---------- 變更 ----------
    public void Start()
    {
        LoadOptions();
        try
        {
            Directory.CreateDirectory(Dir);
            _watcher = new FileSystemWatcher(Dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler h = (_, _) => Kick();
            _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
            _watcher.Renamed += (_, _) => Kick();
        }
        catch { /* 監看失敗就退回「重開才生效」 */ }
    }

    void Kick()
    {
        if (DateTime.UtcNow < _muteUntil) return;
        var d = Application.Current?.Dispatcher;
        d?.BeginInvoke(() =>
        {
            _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _debounce.Tick -= OnDebounce;
            _debounce.Tick += OnDebounce;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    void OnDebounce(object? s, EventArgs e) { _debounce?.Stop(); Reload(); }

    void Mute() => _muteUntil = DateTime.UtcNow.AddMilliseconds(900);

    public void Reload()
    {
        _cache.Clear();
        LoadOptions();
        Changed?.Invoke();
    }

    void LoadOptions()
    {
        try
        {
            var d = File.Exists(OptionsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, SkinOptions>>(File.ReadAllText(OptionsPath))
                : null;
            _opts = new Dictionary<string, SkinOptions>(d ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { _opts = new Dictionary<string, SkinOptions>(StringComparer.OrdinalIgnoreCase); }
    }

    public void SetOptions(string key, int slice, bool pixel)
    {
        var b = BaseKey(key);
        if (slice <= 0 && !pixel) _opts.Remove(b);
        else _opts[b] = new SkinOptions { Slice = Math.Clamp(slice, 0, 2048), Pixel = pixel };
        Directory.CreateDirectory(Dir);
        File.WriteAllText(OptionsPath, JsonSerializer.Serialize(_opts, new JsonSerializerOptions { WriteIndented = true }));
        Mute();
        Reload();
    }

    /// 把任一格式的圖（png／jpg／bmp／gif 第一格）轉成 PNG 放進素材資料夾。
    public void Import(string key, string sourcePath)
    {
        key = Normalize(key);
        if (!IsKnown(key)) throw new ArgumentException("不認得的素材名稱：" + key);
        var fi = new FileInfo(sourcePath);
        if (!fi.Exists) throw new FileNotFoundException("找不到檔案", sourcePath);
        if (fi.Length > MaxFile) throw new InvalidDataException("圖片超過 20 MB。");

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(sourcePath);
        bi.EndInit();
        bi.Freeze();
        if (bi.PixelWidth > 8192 || bi.PixelHeight > 8192) throw new InvalidDataException("圖片尺寸超過 8192 像素。");

        Directory.CreateDirectory(Dir);
        var dest = Path.Combine(Dir, key + ".png");
        var tmp = dest + ".part";
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bi));
        try
        {
            using (var fs = File.Create(tmp)) enc.Save(fs);
            File.Move(tmp, dest, true);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
        Mute();
        Reload();
    }

    public void Clear(string key)
    {
        var p = Path.Combine(Dir, Normalize(key) + ".png");
        if (File.Exists(p)) File.Delete(p);
        Mute();
        Reload();
    }

    public void ClearAll()
    {
        if (Directory.Exists(Dir))
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*.png"))
                if (IsKnown(Path.GetFileNameWithoutExtension(f))) File.Delete(f);
            if (File.Exists(OptionsPath)) File.Delete(OptionsPath);
        }
        Mute();
        Reload();
    }

    // ---------- 素材包（zip）：方便分享 ----------
    public int ExportPack(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        int n = 0;
        using var z = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        if (Directory.Exists(Dir))
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*.png"))
            {
                var k = Path.GetFileNameWithoutExtension(f);
                if (!IsKnown(k)) continue;
                z.CreateEntryFromFile(f, k + ".png"); n++;
            }
            if (File.Exists(OptionsPath)) z.CreateEntryFromFile(OptionsPath, "skin.json");
        }
        return n;
    }

    /// 只接受「根目錄、檔名是已知部位的 png」與 skin.json；其他一律略過（也就不可能有路徑穿越）。
    public int ImportPack(string zipPath)
    {
        int n = 0;
        long total = 0;
        Directory.CreateDirectory(Dir);
        using var z = ZipFile.OpenRead(zipPath);
        foreach (var e in z.Entries)
        {
            if (e.FullName != e.Name || e.Length == 0 || e.Length > MaxFile) continue;
            bool json = e.Name.Equals("skin.json", StringComparison.OrdinalIgnoreCase);
            bool png = e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                       && IsKnown(Path.GetFileNameWithoutExtension(e.Name));
            if (!json && !png) continue;
            total += e.Length;
            if (total > 200L * 1024 * 1024) throw new InvalidDataException("素材包太大。");
            var dest = Path.Combine(Dir, json ? "skin.json" : e.Name);
            var tmp = dest + ".part";
            try { e.ExtractToFile(tmp, true); File.Move(tmp, dest, true); }
            catch { try { File.Delete(tmp); } catch { } throw; }
            n++;
        }
        Mute();
        Reload();
        return n;
    }
}

/// 已解析好的圖（含九宮格切片），負責實際繪製。
internal sealed class SkinSurface
{
    readonly BitmapSource _img;
    readonly SkinFit _fit;
    readonly int _slice;
    readonly BitmapSource[]? _parts;
    public bool Pixel { get; }

    SkinSurface(BitmapSource img, SkinFit fit, int slice, bool pixel)
    {
        _img = img; _fit = fit; Pixel = pixel;
        int pw = img.PixelWidth, ph = img.PixelHeight;
        int s = Math.Min(slice, (Math.Min(pw, ph) - 1) / 2);
        if (s > 0 && fit == SkinFit.Fill)
        {
            _slice = s;
            int[] xs = { 0, s, pw - s, pw }, ys = { 0, s, ph - s, ph };
            _parts = new BitmapSource[9];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    var cb = new CroppedBitmap(img, new Int32Rect(xs[c], ys[r], xs[c + 1] - xs[c], ys[r + 1] - ys[r]));
                    cb.Freeze();
                    _parts[r * 3 + c] = cb;
                }
        }
    }

    /// state 圖不存在時退回原圖（沒有「滑過」圖就一直用一般圖）。
    public static SkinSurface? Resolve(string? slot, string? state, SkinFit fit)
    {
        if (string.IsNullOrWhiteSpace(slot)) return null;
        slot = SkinManager.Normalize(slot);
        var sm = SkinManager.Instance;
        var img = string.IsNullOrEmpty(state) ? null : sm.Get(slot + state);
        img ??= sm.Get(slot);
        if (img is null) return null;
        var o = sm.Options(slot);
        return new SkinSurface(img, fit, o.Slice, o.Pixel);
    }

    public void Draw(DrawingContext dc, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        if (_parts is not null)
        {
            double k = _img.PixelWidth > 0 ? _img.Width / _img.PixelWidth : 1;   // 每個像素佔幾個 DIP
            double d = Math.Min(_slice * k, Math.Min(size.Width, size.Height) / 2);
            double[] dx = { 0, d, size.Width - d, size.Width }, dy = { 0, d, size.Height - d, size.Height };
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    double w = dx[c + 1] - dx[c], h = dy[r + 1] - dy[r];
                    if (w > 0 && h > 0) dc.DrawImage(_parts[r * 3 + c], new Rect(dx[c], dy[r], w, h));
                }
            return;
        }
        double iw = _img.Width, ih = _img.Height;
        switch (_fit)
        {
            case SkinFit.Cover:
            {
                double sc = Math.Max(size.Width / iw, size.Height / ih), w = iw * sc, h = ih * sc;
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, size.Width, size.Height)));
                dc.DrawImage(_img, new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h));
                dc.Pop();
                break;
            }
            case SkinFit.Contain:
            {
                double sc = Math.Min(size.Width / iw, size.Height / ih), w = iw * sc, h = ih * sc;
                dc.DrawImage(_img, new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h));
                break;
            }
            default:
                dc.DrawImage(_img, new Rect(0, 0, size.Width, size.Height));
                break;
        }
    }
}

/// 可以直接取代 Border：有 Slot 對應的圖就畫圖（取代底色與邊框），沒有就畫原本的 Border 樣子。
/// State 可由模板觸發器設成 _hover／_pressed／_disabled。HasImage 可給模板判斷要不要隱藏程式畫的文字。
public class SkinBorder : Border
{
    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(
        nameof(Slot), typeof(string), typeof(SkinBorder), new PropertyMetadata(null, OnSkinChanged));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(string), typeof(SkinBorder), new PropertyMetadata("", OnSkinChanged));
    public static readonly DependencyProperty FitProperty = DependencyProperty.Register(
        nameof(Fit), typeof(SkinFit), typeof(SkinBorder), new PropertyMetadata(SkinFit.Fill, OnSkinChanged));
    static readonly DependencyPropertyKey HasImagePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasImage), typeof(bool), typeof(SkinBorder), new PropertyMetadata(false));
    public static readonly DependencyProperty HasImageProperty = HasImagePropertyKey.DependencyProperty;

    public string? Slot { get => (string?)GetValue(SlotProperty); set => SetValue(SlotProperty, value); }
    public string? State { get => (string?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public SkinFit Fit { get => (SkinFit)GetValue(FitProperty); set => SetValue(FitProperty, value); }
    public bool HasImage => (bool)GetValue(HasImageProperty);

    SkinSurface? _surface;

    public SkinBorder()
    {
        Loaded += (_, _) =>
        {
            SkinManager.Instance.Changed -= Refresh;
            SkinManager.Instance.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => SkinManager.Instance.Changed -= Refresh;
    }

    static void OnSkinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SkinBorder)d).Refresh();

    void Refresh()
    {
        _surface = SkinSurface.Resolve(Slot, State, Fit);
        SetValue(HasImagePropertyKey, _surface is not null);
        RenderOptions.SetBitmapScalingMode(this, _surface?.Pixel == true ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Unspecified);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_surface is not null) _surface.Draw(dc, new Size(ActualWidth, ActualHeight));
        else base.OnRender(dc);
    }
}

/// 模板用：這顆按鈕是用 ArtProps.File 指定「圖上已畫好文字」的圖，而且圖存在 → 隱藏程式畫的文字／裝飾。
public sealed class HideWhenBakedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 1 && values[0] is string s && !string.IsNullOrEmpty(s) && values[1] is true
            ? Visibility.Collapsed : Visibility.Visible;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
