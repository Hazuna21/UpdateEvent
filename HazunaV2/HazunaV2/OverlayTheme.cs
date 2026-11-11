using System.Windows.Media;

namespace HazunaV2;

// Tema warna semua overlay. Desain bawaan berwarna ungu neon; tema lain menggeser rona (hue) warnanya
// sehingga seluruh susunan warna (bingkai, kilau, bayangan, roda) ikut berganti dengan serasi.
public static class OverlayTheme
{
    public sealed record Def(string Name, double HueShift, double Sat = 1, double Val = 1);

    public static readonly Def[] All =
    {
        new("Neon Ungu", 0),
        new("Api", 110, 1.05, 1.05),
        new("Es", -70, 0.9, 1.05),
        new("Emas", 140, 0.95, 1.05),
        new("Hijau Matrix", -125, 1.0, 0.95),
        new("Pink Sakura", 65, 0.9, 1.08),
    };

    public static Def Current { get; private set; } = All[0];

    public static void Set(string? name) =>
        Current = All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static Color Rgb(int r, int g, int b) => Map(Color.FromRgb((byte)r, (byte)g, (byte)b));
    public static Color Argb(int a, int r, int g, int b) => Map(Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b));

    public static Color Map(Color c)
    {
        var t = Current;
        if (t.HueShift == 0 && t.Sat == 1 && t.Val == 1) return c;

        ToHsv(c, out double h, out double s, out double v);
        if (s < 0.12) return c;                      // putih, abu-abu, hitam tidak berubah

        h = (h + t.HueShift + 360) % 360;
        s = Math.Clamp(s * t.Sat, 0, 1);
        v = Math.Clamp(v * t.Val, 0, 1);
        var r = FromHsv(h, s, v);
        return Color.FromArgb(c.A, r.R, r.G, r.B);
    }

    private static void ToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
