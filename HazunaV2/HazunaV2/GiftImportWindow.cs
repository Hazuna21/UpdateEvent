using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HazunaV2;

public class GiftImportWindow : Window
{
    private readonly TextBox _box = new();

    public int Added { get; private set; }
    public int Updated { get; private set; }

    public GiftImportWindow()
    {
        Title = "Impor Daftar Gift";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1E));
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var info = new TextBlock
        {
            Text = "Tempel daftar gift, satu gift per baris.\n" +
                   "Format: Nama;diamond   (contoh: Rose;1). Diamond boleh dikosongkan.\n" +
                   "Pemisah boleh titik koma, tab (dari tabel/spreadsheet), atau koma sebelum angka diamond.\n" +
                   "Gift yang sudah ada akan diperbarui harganya.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB0, 0xD0)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(info, 0);
        grid.Children.Add(info);

        _box.AcceptsReturn = true;
        _box.TextWrapping = TextWrapping.NoWrap;
        _box.VerticalAlignment = VerticalAlignment.Stretch;
        _box.VerticalContentAlignment = VerticalAlignment.Top;
        _box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _box.FontFamily = new FontFamily("Consolas");
        Grid.SetRow(_box, 1);
        grid.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Batal", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var import = new Button { Content = "Impor", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        import.Click += (s, e) => DoImport();
        buttons.Children.Add(cancel);
        buttons.Children.Add(import);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
        Loaded += (s, e) => _box.Focus();
    }

    private void DoImport()
    {
        int added = 0, updated = 0, skipped = 0;

        foreach (var raw in _box.Text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;

            var parsed = ParseLine(line);
            if (parsed == null)
            {
                skipped++;
                continue;
            }

            var (name, diamonds) = parsed.Value;
            bool existed = GiftCatalog.FindByName(name) != null;
            GiftCatalog.AddManual(name, diamonds);
            if (existed) updated++; else added++;
        }

        if (added + updated == 0)
        {
            MessageBox.Show(skipped > 0
                ? "Tidak ada baris yang bisa dibaca. Pakai format: Nama;diamond"
                : "Belum ada yang ditempel.");
            return;
        }

        GiftCatalog.Flush();
        Added = added;
        Updated = updated;
        DialogResult = true;
    }

    private static (string Name, long Diamonds)? ParseLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0) return null;

        string name = line;
        string num = "";

        int sep = line.IndexOfAny(new[] { ';', '\t' });
        if (sep >= 0)
        {
            name = line[..sep].Trim();
            // Ambil kolom berikutnya saja (abaikan kolom lain dari tabel)
            num = line[(sep + 1)..].Split(';', '\t')[0].Trim();
        }
        else
        {
            int comma = line.LastIndexOf(',');
            if (comma > 0)
            {
                var right = line[(comma + 1)..].Trim();
                if (right.Length > 0 && right.All(char.IsDigit))
                {
                    name = line[..comma].Trim();
                    num = right;
                }
            }
        }

        if (name.Length == 0) return null;

        long diamonds = 0;
        if (num.Length > 0)
        {
            // "1.000" atau "1,000" dibaca sebagai 1000
            var digits = new string(num.Where(char.IsDigit).ToArray());
            long.TryParse(digits, out diamonds);
        }

        return (name, diamonds);
    }
}