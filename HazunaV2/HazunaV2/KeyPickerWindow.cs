using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HazunaV2;

public class KeyPickerWindow : Window
{
    private const double U = 44;     // satu satuan lebar tombol (piksel)
    private const int MaxKeys = 5;

    private static readonly string[] ModifierOrder = { "Ctrl", "Alt", "Shift" };

    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0x1E, 0x28, 0x46));
    private static readonly SolidColorBrush NumpadBrush = new(Color.FromRgb(0x1D, 0x3B, 0x47));
    private static readonly SolidColorBrush HoverBrush = new(Color.FromRgb(0x34, 0x49, 0x6A));
    private static readonly SolidColorBrush SelectedBrush = new(Color.FromRgb(0x8B, 0x5C, 0xF6));
    private static readonly SolidColorBrush HintBrush = new(Color.FromRgb(0xA5, 0xB0, 0xD0));

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = "Ctrl", ["Escape"] = "Esc", ["Return"] = "Enter",
        ["`"] = "Backtick", ["-"] = "Minus", ["="] = "Equals",
        ["["] = "LBracket", ["]"] = "RBracket", ["\\"] = "Backslash",
        [";"] = "Semicolon", ["'"] = "Quote", [","] = "Comma",
        ["."] = "Period", ["/"] = "Slash"
    };

    private readonly Canvas _canvas = new();
    private readonly Dictionary<string, List<Border>> _keysByToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _selected = new();
    private readonly TextBlock _preview = new();
    private readonly TextBlock _note = new();

    public string Result { get; private set; } = "";

    public KeyPickerWindow(string initial)
    {
        Title = "Pilih Tombol";
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1E));

        BuildLayout();
        LoadInitial(initial);

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "Klik tombol untuk memilih, klik lagi untuk melepas. " +
                   "Pilih beberapa tombol untuk membuat kombinasi (maksimal 5).",
            Foreground = HintBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        previewRow.Children.Add(new TextBlock
        {
            Text = "Kombinasi:",
            Foreground = HintBrush,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        _preview.FontSize = 24;
        _preview.FontWeight = FontWeights.Bold;
        _preview.Foreground = Brushes.White;
        previewRow.Children.Add(_preview);
        root.Children.Add(previewRow);

        _canvas.Width = 22 * U;
        _canvas.Height = 6.25 * U + 4;
        root.Children.Add(_canvas);

        _note.Foreground = Brushes.Orange;
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var clear = new Button { Content = "Hapus Semua", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 24, 0) };
        clear.Click += (s, e) =>
        {
            _selected.Clear();
            _note.Text = "";
            UpdateVisuals();
        };

        var cancel = new Button { Content = "Batal", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };

        var save = new Button { Content = "Simpan", IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        save.Click += (s, e) =>
        {
            Result = BuildCombo();
            DialogResult = true;
        };

        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
        UpdateVisuals();
    }

    // ---------- Tata letak keyboard ----------

    private void BuildLayout()
    {
        // Baris fungsi
        AddKey(0, 0, 1, "Esc", "Esc");
        double[] fx = { 2, 3, 4, 5, 6.5, 7.5, 8.5, 9.5, 11, 12, 13, 14 };
        for (int i = 0; i < 12; i++)
            AddKey(fx[i], 0, 1, "F" + (i + 1), "F" + (i + 1));

        // Baris angka
        AddKey(0, 1.25, 1, "`", "Backtick");
        for (int i = 1; i <= 10; i++)
        {
            string d = (i % 10).ToString();
            AddKey(i, 1.25, 1, d, d);
        }
        AddKey(11, 1.25, 1, "-", "Minus");
        AddKey(12, 1.25, 1, "=", "Equals");
        AddKey(13, 1.25, 2, "Backspace", "Backspace");

        // Baris QWERTY
        AddKey(0, 2.25, 1.5, "Tab", "Tab");
        const string top = "QWERTYUIOP";
        for (int i = 0; i < top.Length; i++)
            AddKey(1.5 + i, 2.25, 1, top[i].ToString(), top[i].ToString());
        AddKey(11.5, 2.25, 1, "[", "LBracket");
        AddKey(12.5, 2.25, 1, "]", "RBracket");
        AddKey(13.5, 2.25, 1.5, "\\", "Backslash");

        // Baris ASDF
        const string mid = "ASDFGHJKL";
        for (int i = 0; i < mid.Length; i++)
            AddKey(1.75 + i, 3.25, 1, mid[i].ToString(), mid[i].ToString());
        AddKey(10.75, 3.25, 1, ";", "Semicolon");
        AddKey(11.75, 3.25, 1, "'", "Quote");
        AddKey(12.75, 3.25, 2.25, "Enter", "Enter");

        // Baris ZXCV
        AddKey(0, 4.25, 2.25, "Shift", "Shift");
        const string bot = "ZXCVBNM";
        for (int i = 0; i < bot.Length; i++)
            AddKey(2.25 + i, 4.25, 1, bot[i].ToString(), bot[i].ToString());
        AddKey(9.25, 4.25, 1, ",", "Comma");
        AddKey(10.25, 4.25, 1, ".", "Period");
        AddKey(11.25, 4.25, 1, "/", "Slash");
        AddKey(12.25, 4.25, 2.75, "Shift", "Shift");

        // Baris spasi
        AddKey(0, 5.25, 1.25, "Ctrl", "Ctrl");
        AddKey(2.5, 5.25, 1.25, "Alt", "Alt");
        AddKey(3.75, 5.25, 6.25, "Space", "Space");
        AddKey(10, 5.25, 1.25, "Alt", "Alt");
        AddKey(13.75, 5.25, 1.25, "Ctrl", "Ctrl");

        // Blok navigasi
        AddKey(15.5, 1.25, 1, "Ins", "Insert");
        AddKey(16.5, 1.25, 1, "Home", "Home");
        AddKey(17.5, 1.25, 1, "PgUp", "PageUp");
        AddKey(15.5, 2.25, 1, "Del", "Delete");
        AddKey(16.5, 2.25, 1, "End", "End");
        AddKey(17.5, 2.25, 1, "PgDn", "PageDown");

        // Panah
        AddKey(16.5, 4.25, 1, "↑", "Up");
        AddKey(15.5, 5.25, 1, "←", "Left");
        AddKey(16.5, 5.25, 1, "↓", "Down");
        AddKey(17.5, 5.25, 1, "→", "Right");

        // Numpad
        var numLabel = new TextBlock
        {
            Text = "Numpad (NumLock harus menyala)",
            Foreground = HintBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Width = 3 * U
        };
        Canvas.SetLeft(numLabel, 19 * U + 2);
        Canvas.SetTop(numLabel, 1.25 * U + 4);
        _canvas.Children.Add(numLabel);

        string[][] pad =
        {
            new[] { "7", "8", "9" },
            new[] { "4", "5", "6" },
            new[] { "1", "2", "3" }
        };
        for (int r = 0; r < pad.Length; r++)
            for (int c = 0; c < 3; c++)
                AddKey(19 + c, 2.25 + r, 1, "Num" + pad[r][c], "Num" + pad[r][c]);
        AddKey(19, 5.25, 2, "Num0", "Num0");
    }

    private void AddKey(double x, double y, double w, string label, string token)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = label.Length > 4 ? 10 : 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        var border = new Border
        {
            Width = w * U - 4,
            Height = U - 4,
            CornerRadius = new CornerRadius(5),
            Background = NormalBrush,
            Child = text,
            Cursor = Cursors.Hand,
            Tag = token
        };

        Canvas.SetLeft(border, x * U + 2);
        Canvas.SetTop(border, y * U + 2);

        border.MouseLeftButtonDown += (s, e) => Toggle(token);
        border.MouseEnter += (s, e) => ApplyVisual(border, true);
        border.MouseLeave += (s, e) => ApplyVisual(border, false);

        _canvas.Children.Add(border);

        if (!_keysByToken.TryGetValue(token, out var list))
        {
            list = new List<Border>();
            _keysByToken[token] = list;
        }
        list.Add(border);

        ApplyVisual(border, false);
    }

    // ---------- Pilihan ----------

    private void LoadInitial(string initial)
    {
        if (string.IsNullOrWhiteSpace(initial)) return;

        var skipped = new List<string>();
        var parts = initial.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var raw in parts)
        {
            var token = Aliases.TryGetValue(raw, out var alias) ? alias : raw;
            var canon = _keysByToken.Keys.FirstOrDefault(k =>
                string.Equals(k, token, StringComparison.OrdinalIgnoreCase));

            if (canon == null)
                skipped.Add(raw);
            else if (!_selected.Contains(canon, StringComparer.OrdinalIgnoreCase) && _selected.Count < MaxKeys)
                _selected.Add(canon);
        }

        if (skipped.Count > 0)
            _note.Text = "Bagian lama tidak dikenali dan dilewati: " + string.Join(", ", skipped);
    }

    private void Toggle(string token)
    {
        int idx = _selected.FindIndex(s => string.Equals(s, token, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _selected.RemoveAt(idx);
        }
        else
        {
            if (_selected.Count >= MaxKeys)
            {
                _note.Text = $"Maksimal {MaxKeys} tombol sekaligus.";
                return;
            }
            _selected.Add(token);
        }

        _note.Text = "";
        UpdateVisuals();
    }

    private string BuildCombo()
    {
        var mods = ModifierOrder
            .Where(m => _selected.Contains(m, StringComparer.OrdinalIgnoreCase));
        var others = _selected
            .Where(s => !ModifierOrder.Contains(s, StringComparer.OrdinalIgnoreCase));
        return string.Join("+", mods.Concat(others));
    }

    private void UpdateVisuals()
    {
        foreach (var list in _keysByToken.Values)
            foreach (var b in list)
                ApplyVisual(b, false);

        var combo = BuildCombo();
        _preview.Text = combo.Length == 0 ? "(kosong)" : combo;
    }

    private void ApplyVisual(Border b, bool hover)
    {
        var token = (string)b.Tag;
        bool selected = _selected.Contains(token, StringComparer.OrdinalIgnoreCase);

        if (selected)
            b.Background = SelectedBrush;
        else if (hover)
            b.Background = HoverBrush;
        else
            b.Background = token.StartsWith("Num", StringComparison.Ordinal) ? NumpadBrush : NormalBrush;
    }
}