using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IEML.App;

/// 集中的動畫小工具。時間與距離都在這裡改；系統開「減少動畫」時一律跳過。
public static class Motion
{
    public const int PageMs = 250, PageOutMs = 140;
    public const double PageSlide = 14;
    static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// 淡入＋由右滑入（頁面切換）。
    public static void FadeSlideIn(FrameworkElement el, double fromX = PageSlide, int ms = PageMs, int delayMs = 0)
    {
        if (!Enabled) { el.Opacity = 1; return; }
        var begin = TimeSpan.FromMilliseconds(delayMs);
        if (delayMs > 0) el.Opacity = 0;                // 等待期間先藏起來，避免閃一下
        var tt = el.RenderTransform as TranslateTransform;
        if (tt is null || tt.IsFrozen) { tt = new TranslateTransform(); el.RenderTransform = tt; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = begin });
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = begin });
    }

    /// 淡出後執行 done（例如把頁面設成 Collapsed）。
    public static void FadeOut(FrameworkElement el, Action done, int ms = PageOutMs)
    {
        if (!Enabled) { done(); return; }
        var a = new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (_, _) => { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; done(); };
        el.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// 清單依序淡入：每一項延遲 StaggerMs（最多延遲 MaxStaggerMs）。
    public const int StaggerMs = 45, MaxStaggerMs = 360;
    public static void Stagger(System.Windows.Controls.Panel panel)
    {
        int i = 0;
        foreach (FrameworkElement child in panel.Children.OfType<FrameworkElement>())
            FadeSlideIn(child, 10, 220, Math.Min(i++ * StaggerMs, MaxStaggerMs));
    }

    /// 卡片進場：由下往上浮起＋淡入＋從 96% 放大，依序錯開。
    public const double CardRise = 14;
    public static void CardIn(FrameworkElement el, int index)
    {
        if (!Enabled) { el.Opacity = 1; return; }
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform(0.96, 0.96);
        var tt = new TranslateTransform(0, CardRise);
        el.RenderTransform = new TransformGroup { Children = { sc, tt } };
        el.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(Math.Min(index * 60, MaxStaggerMs));
        var dur = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = ease };
        fade.Completed += (_, _) => { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; el.RenderTransform = Transform.Identity; };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(CardRise, 0, dur) { BeginTime = begin, EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, dur) { BeginTime = begin, EasingFunction = ease });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, dur) { BeginTime = begin, EasingFunction = ease });
    }

    /// 空狀態提示上下緩慢浮動（無限循環）。
    public static void Float(FrameworkElement el, double amp = 5, double seconds = 2.2)
    {
        if (!Enabled) return;
        var tt = new TranslateTransform();
        el.RenderTransform = tt;
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-amp, amp, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    /// 提示條：由下滑入、停留 holdMs、淡出後執行 done。重複呼叫會先取消上一次。
    public static void Toast(FrameworkElement el, int holdMs, Action done)
    {
        el.BeginAnimation(UIElement.OpacityProperty, null);
        var tt = el.RenderTransform as TranslateTransform;
        if (tt is null || tt.IsFrozen) { tt = new TranslateTransform(); el.RenderTransform = tt; }
        if (!Enabled) { el.Opacity = 1; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220 + holdMs))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220 + holdMs + 260))));
        fade.Completed += (_, _) => done();
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

/// 進度條數值平滑：把 Value 綁到 local:SmoothProgress.Value，數值增加時 250ms 滑過去；變小（重設）時直接跳。
public static class SmoothProgress
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(SmoothProgress), new PropertyMetadata(0.0, OnChanged));
    public static double GetValue(DependencyObject d) => (double)d.GetValue(ValueProperty);
    public static void SetValue(DependencyObject d, double v) => d.SetValue(ValueProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Primitives.RangeBase bar) return;
        double to = (double)e.NewValue, from = bar.Value;
        if (!SystemParameters.ClientAreaAnimation || to <= from)
        {
            bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            bar.Value = to; return;
        }
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
