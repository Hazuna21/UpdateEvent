using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HazunaV2;

public class GiftPickerWindow : Window
{
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0xA5, 0xB0, 0xD0));
    private static readonly SolidColorBrush PriceBrush = new(Color.FromRgb(0x22, 0xD3, 0xEE));
    private static readonly Dictionary<string, BitmapImage> IconCache = new();

    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly TextBox _manualName = new();
    private readonly TextBox _manualDiamonds = new();
    private readonly TextBlock _hint = new();
    private readonly TextBlock _note = new();
    private readonly long _currentId;
    private readonly string _currentName;
    private bool _iconFailureNoted;

    public KnownGift? Selected { get; private set; }

    public GiftPickerWindow(long currentId, string currentName)
    {
        _currentId = currentId;
        _currentName = currentName;

        Title = "Pilih Gift";
        Width = 540;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1E));
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        var grid = new Grid { Margin = new Thickness(18) };
        for (int i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });

        // Keterangan
        var hintPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        _hint.Foreground = Muted;
        _hint.TextWrapping = TextWrapping.Wrap;
        _note.Foreground = Brushes.Orange;
        _note.FontSize = 11;
        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(0, 4, 0, 0);
        hintPanel.Children.Add(_hint);
        hintPanel.Children.Add(_note);
        Grid.SetRow(hintPanel, 0);
        grid.Children.Add(hintPanel);

        // Pencarian
        var searchPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        searchPanel.Children.Add(new TextBlock { Text = "Cari gift", Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        searchPanel.Children.Add(_search);
        Grid.SetRow(searchPanel, 1);
        grid.Children.Add(searchPanel);
        _search.TextChanged += (s, e) => Populate();

        // Daftar
        _list.Background = new SolidColorBrush(Color.FromRgb(0x13, 0x1A, 0x2E));
        _list.BorderThickness = new Thickness(0);
        Grid.SetRow(_list, 2);
        grid.Children.Add(_list);

        // Tambah manual
        var manual = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        manual.Children.Add(new TextBlock
        {
            Text = "Tidak ada di daftar? Tambah manual",
            Foreground = Muted,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var manualRow = new DockPanel();
        var add = new Button { Content = "+ Tambah", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(add, Dock.Right);
        add.Click += (s, e) => AddManual();

        _manualDiamonds.Width = 90;
        _manualDiamonds.ToolTip = "Harga diamond (boleh dikosongkan)";
        DockPanel.SetDock(_manualDiamonds, Dock.Right);
        _manualDiamonds.Margin = new Thickness(8, 0, 0, 0);

        manualRow.Children.Add(add);
        manualRow.Children.Add(_manualDiamonds);
        manualRow.Children.Add(_manualName);
        manual.Children.Add(manualRow);
        manual.Children.Add(new TextBlock
        {
            Text = "Kolom kiri: nama gift. Kolom kanan: harga diamond (opsional).",
            Foreground = Muted,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(manual, 3);
        grid.Children.Add(manual);

        // Tombol bawah
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Batal", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Pilih", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        ok.Click += (s, e) => Confirm();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 4);
        grid.Children.Add(buttons);

        Content = grid;
        Populate();
    }

    private void Populate(KnownGift? select = null)
    {
        var q = _search.Text.Trim();
        var wantId = select?.Id ?? _currentId;
        var wantName = select?.Name ?? _currentName;

        _list.Items.Clear();
        ListBoxItem? toSelect = null;

        // Urutan: harga diamond dari terkecil ke terbesar; harga yang belum diketahui di paling bawah
        var items = GiftCatalog.Items
            .Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Diamonds <= 0)
            .ThenBy(i => i.Diamonds)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var gift in items)
        {
            var li = MakeItem(gift);
            _list.Items.Add(li);

            bool match = (wantId != 0 && gift.Id == wantId)
                         || (wantId == 0 && string.Equals(gift.Name, wantName, StringComparison.OrdinalIgnoreCase));
            if (match) toSelect = li;
        }

        _hint.Text = GiftCatalog.Items.Count == 0
            ? "Belum ada gift di daftar. Hubungkan ke TikTok saat LIVE: katalog dan gift yang masuk akan muncul di sini. " +
              "Atau tambahkan manual lewat kolom di bawah."
            : "Diurutkan dari diamond terkecil ke terbesar. Klik dua kali untuk memilih cepat.";

        if (toSelect != null)
        {
            toSelect.IsSelected = true;
            _list.ScrollIntoView(toSelect);
        }
    }

    private ListBoxItem MakeItem(KnownGift gift)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Ikon
        var icon = new Image { Width = 38, Height = 38, Stretch = Stretch.Uniform };
        var src = LoadIcon(gift.ImageUrl);
        if (src != null) icon.Source = src;
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // Nama dan keterangan
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = gift.Name,
            Foreground = Brushes.White,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = gift.Detail,
            Foreground = Muted,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Harga diamond
        var price = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        price.Children.Add(new TextBlock
        {
            Text = gift.Diamonds > 0 ? gift.Diamonds.ToString("N0") : "?",
            Foreground = PriceBrush,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        price.Children.Add(new TextBlock
        {
            Text = "diamond",
            Foreground = Muted,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(price, 2);
        grid.Children.Add(price);

        var item = new ListBoxItem { Content = grid, Tag = gift, Padding = new Thickness(10, 6, 12, 6) };
        item.MouseDoubleClick += (s, e) => Confirm();
        return item;
    }

    private ImageSource? LoadIcon(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        if (IconCache.TryGetValue(url, out var cached))
            return cached;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.DecodePixelWidth = 76;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodeFailed += (s, e) => NoteIconFailure();
            bmp.DownloadFailed += (s, e) => NoteIconFailure();
            bmp.EndInit();

            IconCache[url] = bmp;
            return bmp;
        }
        catch
        {
            NoteIconFailure();
            return null;
        }
    }

    private void NoteIconFailure()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_iconFailureNoted) return;
            _iconFailureNoted = true;
            _note.Text = "Sebagian ikon gagal dimuat: kemungkinan format gambarnya (misalnya WebP) " +
                         "tidak didukung, atau tautannya tidak bisa diakses.";
        }));
    }

    private void AddManual()
    {
        var name = _manualName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("Isi nama gift dulu.");
            return;
        }

        long diamonds = 0;
        var d = _manualDiamonds.Text.Trim();
        if (d.Length > 0 && (!long.TryParse(d, out diamonds) || diamonds < 0))
        {
            MessageBox.Show("Harga diamond harus berupa angka, atau kosongkan saja.");
            return;
        }

        var gift = GiftCatalog.AddManual(name, diamonds);
        _manualName.Text = "";
        _manualDiamonds.Text = "";
        _search.Text = "";
        Populate(gift);
    }

    private void Confirm()
    {
        if (_list.SelectedItem is ListBoxItem li && li.Tag is KnownGift gift)
        {
            Selected = gift;
            DialogResult = true;
        }
    }
}