using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HazunaV2;

public partial class MainWindow
{
    // Keadaan jalan tiap wheel (tidak disimpan ke file)
    private sealed class WheelRuntime
    {
        public readonly Queue<string> Queue = new();
    }

    private WheelSet _wheelSet = new();
    private WheelConfig? _curWheel;
    private readonly Dictionary<WheelConfig, WheelRuntime> _wheelRt = new();
    private bool _wheelReady;

    private WheelRuntime Rt(WheelConfig w)
    {
        if (!_wheelRt.TryGetValue(w, out var rt))
            _wheelRt[w] = rt = new WheelRuntime();
        return rt;
    }

    private void InitWheel()
    {
        _wheelSet = WheelStore.Load();
        WheelList.ItemsSource = _wheelSet.Wheels;
        _wheelReady = true;
        WheelList.SelectedIndex = 0;

        // Satu jendela overlay untuk semua roda: dibuat sekarang (siaga, tak terlihat) supaya bisa dipilih di TikTok Studio
        Dispatcher.InvokeAsync(() =>
        {
            EnsureWheelWindow();
            SettleWheel();
            EnsureAllOverlays();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void SaveWheel()
    {
        WheelStore.Save(_wheelSet);
        _menuOverlay?.Refresh();
    }

    // Kartu Lucky Wheel di overlay menu gift: satu aturan bayangan per roda (tidak disimpan)
    private readonly Dictionary<WheelConfig, GiftRule> _wheelCards = new();

    private IEnumerable<GiftRule> WheelMenuRules()
    {
        var result = new List<GiftRule>();
        foreach (var w in _wheelSet.Wheels)
        {
            if (w.GiftId == 0 && string.IsNullOrWhiteSpace(w.GiftName)) continue;

            if (!_wheelCards.TryGetValue(w, out var r))
                _wheelCards[w] = r = new GiftRule();

            r.Enabled = true;
            r.Trigger = RuleTrigger.Gift;
            r.GiftId = w.GiftId;
            r.GiftName = w.GiftName;
            r.EffectName = "🎡 " + (string.IsNullOrWhiteSpace(w.Name) ? "Lucky Wheel" : w.Name.Trim());
            result.Add(r);
        }
        return result;
    }

    // ---------- Daftar wheel ----------

    private void WheelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_wheelReady) return;

        _curWheel = WheelList.SelectedItem as WheelConfig;
        bool has = _curWheel != null;
        WheelGrid.ItemsSource = _curWheel?.Entries;

        if (!has) return;
        var w = _curWheel!;
        WheelNameBox.Text = w.Name;
        WheelGiftText.Text = w.GiftDisplay;
        WheelPerGiftCheck.IsChecked = w.SpinPerGift;
        WheelShuffleCheck.IsChecked = w.ShuffleEachSpin;
        WheelSecondsBox.Text = w.SpinSeconds.ToString("0.#");
        WheelActionBox.Text = w.ActionSeconds.ToString("0.#");
        WheelAlwaysCheck.IsChecked = w.AlwaysShow;
        ShowWheelSounds(w);
        UpdateWheelOverlayBtn();
    }

    private void WheelNew_Click(object sender, RoutedEventArgs e)
    {
        var w = WheelConfig.CreateDefault("Wheel " + (_wheelSet.Wheels.Count + 1));
        _wheelSet.Wheels.Add(w);
        WheelList.SelectedItem = w;
        SaveWheel();
    }

    private void WheelDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;

        var src = _curWheel;
        var copy = new WheelConfig
        {
            Name = src.Name + " (salinan)",
            GiftId = src.GiftId,
            GiftName = src.GiftName,
            SpinPerGift = src.SpinPerGift,
            SpinSeconds = src.SpinSeconds,
            ActionSeconds = src.ActionSeconds,
            ShuffleEachSpin = src.ShuffleEachSpin,
            SpinSound = src.SpinSound,
            SpinSoundVolume = src.SpinSoundVolume,
            ResultSound = src.ResultSound,
            ResultSoundVolume = src.ResultSoundVolume
        };
        foreach (var en in src.Entries)
            copy.Entries.Add(new WheelEntry { Text = en.Text, Weight = en.Weight, Key = en.Key, ScoreDelta = en.ScoreDelta });

        _wheelSet.Wheels.Add(copy);
        WheelList.SelectedItem = copy;
        SaveWheel();
    }

    private void WheelRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;
        if (_wheelSet.Wheels.Count <= 1)
        {
            MessageBox.Show("Minimal harus ada satu wheel.");
            return;
        }
        if (MessageBox.Show($"Hapus wheel \"{_curWheel.Name}\" beserta isinya?", "Hapus wheel",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var gone = _curWheel;
        int idx = _wheelSet.Wheels.IndexOf(gone);

        if (_wheelRt.TryGetValue(gone, out var rt))
        {
            rt.Queue.Clear();
            _wheelRt.Remove(gone);
        }

        _wheelSet.Wheels.Remove(gone);
        if (_wheelOnScreen == gone)
        {
            _wheelOnScreen = null;
            var next = _wheelSet.Wheels.FirstOrDefault();
            if (next != null && _wheelWin != null && !_wheelBusy)
            {
                _wheelWin.SetWheel(next);
                _wheelOnScreen = next;
            }
        }
        WheelList.SelectedIndex = Math.Min(idx, _wheelSet.Wheels.Count - 1);
        SaveWheel();
    }

    // ---------- Pengaturan wheel terpilih ----------

    private void WheelPickGift_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;

        var dlg = new GiftPickerWindow(_curWheel.GiftId, _curWheel.GiftName) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected != null)
        {
            _curWheel.GiftId = dlg.Selected.Id;
            _curWheel.GiftName = dlg.Selected.Name;
            WheelGiftText.Text = _curWheel.GiftDisplay;
            SaveWheel();
            RebuildWheelOverlay(_curWheel);
        }
    }

    private void WheelOption_Click(object sender, RoutedEventArgs e) => CommitWheelSettings();
    private void WheelField_LostFocus(object sender, RoutedEventArgs e) => CommitWheelSettings();

    private void CommitWheelSettings()
    {
        if (!_wheelReady || _curWheel == null) return;
        var w = _curWheel;

        w.Name = WheelNameBox.Text;
        WheelNameBox.Text = w.Name;
        w.SpinPerGift = WheelPerGiftCheck.IsChecked == true;
        w.ShuffleEachSpin = WheelShuffleCheck.IsChecked == true;

        if (double.TryParse(WheelSecondsBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var spin))
            w.SpinSeconds = Math.Clamp(spin, 2, 30);
        WheelSecondsBox.Text = w.SpinSeconds.ToString("0.#");

        if (double.TryParse(WheelActionBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var act))
            w.ActionSeconds = Math.Clamp(act, 0.2, 60);
        WheelActionBox.Text = w.ActionSeconds.ToString("0.#");

        if (int.TryParse(WheelSpinVolBox.Text, out var sv)) w.SpinSoundVolume = Math.Clamp(sv, 1, 200);
        if (int.TryParse(WheelResultVolBox.Text, out var rv)) w.ResultSoundVolume = Math.Clamp(rv, 1, 200);
        ShowWheelSounds(w);

        bool always = WheelAlwaysCheck.IsChecked == true;
        bool changedAlways = always != w.AlwaysShow;
        w.AlwaysShow = always;

        SaveWheel();
        RebuildWheelOverlay(w);

        if (changedAlways) SettleWheel();
    }

    // ---------- Suara roda ----------

    private readonly SoundChannel _wheelSpinSound = new();
    private readonly SoundChannel _wheelResultSound = new();

    private void ShowWheelSounds(WheelConfig w)
    {
        WheelSpinSoundText.Text = string.IsNullOrWhiteSpace(w.SpinSound) ? "(tanpa suara)" : System.IO.Path.GetFileName(w.SpinSound);
        WheelResultSoundText.Text = string.IsNullOrWhiteSpace(w.ResultSound) ? "(tanpa suara)" : System.IO.Path.GetFileName(w.ResultSound);
        WheelSpinVolBox.Text = w.SpinSoundVolume.ToString();
        WheelResultVolBox.Text = w.ResultSoundVolume.ToString();
    }

    private void WheelPickSound_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pilih suara",
            Filter = "Audio|*.mp3;*.wav;*.ogg;*.m4a;*.wma;*.aac;*.flac|Semua file|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        if ((sender as FrameworkElement)?.Tag as string == "spin") _curWheel.SpinSound = dlg.FileName;
        else _curWheel.ResultSound = dlg.FileName;

        SaveWheel();
        ShowWheelSounds(_curWheel);
    }

    private void WheelClearSound_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;

        if ((sender as FrameworkElement)?.Tag as string == "spin") _curWheel.SpinSound = "";
        else _curWheel.ResultSound = "";

        SaveWheel();
        ShowWheelSounds(_curWheel);
    }

    // ---------- Isi roda ----------

    private void WheelAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;
        var entry = new WheelEntry { Text = "Isi " + (_curWheel.Entries.Count + 1) };
        _curWheel.Entries.Add(entry);
        WheelGrid.SelectedItem = entry;
        WheelGrid.ScrollIntoView(entry);
        SaveWheel();
        RebuildWheelOverlay(_curWheel);
    }

    // Mengacak urutan isi di daftar (dan di roda)
    private void WheelShuffle_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null || _curWheel.Entries.Count < 2) return;

        var list = _curWheel.Entries;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            if (i != j) list.Move(i, j);
        }
        SaveWheel();
        RebuildWheelOverlay(_curWheel);
    }

    private void WheelDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null || WheelGrid.SelectedItem is not WheelEntry entry) return;
        _curWheel.Entries.Remove(entry);
        SaveWheel();
        RebuildWheelOverlay(_curWheel);
    }

    private void WheelPickKey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WheelEntry entry) return;

        var dlg = new KeyPickerWindow(entry.Key) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            entry.Key = dlg.Result;
            WheelGrid.Items.Refresh();
            SaveWheel();
        }
    }

    private void WheelGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Simpan dan gambar ulang setelah nilai sel benar-benar masuk ke data
        Dispatcher.InvokeAsync(() =>
        {
            SaveWheel();
            if (_curWheel != null) RebuildWheelOverlay(_curWheel);
        }, DispatcherPriority.Background);
    }

    // ---------- Overlay (satu jendela untuk semua roda) ----------

    private WheelOverlayWindow? _wheelWin;
    private WheelConfig? _wheelOnScreen;                    // roda yang sedang dipasang di jendela
    private bool _wheelBusy;                                // ada putaran yang sedang berjalan
    private bool _wheelPreview;                             // dibuka tombol "Tampilkan / atur posisi"
    private (WheelConfig W, string User, WheelEntry Entry)? _wheelPending;   // hasil yang belum diterapkan

    private void RebuildWheelOverlay(WheelConfig w)
    {
        if (_wheelWin != null && _wheelOnScreen == w && !_wheelWin.IsSpinning)
            _wheelWin.Rebuild();
    }

    private WheelOverlayWindow EnsureWheelWindow()
    {
        if (_wheelWin != null) return _wheelWin;

        var first = _wheelOnScreen ?? _curWheel ?? _wheelSet.Wheels.First();
        var win = new WheelOverlayWindow(first, _wheelSet.PosX, _wheelSet.PosY, startHidden: true);
        win.SetChromaKey(ChromaCheck.IsChecked == true);
        _wheelWin = win;
        _wheelOnScreen = first;

        win.Closed += (s, args) =>
        {
            _wheelSet.PosX = win.Left;
            _wheelSet.PosY = win.Top;
            WheelStore.Save(_wheelSet);

            _wheelWin = null;
            _wheelPreview = false;

            // Ditutup di tengah putaran: hasilnya tetap diterapkan, lalu lanjut ke antrean berikutnya
            if (_wheelBusy)
            {
                _wheelBusy = false;
                if (_wheelPending is { } p)
                {
                    _wheelPending = null;
                    ApplyWheelResult(p.W, p.User, p.Entry);
                }
                PumpWheel();
            }
            UpdateWheelOverlayBtn();
        };

        win.Show();
        return win;
    }

    private void ShowWheelOnScreen(WheelConfig w)
    {
        var win = EnsureWheelWindow();
        if (_wheelOnScreen != w)
        {
            win.SetWheel(w);
            _wheelOnScreen = w;
        }
    }

    // Keadaan diam: tidak ada putaran dan tidak ada antrean. Roda "selalu tampil" tetap terlihat, selain itu disembunyikan.
    private void SettleWheel()
    {
        if (_wheelBusy || _wheelPreview) return;
        if (_wheelRt.Values.Any(r => r.Queue.Count > 0)) return;

        var win = EnsureWheelWindow();
        var always = _wheelSet.Wheels.FirstOrDefault(x => x.AlwaysShow);
        if (always != null)
        {
            ShowWheelOnScreen(always);
            win.Reveal();
        }
        else
        {
            win.Conceal();
        }
        UpdateWheelOverlayBtn();
    }

    private void ToggleWheelOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;

        if (_wheelPreview && _wheelOnScreen == _curWheel)
        {
            _wheelPreview = false;
            SettleWheel();
        }
        else
        {
            if (_wheelBusy) return;
            _wheelPreview = true;
            ShowWheelOnScreen(_curWheel);
            _wheelWin!.Reveal();
        }
        UpdateWheelOverlayBtn();
    }

    private void UpdateWheelOverlayBtn()
    {
        if (_curWheel == null) return;
        WheelOverlayBtn.Content = _wheelPreview && _wheelOnScreen == _curWheel ? "Sembunyikan overlay" : "Tampilkan / atur posisi";
    }

    private void SetWheelChroma(bool on) => _wheelWin?.SetChromaKey(on);

    private void CloseAllWheelOverlays()
    {
        foreach (var rt in _wheelRt.Values)
            rt.Queue.Clear();
        _wheelBusy = false;
        _wheelSpinSound.Stop();
        _wheelResultSound.Stop();
        _wheelWin?.Close();
    }

    // ---------- Pemicu dan putaran ----------

    private void OnWheelGift(GiftEvent g)
    {
        foreach (var w in _wheelSet.Wheels.ToList())
        {
            if (w.GiftId == 0 && string.IsNullOrWhiteSpace(w.GiftName)) continue;

            bool match = (w.GiftId != 0 && g.GiftId != 0)
                ? w.GiftId == g.GiftId
                : string.Equals(w.GiftName.Trim(), g.GiftName.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!match) continue;

            if (_wheelCards.TryGetValue(w, out var card)) _menuOverlay?.Pulse(card);

            int spins = w.SpinPerGift ? Math.Clamp(g.Count, 1, 10) : 1;
            for (int i = 0; i < spins; i++)
                EnqueueWheelSpin(w, g.UserName);
        }
    }

    private void WheelTest_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;
        if (_curWheel.Entries.Count == 0)
        {
            MessageBox.Show("Isi roda masih kosong. Tambah isi dulu.");
            return;
        }
        EnqueueWheelSpin(_curWheel, "Penguji");
    }

    private void EnqueueWheelSpin(WheelConfig w, string user)
    {
        if (w.Entries.Count == 0) return;

        var rt = Rt(w);
        if (rt.Queue.Count >= 30) return;

        rt.Queue.Enqueue(user);
        PumpWheel();
    }

    private static int PickWheelIndex(WheelConfig w)
    {
        int total = w.Entries.Sum(x => Math.Max(1, x.Weight));
        int roll = Random.Shared.Next(total);
        for (int i = 0; i < w.Entries.Count; i++)
        {
            roll -= Math.Max(1, w.Entries[i].Weight);
            if (roll < 0) return i;
        }
        return w.Entries.Count - 1;
    }

    // Memutar satu per satu. Roda yang sedang terpasang dilanjutkan dulu, lalu roda lain yang antre.
    private void PumpWheel()
    {
        if (_wheelBusy) return;

        var w = _wheelSet.Wheels.FirstOrDefault(x => x == _wheelOnScreen && Rt(x).Queue.Count > 0)
                ?? _wheelSet.Wheels.FirstOrDefault(x => Rt(x).Queue.Count > 0);
        if (w == null) return;

        var rt = Rt(w);
        if (w.Entries.Count == 0) { rt.Queue.Clear(); PumpWheel(); return; }

        string user = rt.Queue.Dequeue();
        int index = PickWheelIndex(w);
        var entry = w.Entries[index];

        // Pasang roda yang dimaksud dan munculkan dulu kalau sedang disembunyikan
        ShowWheelOnScreen(w);
        var overlay = _wheelWin!;
        overlay.Reveal();
        UpdateWheelOverlayBtn();

        _wheelBusy = true;
        _wheelPending = (w, user, entry);

        // Jeda singkat supaya roda sempat muncul sebelum berputar
        var start = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        start.Tick += (s, e) =>
        {
            start.Stop();
            if (_wheelWin != overlay || !_wheelBusy) return;     // ditutup selagi menunggu

            if (w.ShuffleEachSpin)
                overlay.Shuffle();

            _wheelResultSound.Stop();
            _wheelSpinSound.Play(w.SpinSound, w.SpinSoundVolume);

            overlay.Spin(index, w.SpinSeconds, user, () =>
            {
                _wheelPending = null;
                _wheelSpinSound.Stop();
                _wheelResultSound.Play(w.ResultSound, w.ResultSoundVolume);
                overlay.ShowResult(user, entry.Text, entry.ScoreDelta);
                ApplyWheelResult(w, user, entry);

                // Biarkan hasil terbaca sebentar sebelum putaran berikutnya atau menyembunyikan roda
                var hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                hold.Tick += (s2, e2) =>
                {
                    hold.Stop();
                    if (_wheelWin != overlay) return;          // sudah ditutup

                    _wheelBusy = false;
                    if (_wheelRt.Values.Any(r => r.Queue.Count > 0))
                    {
                        PumpWheel();
                    }
                    else
                    {
                        overlay.ClearResult();
                        SettleWheel();
                    }
                };
                hold.Start();
            });
        };
        start.Start();
    }

    private void ApplyWheelResult(WheelConfig w, string user, WheelEntry entry)
    {
        AddLog($"🎡 {w.Name}: {user} mendapat \"{entry.Text}\"" + (entry.ScoreDelta != 0 ? $" (skor WIN {entry.ScoreDelta:+0;-0})" : ""));

        _sum.AddWheel(w.Name, entry.Text);
        NoteGameEvent($"Roda \"{w.Name}\": {user} mendapat \"{entry.Text}\"", GameEventLevel.High);

        // Hadiah skor: langsung masuk ke nilai WIN (skor boleh minus, maksimal target)
        if (entry.ScoreDelta != 0)
            _score.Add(entry.ScoreDelta);

        if (string.IsNullOrWhiteSpace(entry.Key)) return;

        _queue.EnqueueRange(new[]
        {
            new QueuedAction(user, w.Name, 1, 1, entry.Key, 100, false, "", "",
                             w.ActionSeconds, 0, 100, entry.Text)
        });
    }
}
