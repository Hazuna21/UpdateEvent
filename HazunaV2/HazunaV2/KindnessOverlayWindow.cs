using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace HazunaV2;

// Overlay "Kebaikan Hati Penonton": gift terbaru, top penyumbang sesi ini, dan ucapan terima kasih besar untuk gift besar.
public class KindnessOverlayWindow : Window
{
    private const int MaxRecent = 5;
    private const int MaxTop = 3;
    private const double CardWidth = 380;

    private readonly StackPanel _recent = new();
    private readonly StackPanel _top = new();
    private readonly Border _banner = new();
    private readonly TextBlock _bannerText = new();
    private readonly ScaleTransform _bannerScale = new(1, 1);
    private readonly TextBlock _empty = new();
    private readonly Dictionary<string, (long Diamonds, int Gifts)> _totals = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Threading.DispatcherTimer? _bannerTimer;

    public KindnessOverlayWindow()
    {
        Title = "Kebaikan Penonton Overlay";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        OverlayGuard.Apply(this);
        ShowActivated = false;
        Left = 40;
        Top = 200;

        var title = new TextBlock
        {
            Text = "💖 KEBAIKAN HATI PENONTON",
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Effect = new DropShadowEffect { Color = OverlayTheme.Rgb(0x8B, 0x5C, 0xF6), BlurRadius = 10, ShadowDepth = 0, Opacity = 1 }
        };

        // Ucapan terima kasih besar (muncul sebentar saat ada gift besar)
        _bannerText.Foreground = Brushes.White;
        _bannerText.FontSize = 22;
        _bannerText.FontWeight = FontWeights.Black;
        _bannerText.TextAlignment = TextAlignment.Center;
        _bannerText.TextWrapping = TextWrapping.Wrap;
        _banner.Child = _bannerText;
        _banner.Padding = new Thickness(12, 8, 12, 8);
        _banner.CornerRadius = new CornerRadius(12);
        _banner.Margin = new Thickness(0, 0, 0, 10);
        _banner.Background = new LinearGradientBrush(OverlayTheme.Rgb(0xE8, 0x79, 0xF9), OverlayTheme.Rgb(0x8B, 0x5C, 0xF6), 0);
        _banner.Visibility = Visibility.Collapsed;
        _banner.RenderTransform = _bannerScale;
        _banner.RenderTransformOrigin = new Point(0.5, 0.5);

        _empty.Text = "Menunggu gift pertama...";
        _empty.Foreground = new SolidColorBrush(OverlayTheme.Rgb(0xC4, 0xB5, 0xFD));
        _empty.FontSize = 14;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Margin = new Thickness(0, 6, 0, 6);

        var root = new StackPanel { Width = CardWidth - 36 };
        root.Children.Add(title);
        root.Children.Add(_banner);
        root.Children.Add(Section("TERBARU"));
        root.Children.Add(_recent);
        root.Children.Add(_empty);
        root.Children.Add(Section("TOP SESI INI", 10));
        root.Children.Add(_top);

        var card = new Border
        {
            Padding = new Thickness(18, 14, 18, 14),
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(
                OverlayTheme.Argb(235, 0x1B, 0x10, 0x3A), OverlayTheme.Argb(235, 0x0B, 0x07, 0x1C), 90),
            BorderBrush = new SolidColorBrush(OverlayTheme.Rgb(0x8B, 0x5C, 0xF6)),
            BorderThickness = new Thickness(2),
            Effect = new DropShadowEffect { Color = OverlayTheme.Rgb(0x8B, 0x5C, 0xF6), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 },
            Margin = new Thickness(14),
            Child = root
        };
        Content = card;

        MouseLeftButtonDown += (s, e) => DragMove();
        Closed += (s, e) => _bannerTimer?.Stop();
    }

    private static TextBlock Section(string text, double top = 0) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(OverlayTheme.Rgb(0xA7, 0x8B, 0xFA)),
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, top, 0, 4)
    };

    public void SetChromaKey(bool on)
    {
        Background = on ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : Brushes.Transparent;
    }

    // Mulai dari awal (sesi LIVE baru)
    public void Reset()
    {
        _totals.Clear();
        _recent.Children.Clear();
        _top.Children.Clear();
        _empty.Visibility = Visibility.Visible;
    }

    public void AddGift(string user, string gift, int count, long diamondsEach, bool big)
    {
        user = string.IsNullOrWhiteSpace(user) ? "(tanpa nama)" : user.Trim();
        count = Math.Max(1, count);
        long total = Math.Max(0, diamondsEach) * count;

        var cur = _totals.GetValueOrDefault(user);
        _totals[user] = (cur.Diamonds + total, cur.Gifts + count);

        _empty.Visibility = Visibility.Collapsed;

        // Baris baru di atas, masuk dari kiri
        var row = MakeRow("❤", user, $"{count}x {gift}", big);
        _recent.Children.Insert(0, row);
        while (_recent.Children.Count > MaxRecent) _recent.Children.RemoveAt(_recent.Children.Count - 1);

        var slide = new TranslateTransform(-40, 0);
        row.RenderTransform = slide;
        row.Opacity = 0;
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-40, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        RebuildTop();
        if (big) ShowThanks(user, gift, count);
    }

    private Grid MakeRow(string icon, string name, string detail, bool highlight)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ic = new TextBlock { Text = icon, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var nm = new TextBlock
        {
            Text = name,
            Foreground = highlight ? new SolidColorBrush(OverlayTheme.Rgb(0xFF, 0xD5, 0x4F)) : Brushes.White,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dt = new TextBlock
        {
            Text = detail,
            Foreground = new SolidColorBrush(OverlayTheme.Rgb(0xC4, 0xB5, 0xFD)),
            FontSize = 14,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(ic, 0); Grid.SetColumn(nm, 1); Grid.SetColumn(dt, 2);
        g.Children.Add(ic); g.Children.Add(nm); g.Children.Add(dt);
        return g;
    }

    private void RebuildTop()
    {
        _top.Children.Clear();
        int rank = 1;
        foreach (var t in _totals.OrderByDescending(x => x.Value.Diamonds).ThenByDescending(x => x.Value.Gifts).Take(MaxTop))
        {
            string medal = rank switch { 1 => "🥇", 2 => "🥈", _ => "🥉" };
            _top.Children.Add(MakeRow(medal, t.Key, t.Value.Diamonds > 0 ? $"{t.Value.Diamonds:N0} 💎" : $"{t.Value.Gifts}x", rank == 1));
            rank++;
        }
    }

    private void ShowThanks(string user, string gift, int count) =>
        ShowBanner($"✨ Terima kasih {user}! ✨\n{count}x {gift}");

    // Banner besar sebentar (terima kasih gift besar, alert penonton setia)
    public void ShowBanner(string text)
    {
        _bannerText.Text = text;
        _banner.Visibility = Visibility.Visible;

        var pop = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }
        };
        _bannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _bannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        _banner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));

        _bannerTimer?.Stop();
        _bannerTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _bannerTimer.Tick += (s, e) =>
        {
            _bannerTimer!.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (s2, e2) => _banner.Visibility = Visibility.Collapsed;
            _banner.BeginAnimation(OpacityProperty, fade);
        };
        _bannerTimer.Start();
    }
}
