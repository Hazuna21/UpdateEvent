using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
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

// Overlay "menu gift" berukuran tetap 9 x 5 inci (kanvas HD 1920 x 1067, digambar vektor jadi tetap tajam).
// - Gift yang sama digabung dalam satu kartu; kartu berputar (depan/belakang) bergantian antar efeknya.
// - Kartu mengisi dari atas dan rata tengah; setelah penuh (15 kartu) pindah ke halaman berikutnya (30 dtk).
// - Dioptimalkan: bagian statis di-cache sebagai bitmap, animasi hanya menggerakkan transform,
//   gambar gift diunduh di latar belakang, dan rebuild hanya jika isi benar-benar berubah.
public class GiftMenuOverlay : Window
{
    // ----- Ukuran -----
    private const double WidthInches = 9, HeightInches = 5;
    private const double ScreenScale = 1.0;             // 1 = 9x5 inci (96 dpi). Ubah ke ~2.22 untuk jendela 1920 px asli.
    private const double DesignW = 1920, DesignH = 1067;// kanvas HD (9:5)

    // ----- Tata letak -----
    private const int PerPage = 15;
    private const int Columns = 5;
    private const double CardW = 356, CardH = 300, CardGap = 7, CardRadius = 26;
    private const double TopPad = 24, PagerHeight = 56;

    private static readonly TimeSpan PageInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlipInterval = TimeSpan.FromMilliseconds(3400);
    private static readonly TimeSpan ShineInterval = TimeSpan.FromMilliseconds(1700);

    // ----- Gaya (semua beku supaya murah) -----
    private static readonly Brush Neon = Frozen(new SolidColorBrush(Color.FromRgb(0xA7, 0x6B, 0xFF)));
    private static readonly Brush TextWhite = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush TextLilac = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xC8, 0xFF)));
    private static readonly Brush DividerBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0xC0, 0x84, 0xFC)));
    private static readonly Brush FlashBrush = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush GoldBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)));
    private static readonly FontFamily CardFont = new("Segoe UI Black, Segoe UI, Arial");

    private static readonly DropShadowEffect TextShadow = FrozenEffect(new DropShadowEffect
    {
        Color = Color.FromRgb(0x0A, 0x02, 0x1A), BlurRadius = 8, ShadowDepth = 3, Direction = 270, Opacity = 1
    });

    private static readonly Brush[] FrameBrushes =
    {
        Gradient(Color.FromRgb(0x3E, 0x1C, 0x72), Color.FromRgb(0x16, 0x0A, 0x2E)),
        Gradient(Color.FromRgb(0x4B, 0x20, 0x84), Color.FromRgb(0x1D, 0x0B, 0x3A)),
        Gradient(Color.FromRgb(0x33, 0x16, 0x62), Color.FromRgb(0x12, 0x08, 0x26)),
        Gradient(Color.FromRgb(0x56, 0x26, 0x96), Color.FromRgb(0x20, 0x0D, 0x40)),
    };

    private static readonly Brush ShineBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
        new GradientStop(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.5),
        new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
    }, 0));

    private static readonly BitmapCache Cache = FrozenCache(new BitmapCache(1.0));

    // Cahaya di belakang ikon (statis)
    private static readonly Brush IconGlowBrush = Frozen(new RadialGradientBrush(
        Color.FromArgb(0x90, 0xA8, 0x55, 0xF7), Color.FromArgb(0x00, 0xA8, 0x55, 0xF7)));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static DropShadowEffect FrozenEffect(DropShadowEffect e) { e.Freeze(); return e; }
    private static BitmapCache FrozenCache(BitmapCache c) { c.Freeze(); return c; }
    private static Brush Gradient(Color top, Color bottom) => Frozen(new LinearGradientBrush(top, bottom, 90));

    private static T Fps<T>(T t, int fps) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(t, fps);
        return t;
    }

    // ----- Gambar gift (diunduh di latar belakang, tidak menahan UI) -----
    private static readonly Dictionary<string, ImageSource> ImageCache = new();
    private static readonly HashSet<string> ImageLoading = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static event Action? ImageReady;

    private static ImageSource? GetImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        lock (ImageCache)
            if (ImageCache.TryGetValue(url, out var cached)) return cached;

        lock (ImageLoading)
            if (!ImageLoading.Add(url)) return null;

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 400;     // HD
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                lock (ImageCache) ImageCache[url] = bmp;
                ImageReady?.Invoke();
            }
            catch
            {
                // gagal: tetap pakai ikon emoji
            }
        });
        return null;
    }

    // ----- Status kartu -----
    // Satu muka kartu = satu efek. Sumbernya bisa lebih dari satu (mis. Tap-tap + gift Heart Me).
    private sealed class Face
    {
        public string Effect = "";
        public List<GiftRule> Rules = new();
    }

    private sealed class CardState
    {
        public List<Face> Faces = new();
        public int Face;
        public int Index;
        public Grid Root = null!;
        public TextBlock Title = null!;
        public TextBlock Effect = null!;
        public Grid IconHost = null!;
        public ScaleTransform Flip = new(1, 1);
        public ScaleTransform Pop = new(1, 1);
        public Border Ring = null!;
        public Border Flash = null!;
        public Rectangle Shine = null!;
        public TranslateTransform ShineMove = new();
        public bool Flipping;
    }

    private static readonly HashSet<string> RelevantProps = new()
    {
        nameof(GiftRule.Enabled), nameof(GiftRule.EffectName), nameof(GiftRule.Trigger),
        nameof(GiftRule.LikeCount), nameof(GiftRule.GiftName), nameof(GiftRule.GiftId)
    };

    private readonly ObservableCollection<GiftRule> _rules;
    private readonly StackPanel _rowsPanel = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _progressBack = new();
    private readonly Border _progressFill = new();
    private readonly ScaleTransform _progressScale = new(0, 1);
    private readonly TranslateTransform _rowsMove = new();
    private readonly TranslateTransform[] _floats = { new(), new(), new() };

    private readonly DispatcherTimer _pageTimer = new() { Interval = PageInterval };
    private readonly DispatcherTimer _flipTimer = new() { Interval = FlipInterval };
    private readonly DispatcherTimer _shineTimer = new() { Interval = ShineInterval };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private readonly List<CardState> _cards = new();
    private readonly Dictionary<GiftRule, CardState> _byRule = new();

    private int _page;
    private int _shineIndex;
    private bool _transitioning;
    private bool _forceRefresh;
    private string _signature = "";

    public GiftMenuOverlay(ObservableCollection<GiftRule> rules)
    {
        _rules = rules;

        Title = "Menu Gift Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = 40;
        Top = 40;
        Width = WidthInches * 96 * ScreenScale;
        Height = HeightInches * 96 * ScreenScale;

        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

        // ----- Kanvas HD -----
        var canvas = new Grid { Width = DesignW, Height = DesignH };

        _rowsPanel.Margin = new Thickness(0, TopPad, 0, 0);
        _rowsPanel.RenderTransform = _rowsMove;
        canvas.Children.Add(_rowsPanel);

        _progressBack.Height = 8;
        _progressBack.Width = 760;
        _progressBack.CornerRadius = new CornerRadius(4);
        _progressBack.Background = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xC0, 0x84, 0xFC)));
        _progressBack.ClipToBounds = true;
        _progressFill.CornerRadius = new CornerRadius(4);
        _progressFill.Background = Frozen(new LinearGradientBrush(
            Color.FromRgb(0xE8, 0x79, 0xF9), Color.FromRgb(0x8B, 0x5C, 0xF6), 0));
        _progressFill.RenderTransformOrigin = new Point(0, 0.5);
        _progressFill.RenderTransform = _progressScale;
        _progressBack.Child = _progressFill;

        var pager = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
            Height = PagerHeight - 14
        };
        pager.Children.Add(_progressBack);
        _dots.Margin = new Thickness(0, 10, 0, 0);
        pager.Children.Add(_dots);
        canvas.Children.Add(pager);

        Content = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };

        // Geser jendela dengan menahan klik kiri
        MouseLeftButtonDown += (s, e) => DragMove();

        _rules.CollectionChanged += OnRulesChanged;
        foreach (var r in _rules) r.PropertyChanged += OnRuleProperty;
        ImageReady += OnImageReady;

        _refreshTimer.Tick += (s, e) => RefreshTick();
        _pageTimer.Tick += (s, e) => NextPage();
        _flipTimer.Tick += (s, e) => FlipAll();
        _shineTimer.Tick += (s, e) => ShineNext();

        Closed += (s, e) =>
        {
            _pageTimer.Stop();
            _flipTimer.Stop();
            _shineTimer.Stop();
            _refreshTimer.Stop();
            ImageReady -= OnImageReady;
            _rules.CollectionChanged -= OnRulesChanged;
            foreach (var r in _rules) r.PropertyChanged -= OnRuleProperty;
        };

        Loaded += (s, e) =>
        {
            StartFloating();
            _signature = Signature(Cards());
            BuildPage(entrance: true);
            _pageTimer.Start();
            _flipTimer.Start();
            _shineTimer.Start();
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

    private void OnRuleProperty(object? s, PropertyChangedEventArgs e)
    {
        // Hanya properti yang memengaruhi tampilan yang memicu pembaruan
        if (e.PropertyName != null && RelevantProps.Contains(e.PropertyName))
            ScheduleRefresh();
    }

    private void OnImageReady()
    {
        Dispatcher.InvokeAsync(() =>
        {
            _forceRefresh = true;
            ScheduleRefresh();
        });
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void RefreshTick()
    {
        _refreshTimer.Stop();
        if (_transitioning)
        {
            _refreshTimer.Start();
            return;
        }

        var sig = Signature(Cards());
        if (sig == _signature && !_forceRefresh) return;

        _signature = sig;
        _forceRefresh = false;
        BuildPage(entrance: false);
    }

    // ---------- Data ----------

    // Aturan yang tampil: aktif dan punya nama efek
    private List<GiftRule> Visible() =>
        _rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.EffectName)).ToList();

    // Susunan kartu:
    // 1) Aturan dengan nama efek yang sama digabung jadi satu muka (satu efek, banyak sumber).
    // 2) Muka-muka yang berbagi sumber yang sama (gift yang sama, atau sama-sama Tap-tap) ditaruh
    //    dalam satu kartu dan berputar bergantian.
    // Urutan kartu dan muka mengikuti urutan aturan di Gift Rules.
    private List<List<Face>> Cards()
    {
        var faces = new List<Face>();
        var byEffect = new Dictionary<string, Face>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in Visible())
        {
            string effect = r.EffectName.Trim();
            if (!byEffect.TryGetValue(effect, out var face))
            {
                face = new Face { Effect = effect };
                byEffect[effect] = face;
                faces.Add(face);
            }
            face.Rules.Add(r);
        }

        // Gabungkan muka yang berbagi sumber (union-find sederhana)
        int[] parent = Enumerable.Range(0, faces.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

        var firstBySource = new Dictionary<string, int>();
        for (int i = 0; i < faces.Count; i++)
        {
            foreach (var r in faces[i].Rules)
            {
                string key = GroupKey(r);
                if (firstBySource.TryGetValue(key, out int j))
                {
                    int a = Find(i), b = Find(j);
                    if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                }
                else
                {
                    firstBySource[key] = i;
                }
            }
        }

        var cards = new List<List<Face>>();
        var byRoot = new Dictionary<int, List<Face>>();
        for (int i = 0; i < faces.Count; i++)
        {
            int root = Find(i);
            if (!byRoot.TryGetValue(root, out var list))
            {
                list = new List<Face>();
                byRoot[root] = list;
                cards.Add(list);
            }
            list.Add(faces[i]);
        }
        return cards;
    }

    // Identitas sumber: gift yang sama (per ID atau nama), atau jenis Tap-tap / Follow / Share
    private static string GroupKey(GiftRule r) => r.Trigger switch
    {
        RuleTrigger.Gift => r.GiftId != 0 ? "G#" + r.GiftId : "G:" + r.GiftName.Trim().ToLowerInvariant(),
        RuleTrigger.Like => "L",
        RuleTrigger.Follow => "F",
        _ => "S"
    };

    private static string Signature(List<List<Face>> cards) =>
        string.Join("||", cards.Select(c => string.Join("//", c.Select(f =>
            f.Effect + "=" + string.Join("|", f.Rules.Select(r => $"{r.Trigger}:{r.GiftId}:{r.GiftName}:{r.LikeCount}"))))));

    private static int PageCount(int groups) => Math.Max(1, (int)Math.Ceiling(groups / (double)PerPage));

    // ---------- Halaman ----------

    private void NextPage()
    {
        int pages = PageCount(Cards().Count);
        if (pages <= 1 || _transitioning)
        {
            RestartProgress(pages);
            return;
        }

        _page = (_page + 1) % pages;
        TransitionToPage();
    }

    // Halaman lama bergeser keluar, halaman baru masuk. Selama bergeser, isinya di-cache jadi satu bitmap.
    private void TransitionToPage()
    {
        _transitioning = true;
        _rowsPanel.CacheMode = Cache;

        var outX = Fps(new DoubleAnimation(0, -110, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } }, 60);
        var outO = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240));
        outO.Completed += (s, e) =>
        {
            BuildPage(entrance: false);

            var inX = new DoubleAnimation(110, 0, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var inO = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            inO.Completed += (s2, e2) =>
            {
                _rowsPanel.CacheMode = null;
                _transitioning = false;
            };
            _rowsMove.BeginAnimation(TranslateTransform.XProperty, inX);
            _rowsPanel.BeginAnimation(OpacityProperty, inO);
        };
        _rowsMove.BeginAnimation(TranslateTransform.XProperty, outX);
        _rowsPanel.BeginAnimation(OpacityProperty, outO);
    }

    private void BuildPage(bool entrance)
    {
        var cards = Cards();
        int pages = PageCount(cards.Count);
        if (_page >= pages) _page = 0;

        _cards.Clear();
        _byRule.Clear();
        _rowsPanel.Children.Clear();

        var pageGroups = cards.Skip(_page * PerPage).Take(PerPage).ToList();

        if (pageGroups.Count == 0)
        {
            _rowsPanel.Children.Add(BuildEmptyCard());
        }
        else
        {
            // Kartu mengisi dari atas, tiap baris rata tengah
            for (int start = 0; start < pageGroups.Count; start += Columns)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                for (int i = start; i < Math.Min(start + Columns, pageGroups.Count); i++)
                {
                    var st = BuildCard(pageGroups[i], i, entrance);
                    _cards.Add(st);
                    foreach (var f in pageGroups[i]) foreach (var r in f.Rules) _byRule[r] = st;
                    row.Children.Add(st.Root);
                }
                _rowsPanel.Children.Add(row);
            }
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
            _dots.Children.Add(new Border
            {
                Height = 14,
                Width = active ? 44 : 14,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(6, 0, 6, 0),
                Background = active
                    ? Frozen(new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC)))
                    : Frozen(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)))
            });
        }
    }

    // Bilah waktu terisi selama 30 detik, lalu halaman berganti
    private void RestartProgress(int pages)
    {
        _progressBack.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _progressScale.ScaleX = 0;

        if (pages > 1)
            _progressScale.BeginAnimation(ScaleTransform.ScaleXProperty, Fps(new DoubleAnimation(0, 1, PageInterval), 10));

        _pageTimer.Stop();
        _pageTimer.Start();
    }

    private static Border BuildEmptyCard() => new()
    {
        Width = CardW * 3,
        Height = 150,
        Margin = new Thickness(CardGap),
        CornerRadius = new CornerRadius(CardRadius),
        BorderBrush = Neon,
        BorderThickness = new Thickness(5),
        Background = FrameBrushes[0],
        Child = Label("Belum ada efek.\nIsi kolom \"Efek (overlay)\" di Gift Rules.", 30, TextWhite)
    };

    // ---------- Kartu ----------

    private CardState BuildCard(List<Face> faces, int index, bool entrance)
    {
        var st = new CardState { Faces = faces, Index = index };

        // Bingkai statis (judul, efek, garis): di-cache sebagai bitmap supaya tidak digambar ulang tiap frame
        var inner = new Grid();
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });

        st.Title = Label("", 36, TextWhite);
        st.Effect = Label("", 36, TextLilac);
        Grid.SetRow(st.Title, 0);
        Grid.SetRow(st.Effect, 2);
        inner.Children.Add(st.Title);
        inner.Children.Add(st.Effect);
        inner.Children.Add(Divider(VerticalAlignment.Top, 84));
        inner.Children.Add(Divider(VerticalAlignment.Bottom, 84));

        var frame = new Border
        {
            CornerRadius = new CornerRadius(CardRadius),
            BorderBrush = Neon,
            BorderThickness = new Thickness(5),
            Background = FrameBrushes[index % FrameBrushes.Length],
            CacheMode = Cache,
            Child = inner
        };

        // Ikon (lapisan sendiri, di-cache; hanya digeser naik-turun)
        st.IconHost = new Grid { Margin = new Thickness(0, 84, 0, 84), IsHitTestVisible = false };

        // Kilau (hanya aktif sebentar), kilatan putih, dan cincin emas saat terpicu
        st.Shine = new Rectangle
        {
            Width = 110,
            Height = CardH * 1.8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Fill = ShineBrush,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var shineGroup = new TransformGroup();
        shineGroup.Children.Add(new RotateTransform(20));
        shineGroup.Children.Add(st.ShineMove);
        st.Shine.RenderTransform = shineGroup;

        st.Flash = new Border
        {
            Background = FlashBrush,
            Opacity = 0,
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(CardRadius)
        };

        var overlay = new Grid
        {
            IsHitTestVisible = false,
            Clip = Frozen(new RectangleGeometry(new Rect(0, 0, CardW, CardH), CardRadius, CardRadius))
        };
        overlay.Children.Add(st.Shine);
        overlay.Children.Add(st.Flash);

        st.Ring = new Border
        {
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(9),
            CornerRadius = new CornerRadius(CardRadius),
            Opacity = 0,
            IsHitTestVisible = false
        };

        var root = new Grid
        {
            Width = CardW,
            Height = CardH,
            Margin = new Thickness(CardGap),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var group = new TransformGroup();
        group.Children.Add(st.Flip);
        group.Children.Add(st.Pop);
        root.RenderTransform = group;

        root.Children.Add(frame);
        root.Children.Add(st.IconHost);
        root.Children.Add(overlay);
        root.Children.Add(st.Ring);
        st.Root = root;

        SetFace(st, 0);
        if (entrance) PlayEntrance(st, index);
        return st;
    }

    // Menampilkan satu muka kartu: judul (semua sumbernya), ikon-ikon sumber, dan nama efek
    private void SetFace(CardState st, int face)
    {
        st.Face = face % st.Faces.Count;
        var f = st.Faces[st.Face];

        string title = string.Join(" + ", f.Rules.Select(r => r.OverlayTitle.ToUpperInvariant()).Distinct());
        string effect = f.Effect.ToUpperInvariant();

        st.Title.Text = title;
        st.Title.FontSize = FontFor(title);
        st.Effect.Text = effect;
        st.Effect.FontSize = FontFor(effect);

        st.IconHost.Children.Clear();
        st.IconHost.Children.Add(BuildFaceIcons(f, st.Index));
    }

    // Satu ikon per sumber yang berbeda (maksimal 3), berdampingan kalau lebih dari satu
    private FrameworkElement BuildFaceIcons(Face face, int index)
    {
        var sources = face.Rules.GroupBy(GroupKey).Select(g => g.First()).Take(3).ToList();
        if (sources.Count == 1)
            return BuildIcon(sources[0], index);

        var grid = new UniformGrid { Rows = 1, Columns = sources.Count };
        foreach (var rule in sources)
            grid.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(2), Child = BuildIcon(rule, index) });
        return grid;
    }

    private static double FontFor(string text) => text.Length <= 12 ? 36 : text.Length <= 18 ? 30 : text.Length <= 26 ? 25 : 20;

    private static Border Divider(VerticalAlignment va, double offset) => new()
    {
        Height = 3,
        Margin = new Thickness(22, va == VerticalAlignment.Top ? offset : 0, 22, va == VerticalAlignment.Bottom ? offset : 0),
        Background = DividerBrush,
        VerticalAlignment = va
    };

    private static TextBlock Label(string text, double size, Brush color) => new()
    {
        Text = text,
        FontFamily = CardFont,
        FontSize = size,
        FontWeight = FontWeights.Black,
        Foreground = color,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(12, 0, 12, 0),
        Effect = TextShadow
    };

    // ---------- Ikon ----------

    private FrameworkElement BuildIcon(GiftRule rule, int index)
    {
        FrameworkElement icon;
        string? glyph = null;
        Brush color = TextWhite;

        switch (rule.Trigger)
        {
            case RuleTrigger.Like: glyph = "❤"; color = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x20, 0x2A))); break;
            case RuleTrigger.Follow: glyph = "\U0001F464"; break;
            case RuleTrigger.Share: glyph = "\U0001F517"; break;
            default:
                var known = rule.GiftId != 0 ? GiftCatalog.FindById(rule.GiftId) : GiftCatalog.FindByName(rule.GiftName);
                var img = GetImage(known?.ImageUrl);
                if (img != null)
                {
                    icon = new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(26, 10, 26, 10) };
                    RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                    return WrapIcon(icon, index);
                }
                glyph = "\U0001F381";
                break;
        }

        icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 150,
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return WrapIcon(icon, index);
    }

    // Ikon + cahaya di belakangnya jadi satu lapisan cache yang hanya digeser (murah)
    private FrameworkElement WrapIcon(FrameworkElement icon, int index)
    {
        var holder = new Grid { CacheMode = Cache, RenderTransform = _floats[index % _floats.Length] };
        holder.Children.Add(new Ellipse { Width = 230, Height = 230, Fill = IconGlowBrush });
        holder.Children.Add(icon);
        return holder;
    }

    // Tiga transform bersama menggerakkan semua ikon (fase berbeda), jadi hanya 3 animasi yang berjalan
    private void StartFloating()
    {
        for (int i = 0; i < _floats.Length; i++)
        {
            _floats[i].BeginAnimation(TranslateTransform.YProperty, Fps(new DoubleAnimation(-9, 9, TimeSpan.FromSeconds(2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                BeginTime = TimeSpan.FromMilliseconds(i * 650)
            }, 30));
        }
    }

    // ---------- Animasi ----------

    // Kartu muncul satu per satu: membesar dengan sedikit memantul sambil memudar masuk
    private static void PlayEntrance(CardState st, int index)
    {
        var begin = TimeSpan.FromMilliseconds(index * 45);

        st.Pop.ScaleX = 0.55;
        st.Pop.ScaleY = 0.55;
        var pop = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(480))
        {
            BeginTime = begin,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
        };
        st.Pop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        st.Pop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        st.Root.Opacity = 0;
        st.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = begin });
    }

    // Kartu yang punya lebih dari satu efek berputar (depan/belakang) bergantian
    private void FlipAll()
    {
        if (_transitioning) return;

        int k = 0;
        foreach (var st in _cards)
        {
            if (st.Faces.Count < 2) continue;
            var card = st;
            int delay = k++ * 160;
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(delay);
                if (_cards.Contains(card)) FlipTo(card, card.Face + 1);
            });
        }
    }

    // Memutar kartu: menyempit sampai tipis, ganti muka, lalu melebar lagi
    private void FlipTo(CardState st, int face)
    {
        if (st.Flipping) return;
        st.Flipping = true;

        var close = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(170))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        close.Completed += (s, e) =>
        {
            SetFace(st, face);
            var open = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            open.Completed += (s2, e2) => st.Flipping = false;
            st.Flip.BeginAnimation(ScaleTransform.ScaleXProperty, open);
        };
        st.Flip.BeginAnimation(ScaleTransform.ScaleXProperty, close);
    }

    // Kilau menyapu satu kartu pada satu waktu, bergiliran
    private void ShineNext()
    {
        if (_cards.Count == 0 || _transitioning) return;

        var st = _cards[_shineIndex++ % _cards.Count];
        st.Shine.Visibility = Visibility.Visible;

        var move = Fps(new DoubleAnimation(-220, CardW + 220, TimeSpan.FromMilliseconds(900))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }, 30);
        move.Completed += (s, e) => st.Shine.Visibility = Visibility.Collapsed;
        st.ShineMove.BeginAnimation(TranslateTransform.XProperty, move);
    }

    // Dipanggil saat aturan terpicu: pindah ke halamannya kalau perlu, putar ke efeknya, lalu kartu menyala
    public void Pulse(GiftRule rule)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            var cards = Cards();
            int idx = cards.FindIndex(c => c.Any(f => f.Rules.Contains(rule)));
            if (idx < 0) return;

            int page = idx / PerPage;
            if (page != _page && !_transitioning)
            {
                _page = page;
                BuildPage(entrance: false);
                await Task.Delay(120);
            }

            if (!_byRule.TryGetValue(rule, out var st)) return;

            int face = st.Faces.FindIndex(f => f.Rules.Contains(rule));
            if (face >= 0 && face != st.Face)
                SetFace(st, face);

            PulseCard(st);
        });
    }

    private static void PulseCard(CardState st)
    {
        var bump = Fps(new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) }, 60);
        bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)),
            new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }));
        st.Pop.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
        st.Pop.BeginAnimation(ScaleTransform.ScaleYProperty, bump);

        // Cincin emas (tanpa blur, jadi ringan)
        var ring = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1100) };
        ring.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130))));
        ring.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
        st.Ring.BeginAnimation(OpacityProperty, ring);

        var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(550) };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550))));
        st.Flash.BeginAnimation(OpacityProperty, flash);
    }
}
