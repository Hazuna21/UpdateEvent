using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace HazunaV2;

/// <summary>Tema tampilan aplikasi utama (jendela, kartu, tombol). Beda dengan OverlayTheme.</summary>
public static class AppTheme
{
    // H = hue warna netral (null = tidak diubah), S/V = pengali kejenuhan/kecerahan netral.
    public sealed record Def(string Name, double? H, double S, double V,
        string Accent, string AccentDeep, string Secondary, string GradA, string GradB,
        System.Collections.Generic.Dictionary<string, string>? Map = null);

    static readonly System.Collections.Generic.Dictionary<string, string> DarkMap = new()
    {
        ["#0B0F1E"] = "#0F0F10", ["#131A2E"] = "#171718", ["#171F38"] = "#1B1B1D", ["#0E1424"] = "#0C0C0D",
        ["#1E2846"] = "#262628", ["#2A3560"] = "#35353A", ["#2B3760"] = "#333338", ["#EEF2FF"] = "#F2F2F3",
        ["#A5B0D0"] = "#A8A8B0", ["#222E58"] = "#2E2E32", ["#3A4680"] = "#46464C", ["#C9D2F5"] = "#D0D0D6",
        ["#8C97C2"] = "#8E8E96", ["#1A2242"] = "#212123", ["#252E5C"] = "#2A2A2E", ["#27336A"] = "#303034",
        ["#1B2449"] = "#222225", ["#1C1456"] = "#1A1A1C", ["#101437"] = "#131315",
    };

    static readonly System.Collections.Generic.Dictionary<string, string> LightMap = new()
    {
        ["#0B0F1E"] = "#F3F5FA", ["#131A2E"] = "#FFFFFF", ["#171F38"] = "#F7F8FC", ["#0E1424"] = "#FFFFFF",
        ["#1E2846"] = "#E8ECF5", ["#2A3560"] = "#DDE3F0", ["#2B3760"] = "#CBD3E4", ["#EEF2FF"] = "#1B2238",
        ["#A5B0D0"] = "#5B6680", ["#222E58"] = "#E6EBF7", ["#3A4680"] = "#B8C1D8", ["#C9D2F5"] = "#2E3A5C",
        ["#8C97C2"] = "#6B7694", ["#1A2242"] = "#EDF0F8", ["#252E5C"] = "#D5DCEB", ["#27336A"] = "#E3E8F5",
        ["#1B2449"] = "#D8DFEF", ["#1C1456"] = "#E9E4FF", ["#101437"] = "#EEF0FA",
    };

    public static readonly Def[] All =
    {
        new("Navy Ungu (bawaan)", null, 1.0, 1.0, "#8B5CF6", "#4C3FA8", "#22D3EE", "#7C3AED", "#3B82F6"),
        new("AMOLED Hitam",       null, 0.25, 0.42, "#A78BFA", "#4C3FA8", "#22D3EE", "#7C3AED", "#3B82F6"),
        new("Samudra",            200, 1.0, 1.0, "#22D3EE", "#0E7490", "#38BDF8", "#0891B2", "#2563EB"),
        new("Hutan",              150, 0.85, 0.95, "#34D399", "#047857", "#A3E635", "#059669", "#0D9488"),
        new("Sakura Gelap",       330, 0.8, 1.0, "#F472B6", "#9D174D", "#FBCFE8", "#DB2777", "#9333EA"),
        new("Gelap",              null, 1.0, 1.0, "#3B82F6", "#1E40AF", "#22D3EE", "#2563EB", "#7C3AED", DarkMap),
        new("Terang",             null, 1.0, 1.0, "#7C3AED", "#5B21B6", "#0891B2", "#7C3AED", "#3B82F6", LightMap),
        new("Senja",              20, 0.8, 1.0, "#FB923C", "#9A3412", "#FBBF24", "#EA580C", "#DB2777"),
    };

    public static Def Current { get; private set; } = All[0];

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static void ToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max; s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    static Color FromHsv(double h, double s, double v, byte a)
    {
        h = ((h % 360) + 360) % 360; s = Math.Clamp(s, 0, 1); v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c, r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0); else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x); else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c); else (r, g, b) = (c, 0, x);
        return Color.FromArgb(a, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    // Warna netral bawaan (Navy Ungu) - diubah lewat HSV agar semua tema punya struktur yang sama.
    static Color Neutral(string baseHex, Def t)
    {
        var c = C(baseHex);
        if (t.Map is not null && t.Map.TryGetValue(baseHex.ToUpperInvariant(), out var mapped)) return C(mapped);
        if (t.H is null && t.S == 1.0 && t.V == 1.0) return c;
        ToHsv(c, out var h, out var s, out var v);
        return FromHsv(t.H ?? h, s * t.S, v * t.V, c.A);
    }

    static void Set(string key, Color c)
    {
        // Brush diganti baru (bukan diubah) karena brush dari Theme.xaml bisa beku; XAML memakai DynamicResource.
        Application.Current.Resources[key] = new SolidColorBrush(c);
    }

    static void SetStops(string key, params Color[] colors)
    {
        if (Application.Current.TryFindResource(key) is GradientBrush src)
        {
            var g = src.Clone();
            for (int i = 0; i < colors.Length && i < g.GradientStops.Count; i++) g.GradientStops[i].Color = colors[i];
            Application.Current.Resources[key] = g;
        }
    }

    public static void Set(string name)
    {
        Current = All.FirstOrDefault(t => t.Name == name) ?? All[0];
        Apply();
    }

    public static void Apply()
    {
        if (Application.Current is null) return;
        var t = Current;
        Color N(string hex) => Neutral(hex, t);

        Set("BgBrush", N("#0B0F1E")); Set("SurfaceBrush", N("#131A2E")); Set("Surface2Brush", N("#171F38"));
        Set("InputBrush", N("#0E1424")); Set("RaisedBrush", N("#1E2846")); Set("HoverBrush", N("#2A3560"));
        Set("LineBrush", N("#2B3760")); Set("TextBrush", N("#EEF2FF")); Set("MutedBrush", N("#A5B0D0"));
        Set("RowHoverBrush", N("#222E58")); Set("ThumbBrush", N("#3A4680"));
        Set("SoftTextBrush", N("#C9D2F5")); Set("Muted2Brush", N("#8C97C2")); Set("Raised2Brush", N("#1A2242"));
        Set("SideLineBrush", N("#252E5C"));

        var accent = C(t.Accent);
        Set("AccentBrush", accent); Set("ThumbHoverBrush", accent);
        Set("AccentDeepBrush", C(t.AccentDeep));
        Set("CyanBrush", C(t.Secondary));

        SetStops("PrimaryGradient", C(t.GradA), C(t.GradB));
        SetStops("CardBorderBrush", Color.FromArgb(0x66, accent.R, accent.G, accent.B),
                 Color.FromArgb(0x55, C(t.Secondary).R, C(t.Secondary).G, C(t.Secondary).B));
        SetStops("HeaderGradient", N("#27336A"), N("#1B2449"));
        SetStops("TitleGradient", N("#EEF2FF"), accent);
        SetStops("SidebarBrush", N("#1C1456"), N("#101437"), N("#0B0F1E"));
        SetStops("GlowBrush", Color.FromArgb(0x46, accent.R, accent.G, accent.B),
                 Color.FromArgb(0x00, accent.R, accent.G, accent.B));
    }
}
