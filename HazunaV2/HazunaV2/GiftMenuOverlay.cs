using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HazunaV2;

// Overlay "menu gift": kartu berisi judul (gift / tap-tap / follow / share), gambar, dan nama efek.
// Maksimal 15 kartu per halaman (5 kolom x 3 baris). Kalau lebih, halaman bergantian tiap 30 detik.
// Animasi: kartu muncul bergantian, ikon melayang, kilau menyapu kartu, halaman bergeser halus,
// dan kartu "menyala" saat aturannya terpicu.
public class GiftMenuOverlay : Window
{
    private const int PerPage = 15;
    private const int Columns = 5;
    private const double CardW = 190, CardH = 240, Gap = 6, Radius = 18;
    private static readonly TimeSpan PageInterval = TimeSpan.FromSeconds(30);

    // Tema ungu gelap: kartu gelap, garis ungu neon, tulisan putih tebal dengan kontur gelap
    private static readonly Brush Neon = new SolidColorBrush(Color.FromRgb(0xA7, 0x6B, 0xFF));
    private static readonly Brush TextWhite = new SolidColorBrush(Colors.White);
    private static readonly Brush TextLilac = new SolidColorBrush(Color.FromRgb(0xF0, 0xC8, 0xFF));
    private static readonly FontFamily CardFont = new("Segoe UI Black, Segoe UI, Arial");
    private static readonly Dictionary<string, ImageSource> ImageCache = new();

    // Pasangan warna atas -> bawah tiap kartu (berganti-ganti)
    private static readonly (Color Top, Color Bottom)[] Palettes =
    {
        (Color.FromRgb(0x3E, 0x1C, 0x72), Color.FromRgb(0x16, 0x0A, 0x2E)),
        (Color.FromRgb(0x4B, 0x20, 0x84), Color.FromRgb(0x1D, 0x0B, 0x3A)),
        (Color.FromRgb(0x33, 0x16, 0x62), Color.FromRgb(0x12, 0x08, 0x26)),
        (Color.FromRgb(0x56, 0x26, 0x96), Color.FromRgb(0x20, 0x0D, 0x40)),
    };

    private readonly ObservableCollection<GiftRule> _rules;
    private readonly Border _strip = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _progressBack = new();
    private readonly Border _progressFill = new();
    private readonly ScaleTransform _progressScale = new(0, 1);
    private readonly DispatcherTimer _pageTimer = new() { Interval = PageInterval };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Dictionary<GiftRule, Border> _cards = new();

    private int _page;
    private bool _transitioning;

    public GiftMenuOverlay(ObservableCollection<GiftRule> rules)
    {
        _rules = rules;

        Title = "Menu Gift Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = 40;
        Top = 40;

        _strip.RenderTransform = new TranslateTransform();

        // Bilah waktu menuju halaman berikutnya
        _progressBack.Height = 5;
        _progressBack.CornerRadius = new CornerRadius(3);
        _progressBack.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        _progressBack.Margin = new Thickness(Gap, 10, Gap, 0);
        _progressBack.ClipToBounds = true;
        _progressFill.CornerRadius = new CornerRadius(3);
        _progressFill.Background = new LinearGradientBrush(
            Color.FromRgb(0xE8, 0x79, 0xF9), Color.FromRgb(0x8B, 0x5C, 0xF6), 0);
        _progressFill.RenderTransformOrigin = new Point(0, 0.5);
        _progressFill.RenderTransform = _progressScale;
        _progressBack.Child = _progressFill;

        _dots.Margin = new Thickness(0, 8, 0, 0);

        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(_strip);
        stack.Children.Add(_progressBack);
        stack.Children.Add(_dots);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = stack };

        // Geser jendela dengan menahan klik kiri
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is not ResizeGrip) DragMove();
        };

        // Ikuti perubahan aturan (tambah/hapus/ubah) dengan jeda singkat
        _rules.CollectionChanged += OnRulesChanged;
        foreach (var r in _rules) r.PropertyChanged += OnRuleProperty;

        _refreshTimer.Tick += (s, e) => { _refreshTimer.Stop(); ShowPage(false); };
        _pageTimer.Tick += (s, e) => NextPage();

        Closed += (s, e) =>
        {
            _pageTimer.Stop();
            _refreshTimer.Stop();
            _rules.CollectionChanged -= OnRulesChanged;
            foreach (var r in _rules) r.PropertyChanged -= OnRuleProperty;
        };

        var rows = Math.Max(1, (int)Math.Ceiling(Math.Min(Visible().Count, PerPage) / (double)Columns));
        Width = Columns * (CardW + Gap * 2) * 0.8 + 40;
        Height = rows * (CardH + Gap * 2) * 0.8 + 90;

        Loaded += (s, e) =>
        {
            BuildPage(entrance: true);
            _pageTimer.Start();
        };
    }

    public void SetChromaKey(bool on)
    {
        Background = on ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : Brushes.Transparent;
    }

    // ---------- Perubahan aturan ----------

    private void OnRulesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (GiftRule r in e.OldItems) r.PropertyChanged -= OnRuleProperty;
        if (e.NewItems != null) foreach (GiftRule r in e.NewItems) r.PropertyChanged += OnRuleProperty;
        ScheduleRefresh();
    }

    private void OnRuleProperty(object? s, PropertyChangedEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // Aturan yang tampil: aktif dan punya nama efek
    private List<GiftRule> Visible() =>
        _rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.EffectName)).ToList();

    private int PageCount(int items) => Math.Max(1, (int)Math.Ceiling(items / (double)PerPage));

    // ---------- Halaman ----------

    private void NextPage()
    {
        int pages = PageCount(Visible().Count);
        if (pages <= 1 || _transitioning)
        {
            RestartProgress(pages);
            return;
        }

        _page = (_page + 1) % pages;
        ShowPage(true);
    }

    // Menampilkan halaman saat ini. transition = geser keluar lalu masuk.
    private void ShowPage(bool transition)
    {
        if (!transition || _strip.Child == null)
        {
            BuildPage(entrance: _strip.Child == null);
            return;
        }

        _transitioning = true;
        var tt = (TranslateTransform)_strip.RenderTransform;

        var outX = new DoubleAnimation(0, -90, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        var outO = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
        outO.Completed += (s, e) =>
        {
            BuildPage(entrance: true);

            var inX = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var inO = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            inO.Completed += (s2, e2) => _transitioning = false;
            tt.BeginAnimation(TranslateTransform.XProperty, inX);
            _strip.BeginAnimation(OpacityProperty, inO);
        };
        tt.BeginAnimation(TranslateTransform.XProperty, outX);
        _strip.BeginAnimation(OpacityProperty, outO);
    }

    private void BuildPage(bool entrance)
    {
        var items = Visible();
        int pages = PageCount(items.Count);
        if (_page >= pages) _page = 0;

        _cards.Clear();
        var pageItems = items.Skip(_page * PerPage).Take(PerPage).ToList();

        if (pageItems.Count == 0)
        {
            _strip.Child = BuildEmptyCard();
        }
        else
        {
            var grid = new UniformGrid { Columns = Columns };
            for (int i = 0; i < pageItems.Count; i++)
            {
                var card = BuildCard(pageItems[i], i);
                _cards[pageItems[i]] = card;
                grid.Children.Add(card);
                if (entrance) PlayEntrance(card, i);
            }
            _strip.Child = grid;
        }

        BuildDots(pages);
        RestartProgress(pages);
    }

    private void BuildDots(int pages)
    {
        _dots.Children.Clear();
        _dots.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < pages; i++)
        {
            bool active = i == _page;
            var dot = new Border
            {
                Height = 10,
                Width = 10,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(active ? Color.FromRgb(0xC0, 0x84, 0xFC) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF))
            };
            if (active)
                dot.BeginAnimation(WidthProperty, new DoubleAnimation(10, 30, TimeSpan.FromMilliseconds(350))
                    { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
            _dots.Children.Add(dot);
        }
    }

    // Bilah waktu terisi penuh selama 30 detik, lalu halaman berganti
    private void RestartProgress(int pages)
    {
        _progressBack.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _progressScale.ScaleX = 0;

        if (pages > 1)
            _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, PageInterval));

        _pageTimer.Stop();
        _pageTimer.Start();
    }

    private Border BuildEmptyCard()
    {
        var text = Label("Belum ada efek.\nIsi kolom \"Efek (overlay)\" di Gift Rules.", 16, FontWeights.Black);
        return new Border
        {
            Width = CardW * 3,
            Height = 110,
            Margin = new Thickness(Gap),
            CornerRadius = new CornerRadius(Radius),
            BorderBrush = Neon,
            BorderThickness = new Thickness(3),
            Background = new LinearGradientBrush(Palettes[0].Top, Palettes[0].Bottom, 90),
            Child = text
        };
    }

    // ---------- Kartu ----------

    private Border BuildCard(GiftRule rule, int index)
    {
        var palette = Palettes[index % Palettes.Length];

        var grid = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, CardW - 6, CardH - 6), Radius - 3, Radius - 3) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        var title = Label(rule.OverlayTitle.ToUpperInvariant(), 20, FontWeights.Black, TextWhite);
        var icon = BuildIconArea(rule, index);
        var effect = Label(rule.EffectName.Trim().ToUpperInvariant(), 20, FontWeights.Black, TextLilac);

        Grid.SetRow(title, 0);
        Grid.SetRow(icon, 1);
        Grid.SetRow(effect, 2);
        grid.Children.Add(title);
        grid.Children.Add(icon);
        grid.Children.Add(effect);

        // Garis pemisah tipis di bawah judul dan di atas efek
        grid.Children.Add(Divider(0, VerticalAlignment.Bottom, 58));
        grid.Children.Add(Divider(2, VerticalAlignment.Top, 0));

        // Kilau yang menyapu kartu
        var shine = BuildShine(index);
        Grid.SetRowSpan(shine, 3);
        grid.Children.Add(shine);

        // Kilatan putih saat aturan terpicu
        var flash = new Border
        {
            Background = Brushes.White,
            Opacity = 0,
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(flash, 3);
        grid.Children.Add(flash);

        var card = new Border
        {
            Width = CardW,
            Height = CardH,
            Margin = new Thickness(Gap),
            CornerRadius = new CornerRadius(Radius),
            BorderBrush = Neon,
            BorderThickness = new Thickness(3),
            Background = new LinearGradientBrush(palette.Top, palette.Bottom, 90),
            Effect = BaseShadow(),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Child = grid,
            Tag = flash
        };
        return card;
    }

    private static DropShadowEffect BaseShadow() => new()
    {
        Color = Color.FromRgb(0x7C, 0x3A, 0xED),
        BlurRadius = 18,
        ShadowDepth = 0,
        Opacity = 0.65
    };

    private static Border Divider(int row, VerticalAlignment va, double marginTop)
    {
        var d = new Border
        {
            Height = 2,
            Margin = new Thickness(14, 0, 14, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0xC0, 0x84, 0xFC)),
            VerticalAlignment = va
        };
        Grid.SetRow(d, row);
        return d;
    }

    private static Rectangle BuildShine(int index)
    {
        var move = new TranslateTransform(-140, 0);
        var group = new TransformGroup();
        group.Children.Add(new RotateTransform(20));
        group.Children.Add(move);

        var shine = new Rectangle
        {
            Width = 56,
            Height = CardH * 1.8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransform = group,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 0.5),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
            }, 0)
        };

        // Menyapu sekali (1,3 dtk), lalu diam sampai putaran 7 dtk berikutnya
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(7),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(700 + index * 450)
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-140, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(CardW + 140, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(CardW + 140, KeyTime.FromPercent(1)));
        move.BeginAnimation(TranslateTransform.XProperty, anim);

        return shine;
    }

    private static TextBlock Label(string text, double size, FontWeight weight, Brush? color = null) => new()
    {
        Text = text,
        FontFamily = CardFont,
        FontSize = size,
        FontWeight = weight,
        Foreground = color ?? TextWhite,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
        Effect = new DropShadowEffect { Color = Color.FromRgb(0x0A, 0x02, 0x1A), BlurRadius = 5, ShadowDepth = 1.5, Direction = 270, Opacity = 1 }
    };

    // ---------- Ikon (melayang + cahaya) ----------

    private static FrameworkElement BuildIconArea(GiftRule rule, int index)
    {
        var area = new Grid();

        // Cahaya lembut di belakang ikon, berdenyut pelan
        var glowScale = new ScaleTransform(0.9, 0.9);
        var glow = new Ellipse
        {
            Width = 124,
            Height = 124,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = glowScale,
            Fill = new RadialGradientBrush(Color.FromArgb(0xA0, 0xA8, 0x55, 0xF7), Color.FromArgb(0x00, 0xA8, 0x55, 0xF7))
        };
        var pulse = new DoubleAnimation(0.9, 1.15, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = TimeSpan.FromMilliseconds(index * 120)
        };
        glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        area.Children.Add(glow);

        var icon = BuildIcon(rule);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);

        var float_ = new TranslateTransform();
        var tilt = new RotateTransform();
        var group = new TransformGroup();
        group.Children.Add(tilt);
        group.Children.Add(float_);
        icon.RenderTransform = group;

        var begin = TimeSpan.FromMilliseconds(index * 170);
        float_.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(1.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = begin
        });
        tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(2.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            BeginTime = begin
        });

        area.Children.Add(icon);
        return area;
    }

    private static FrameworkElement BuildIcon(GiftRule rule)
    {
        string glyph;
        Brush color = TextWhite;

        switch (rule.Trigger)
        {
            case RuleTrigger.Like: glyph = "❤"; color = new SolidColorBrush(Color.FromRgb(0xE5, 0x20, 0x2A)); break;
            case RuleTrigger.Follow: glyph = "\U0001F464"; break;
            case RuleTrigger.Share: glyph = "\U0001F517"; break;
            default:
                var known = rule.GiftId != 0 ? GiftCatalog.FindById(rule.GiftId) : GiftCatalog.FindByName(rule.GiftName);
                var img = LoadImage(known?.ImageUrl);
                if (img != null)
                    return new Image
                    {
                        Source = img,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(14, 6, 14, 6),
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 3, Opacity = 0.3 }
                    };
                glyph = "\U0001F381";
                break;
        }

        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 84,
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static ImageSource? LoadImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (ImageCache.TryGetValue(url, out var cached)) return cached;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url);
            bmp.DecodePixelWidth = 220;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            ImageCache[url] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ---------- Animasi kartu ----------

    // Kartu muncul satu per satu: membesar dengan sedikit memantul sambil memudar masuk
    private static void PlayEntrance(Border card, int index)
    {
        var scale = (ScaleTransform)card.RenderTransform;
        var begin = TimeSpan.FromMilliseconds(index * 55);

        var pop = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = begin,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        card.Opacity = 0;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = begin });
    }

    // Dipanggil saat aturan terpicu: pindah ke halamannya kalau perlu, lalu kartu membesar dan menyala
    public void Pulse(GiftRule rule)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            var items = Visible();
            int idx = items.IndexOf(rule);
            if (idx < 0) return;

            int page = idx / PerPage;
            if (page != _page && !_transitioning)
            {
                _page = page;
                BuildPage(entrance: false);
                await Task.Delay(120);
            }

            if (_cards.TryGetValue(rule, out var card))
                PulseCard(card);
        });
    }

    private static void PulseCard(Border card)
    {
        var scale = (ScaleTransform)card.RenderTransform;

        var bump = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)),
            new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, bump);

        // Cahaya emas di sekeliling kartu
        var glow = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xC8, 0x3D), BlurRadius = 0, ShadowDepth = 0, Opacity = 1 };
        card.Effect = glow;
        var blur = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1100) };
        blur.KeyFrames.Add(new EasingDoubleKeyFrame(42, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        blur.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
        blur.Completed += (s, e) => card.Effect = BaseShadow();
        glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);

        // Kilatan putih singkat
        if (card.Tag is Border flash)
        {
            var f = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
            f.KeyFrames.Add(new LinearDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            f.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            flash.BeginAnimation(OpacityProperty, f);
        }
    }
}
