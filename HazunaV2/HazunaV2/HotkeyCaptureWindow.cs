using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HazunaV2;

// Menangkap satu kombinasi tombol pintas (mis. Ctrl+Alt+1). Hasil kosong = hapus tombol pintas.
public class HotkeyCaptureWindow : Window
{
    private readonly TextBlock _preview = new();
    private readonly TextBlock _note = new();
    private string _current;

    public string Result { get; private set; }
    public bool Saved { get; private set; }

    public HotkeyCaptureWindow(string soundName, string initial)
    {
        _current = initial ?? "";
        Result = _current;

        Title = "Tombol pintas";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Application.Current.TryFindResource("BgBrush") as Brush ?? Brushes.Black;
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        Brush Res(string k, Brush fb) => Application.Current.TryFindResource(k) as Brush ?? fb;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Tombol pintas untuk: " + soundName,
            Foreground = Res("TextBrush", Brushes.White),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = "Tekan kombinasi tombolnya sekarang (misalnya Ctrl+Alt+1). Berlaku di mana saja, termasuk saat game dibuka. " +
                   "Tombol yang dipakai di sini tidak sampai ke game, jadi hindari tombol yang dipakai game.",
            Foreground = Res("MutedBrush", Brushes.Gray),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });

        _preview.FontSize = 26;
        _preview.FontWeight = FontWeights.Bold;
        _preview.Foreground = Res("CyanBrush", Brushes.Cyan);
        _preview.HorizontalAlignment = HorizontalAlignment.Center;
        _preview.Margin = new Thickness(0, 4, 0, 4);
        root.Children.Add(_preview);

        _note.Foreground = Brushes.Orange;
        _note.FontSize = 12;
        _note.TextWrapping = TextWrapping.Wrap;
        _note.MinHeight = 18;
        _note.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(_note);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        Button Make(string text, string style, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), Focusable = false };
            if (Application.Current.TryFindResource(style) is Style st) b.Style = st;
            b.Click += (s, e) => act();
            return b;
        }
        row.Children.Add(Make("Hapus", "GhostButton", () => { _current = ""; Update(); }));
        row.Children.Add(Make("Batal", "GhostButton", Close));
        row.Children.Add(Make("Simpan", "PrimaryButton", () => { Result = _current; Saved = true; Close(); }));
        root.Children.Add(row);

        Content = root;
        Update();
        PreviewKeyDown += OnKey;
    }

    private void Update() => _preview.Text = _current.Length == 0 ? "(belum ada)" : _current;

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed)
            return;

        var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        string text = Hotkeys.Format(mods, key);
        if (Hotkeys.Parse(text, out _, out _) == false)
        {
            _note.Text = "Tombol ini perlu digabung dengan Ctrl, Alt, atau Shift.";
            return;
        }
        _note.Text = "";
        _current = text;
        Update();
    }
}

public static class Hotkeys
{
    public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4, NoRepeat = 0x4000;

    // Tombol yang boleh dipakai tanpa Ctrl/Alt/Shift (tidak mengganggu pengetikan)
    private static bool AloneOk(Key k) =>
        (k >= Key.F1 && k <= Key.F24) || (k >= Key.NumPad0 && k <= Key.Divide);

    public static string Format(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    // Menampilkan nama yang rapi (D1 -> 1)
    public static string Pretty(string hotkey) =>
        string.Join("+", hotkey.Split('+').Select(p => p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) ? p[1..] : p));

    // true = valid, false = tidak valid, hasil modifier & vk lewat out
    public static bool Parse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        Key key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.Trim())
            {
                case "Ctrl": modifiers |= ModCtrl; break;
                case "Alt": modifiers |= ModAlt; break;
                case "Shift": modifiers |= ModShift; break;
                default:
                    if (!Enum.TryParse<Key>(part.Trim(), out key)) return false;
                    break;
            }
        }
        if (key == Key.None) return false;
        if (modifiers == 0 && !AloneOk(key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }
}
