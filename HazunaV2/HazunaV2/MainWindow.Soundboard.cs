using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace HazunaV2;

// Soundboard: tombol buatan sendiri (nama, file, warna, volume, tombol pintas), terpisah dari Media & Sounds.
public partial class MainWindow
{
    private List<SoundboardItem> _board = new();
    private readonly List<SoundChannel> _boardChannels = new();
    private readonly SoundChannel _boardSingle = new();
    private bool _boardReady;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WmHotkey = 0x0312;
    private const int HotkeyBase = 0xB000;
    private HwndSource? _hotkeySource;
    private readonly Dictionary<int, SoundboardItem> _hotkeyMap = new();

    private void InitSoundboard()
    {
        _board = SoundboardStore.Load();
        BoardVolumeSlider.Value = Math.Clamp(_settings.SoundboardVolume, 10, 200);
        BoardVolumeLabel.Text = (int)BoardVolumeSlider.Value + "%";
        BoardOverlapCheck.IsChecked = _settings.SoundboardOverlap;
        _boardSingle.Error += msg => Dispatcher.InvokeAsync(() => AddLog("[Soundboard] " + msg));
        _boardReady = true;

        // Tombol pintas global dipasang setelah jendela punya handle
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero) HookHotkeys();
        else SourceInitialized += (s, e) => HookHotkeys();
    }

    private void SaveBoard()
    {
        SoundboardStore.Save(_board);
        RegisterAllHotkeys();
        BuildSoundboard();
    }

    private void HookHotkeys()
    {
        if (_hotkeySource != null) return;
        _hotkeySource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hotkeySource?.AddHook(HotkeyWndProc);
        RegisterAllHotkeys();
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _hotkeyMap.TryGetValue(wParam.ToInt32(), out var item))
        {
            handled = true;
            PlayBoard(item);
        }
        return IntPtr.Zero;
    }

    private void RegisterAllHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        foreach (var id in _hotkeyMap.Keys) UnregisterHotKey(hwnd, id);
        _hotkeyMap.Clear();

        int n = 0;
        foreach (var m in _board.Where(m => !string.IsNullOrWhiteSpace(m.Hotkey)))
        {
            if (!Hotkeys.Parse(m.Hotkey, out var mods, out var vk)) continue;
            int id = HotkeyBase + n++;
            if (RegisterHotKey(hwnd, id, mods | Hotkeys.NoRepeat, vk))
                _hotkeyMap[id] = m;
            else
                AddLog($"[Soundboard] Tombol {Hotkeys.Pretty(m.Hotkey)} untuk \"{m.Name}\" tidak bisa dipakai (sudah dipakai aplikasi lain atau tombol pintas lain).");
        }
    }

    private void BuildSoundboard()
    {
        if (!_boardReady) return;

        string q = (BoardSearchBox.Text ?? "").Trim().ToLowerInvariant();
        BoardPanel.Children.Clear();

        foreach (var m in _board)
        {
            if (q.Length > 0 && !m.Name.ToLowerInvariant().Contains(q)) continue;

            bool exists = File.Exists(m.FilePath);
            var btn = new Button
            {
                Width = 150,
                Height = 64,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(8, 4, 8, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x37)),
                FontWeight = FontWeights.SemiBold,
                IsEnabled = exists,
                Opacity = exists ? 1 : 0.4,
                ToolTip = exists ? m.Name : m.Name + " (file hilang, klik kanan > Ubah untuk memilih file lagi)",
                Tag = m
            };
            btn.SetResourceReference(Control.BackgroundProperty, m.Color);

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock
            {
                Text = m.Name,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = m.Hotkey.Length > 0 ? 36 : 52
            });
            if (m.Hotkey.Length > 0)
                label.Children.Add(new TextBlock
                {
                    Text = "[" + Hotkeys.Pretty(m.Hotkey) + "]",
                    FontSize = 11,
                    FontWeight = FontWeights.Normal,
                    TextAlignment = TextAlignment.Center
                });
            btn.Content = label;
            btn.Click += BoardButton_Click;

            // Klik kanan tetap berfungsi walau tombolnya nonaktif (file hilang): pasang di pembungkus
            var menu = new ContextMenu();
            MenuItem Item(string header, Action act)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (s, e) => act();
                menu.Items.Add(mi);
                return mi;
            }
            Item("Ubah...", () => EditBoardItem(m));
            Item("Geser ke kiri", () => MoveBoardItem(m, -1));
            Item("Geser ke kanan", () => MoveBoardItem(m, 1));
            menu.Items.Add(new Separator());
            Item("Hapus tombol", () =>
            {
                if (MessageBox.Show(this, $"Hapus tombol \"{m.Name}\"?", "Soundboard", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                _board.Remove(m);
                SaveBoard();
            });

            var host = new Border { Child = btn, ContextMenu = menu, Background = Brushes.Transparent };
            BoardPanel.Children.Add(host);
        }

        BoardHintText.Text = "Klik kiri untuk memutar. Klik kanan sebuah tombol untuk mengubah nama, file, warna, volume, atau tombol pintasnya.";
        BoardEmptyText.Visibility = BoardPanel.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BoardEmptyText.Text = _board.Count == 0
            ? "Belum ada tombol. Klik \"Tambah tombol\" untuk memilih file suara, atau \"Impor dari pustaka\" untuk mengambil dari Media & Sounds."
            : "Tidak ada tombol yang cocok dengan pencarian.";
    }

    private void EditBoardItem(SoundboardItem m)
    {
        // Edit pada salinan supaya "Batal" tidak mengubah apa pun
        var copy = new SoundboardItem { Id = m.Id, Name = m.Name, FilePath = m.FilePath, Hotkey = m.Hotkey, Color = m.Color, Volume = m.Volume };
        var dlg = new SoundboardEditWindow(copy) { Owner = this };
        dlg.ShowDialog();
        if (!dlg.Saved) return;

        // Tombol pintas yang sama tidak boleh dipakai dua tombol: yang lama dilepas
        if (copy.Hotkey.Length > 0)
            foreach (var other in _board.Where(o => o != m && o.Hotkey == copy.Hotkey)) other.Hotkey = "";

        m.Name = copy.Name; m.FilePath = copy.FilePath; m.Hotkey = copy.Hotkey; m.Color = copy.Color; m.Volume = copy.Volume;
        SaveBoard();
    }

    private void MoveBoardItem(SoundboardItem m, int delta)
    {
        int i = _board.IndexOf(m), j = i + delta;
        if (i < 0 || j < 0 || j >= _board.Count) return;
        _board.RemoveAt(i);
        _board.Insert(j, m);
        SaveBoard();
    }

    private void BoardAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pilih file suara untuk tombol soundboard",
            Multiselect = true,
            Filter = "File suara|*.mp3;*.wav;*.wma;*.m4a;*.aac|Semua file|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var added = new List<SoundboardItem>();
        foreach (var f in dlg.FileNames)
        {
            var item = new SoundboardItem
            {
                Name = Path.GetFileNameWithoutExtension(f),
                FilePath = f,
                Color = SoundboardEditWindow.Colors[(_board.Count + added.Count) % SoundboardEditWindow.Colors.Length].Key
            };
            added.Add(item);
        }
        _board.AddRange(added);
        SaveBoard();

        // Satu file saja: langsung buka dialog ubah supaya bisa diberi nama, warna, dan tombol pintas
        if (added.Count == 1) EditBoardItem(added[0]);
    }

    private void BoardImport_Click(object sender, RoutedEventArgs e)
    {
        int n = 0;
        foreach (var m in _library.Where(m => m.Kind == "Suara"))
        {
            if (_board.Any(b => string.Equals(b.FilePath, m.FilePath, StringComparison.OrdinalIgnoreCase))) continue;
            _board.Add(new SoundboardItem
            {
                Name = m.DisplayName,
                FilePath = m.FilePath,
                Color = SoundboardEditWindow.Colors[_board.Count % SoundboardEditWindow.Colors.Length].Key
            });
            n++;
        }
        SaveBoard();
        AddLog($"[Soundboard] {n} suara diimpor dari Media & Sounds.");
    }

    private void BoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SoundboardItem m }) PlayBoard(m);
    }

    private void PlayBoard(SoundboardItem m)
    {
        if (!File.Exists(m.FilePath)) return;
        int vol = Math.Clamp((int)(BoardVolumeSlider.Value * m.Volume / 100.0), 1, 200);

        if (BoardOverlapCheck.IsChecked == true)
        {
            // Tumpuk: tiap klik memakai jalur sendiri; jalur yang sudah selesai dibersihkan
            _boardChannels.RemoveAll(c => !c.IsPlaying);
            var ch = new SoundChannel();
            ch.Error += msg => Dispatcher.InvokeAsync(() => AddLog("[Soundboard] " + msg));
            _boardChannels.Add(ch);
            ch.Play(m.FilePath, vol);
        }
        else
        {
            _boardSingle.Play(m.FilePath, vol);
        }
    }

    private void BoardStop_Click(object sender, RoutedEventArgs e)
    {
        _boardSingle.Stop();
        foreach (var c in _boardChannels) c.Stop();
        _boardChannels.Clear();
    }

    private void BoardSearch_Changed(object sender, TextChangedEventArgs e) => BuildSoundboard();

    private void BoardVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_boardReady) return;
        int v = (int)BoardVolumeSlider.Value;
        BoardVolumeLabel.Text = v + "%";
        _settings.SoundboardVolume = v;
        SettingsStore.Save(_settings);
    }

    private void BoardOverlap_Click(object sender, RoutedEventArgs e)
    {
        _settings.SoundboardOverlap = BoardOverlapCheck.IsChecked == true;
        SettingsStore.Save(_settings);
    }
}
