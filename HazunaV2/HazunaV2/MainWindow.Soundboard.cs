using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace HazunaV2;

// Soundboard: semua item jenis Suara di Media & Sounds jadi tombol yang bisa diklik untuk diputar langsung.
public partial class MainWindow
{
    private readonly List<SoundChannel> _boardChannels = new();
    private readonly SoundChannel _boardSingle = new();
    private static readonly string[] BoardColors = { "AccentBrush", "CyanBrush", "PinkBrush", "GreenBrush", "AmberBrush", "BlueBrush", "RoseBrush" };
    private bool _boardReady;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WmHotkey = 0x0312;
    private const int HotkeyBase = 0xB000;
    private HwndSource? _hotkeySource;
    private readonly Dictionary<int, MediaItem> _hotkeyMap = new();

    private void InitSoundboard()
    {
        BoardVolumeSlider.Value = Math.Clamp(_settings.SoundboardVolume, 1, 200);
        BoardVolumeLabel.Text = _settings.SoundboardVolume + "%";
        BoardOverlapCheck.IsChecked = _settings.SoundboardOverlap;
        _boardSingle.Error += msg => Dispatcher.InvokeAsync(() => AddLog("[Soundboard] " + msg));
        _boardReady = true;

        // Tombol pintas global: dipasang setelah jendela punya handle, dan dipasang ulang saat pustaka berubah
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero) HookHotkeys();
        else SourceInitialized += (s, e) => HookHotkeys();
        AppData.LibraryChanged += () => Dispatcher.InvokeAsync(RegisterAllHotkeys);
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
        foreach (var m in _library.Where(m => m.Kind == "Suara" && !string.IsNullOrWhiteSpace(m.Hotkey)))
        {
            if (!Hotkeys.Parse(m.Hotkey, out var mods, out var vk)) continue;
            int id = HotkeyBase + n++;
            if (RegisterHotKey(hwnd, id, mods | Hotkeys.NoRepeat, vk))
                _hotkeyMap[id] = m;
            else
                AddLog($"[Soundboard] Tombol {Hotkeys.Pretty(m.Hotkey)} untuk \"{m.DisplayName}\" tidak bisa dipakai (sudah dipakai aplikasi lain atau tombol pintas lain).");
        }
    }

    private void SetBoardHotkey(MediaItem m)
    {
        var dlg = new HotkeyCaptureWindow(m.DisplayName, m.Hotkey) { Owner = this };
        dlg.ShowDialog();
        if (!dlg.Saved) return;

        // Tombol yang sama tidak boleh dipakai dua suara: yang lama dilepas
        if (dlg.Result.Length > 0)
            foreach (var other in _library.Where(o => o != m && o.Hotkey == dlg.Result))
                other.Hotkey = "";

        m.Hotkey = dlg.Result;
        LibraryStore.Save(_library);
        RegisterAllHotkeys();
        BuildSoundboard();
    }

    private void BuildSoundboard()
    {
        if (!_boardReady) return;

        string q = (BoardSearchBox.Text ?? "").Trim().ToLowerInvariant();
        BoardPanel.Children.Clear();

        var items = _library.Where(m => m.Kind == "Suara")
            .Where(m => q.Length == 0 || m.DisplayName.ToLowerInvariant().Contains(q))
            .ToList();

        int i = 0;
        foreach (var m in items)
        {
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
                ToolTip = exists ? m.DisplayName : m.DisplayName + " (file hilang)",
                Tag = m,
            };
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock
            {
                Text = m.DisplayName,
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

            var menu = new ContextMenu();
            var setItem = new MenuItem { Header = "Atur tombol pintas..." };
            setItem.Click += (s, e) => SetBoardHotkey(m);
            menu.Items.Add(setItem);
            if (m.Hotkey.Length > 0)
            {
                var clear = new MenuItem { Header = "Hapus tombol pintas" };
                clear.Click += (s, e) => { m.Hotkey = ""; LibraryStore.Save(_library); RegisterAllHotkeys(); BuildSoundboard(); };
                menu.Items.Add(clear);
            }
            btn.ContextMenu = menu;
            btn.SetResourceReference(Control.BackgroundProperty, BoardColors[i++ % BoardColors.Length]);
            btn.Click += BoardButton_Click;
            BoardPanel.Children.Add(btn);
        }

        BoardHintText.Text = "Klik kiri untuk memutar. Klik kanan sebuah tombol untuk mengatur tombol pintas (jalan juga saat game dibuka).";
        BoardEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BoardEmptyText.Text = _library.Any(m => m.Kind == "Suara")
            ? "Tidak ada suara yang cocok dengan pencarian."
            : "Belum ada suara. Tambahkan di Media & Sounds, jenis \"Suara\".";
    }

    private void BoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaItem m }) return;
        PlayBoard(m);
    }

    private void PlayBoard(MediaItem m)
    {
        if (!File.Exists(m.FilePath)) return;
        int vol = (int)BoardVolumeSlider.Value;

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
