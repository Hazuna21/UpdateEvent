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
        public bool Busy;
        public bool Preview;                                // dibuka tombol "Tampilkan / atur posisi"
        public WheelOverlayWindow? Overlay;
        public (string User, WheelEntry Entry)? Pending;    // hasil yang belum diterapkan (putaran sedang berjalan)
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

        // Wheel yang diatur "selalu tampil" dibuka setelah jendela utama siap
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var w in _wheelSet.Wheels.Where(x => x.AlwaysShow))
                ShowWheelOverlay(w);
        }, DispatcherPriority.ApplicationIdle);
    }

    private void SaveWheel() => WheelStore.Save(_wheelSet);

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
            ShuffleEachSpin = src.ShuffleEachSpin
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
            rt.Overlay?.Close();
            _wheelRt.Remove(gone);
        }

        _wheelSet.Wheels.Remove(gone);
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

        bool always = WheelAlwaysCheck.IsChecked == true;
        bool changedAlways = always != w.AlwaysShow;
        w.AlwaysShow = always;

        SaveWheel();
        RebuildWheelOverlay(w);

        if (changedAlways)
        {
            var rt = Rt(w);
            if (always) ShowWheelOverlay(w);
            else if (!rt.Preview && !rt.Busy && rt.Queue.Count == 0) rt.Overlay?.FadeOutAndClose();
        }
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

    // ---------- Overlay ----------

    private void RebuildWheelOverlay(WheelConfig w)
    {
        if (_wheelRt.TryGetValue(w, out var rt) && rt.Overlay != null && !rt.Overlay.IsSpinning)
            rt.Overlay.Rebuild();
    }

    private void ShowWheelOverlay(WheelConfig w)
    {
        var rt = Rt(w);
        if (rt.Overlay != null) return;

        var overlay = new WheelOverlayWindow(w);
        overlay.SetChromaKey(ChromaCheck.IsChecked == true);
        rt.Overlay = overlay;

        overlay.Closed += (s, args) =>
        {
            rt.Overlay = null;
            rt.Preview = false;

            // Ditutup di tengah putaran: hasilnya tetap diterapkan, lalu lanjut ke antrean berikutnya
            if (rt.Busy)
            {
                rt.Busy = false;
                if (rt.Pending is { } p)
                {
                    rt.Pending = null;
                    ApplyWheelResult(w, p.User, p.Entry);
                }
                PumpWheel(w);
            }

            if (_curWheel == w) UpdateWheelOverlayBtn();
        };

        overlay.Show();
        if (_curWheel == w) UpdateWheelOverlayBtn();
    }

    private void ToggleWheelOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_curWheel == null) return;
        var rt = Rt(_curWheel);

        if (rt.Overlay != null)
        {
            rt.Preview = false;
            rt.Overlay.Close();
            return;
        }

        rt.Preview = true;
        ShowWheelOverlay(_curWheel);
        UpdateWheelOverlayBtn();
    }

    private void UpdateWheelOverlayBtn()
    {
        if (_curWheel == null) return;
        WheelOverlayBtn.Content = Rt(_curWheel).Overlay != null ? "Sembunyikan overlay" : "Tampilkan / atur posisi";
    }

    private void SetWheelChroma(bool on)
    {
        foreach (var rt in _wheelRt.Values)
            rt.Overlay?.SetChromaKey(on);
    }

    private void CloseAllWheelOverlays()
    {
        foreach (var rt in _wheelRt.Values.ToList())
        {
            rt.Queue.Clear();
            rt.Busy = false;
            rt.Overlay?.Close();
        }
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
        PumpWheel(w);
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

    private void PumpWheel(WheelConfig w)
    {
        var rt = Rt(w);
        if (rt.Busy || rt.Queue.Count == 0) return;
        if (w.Entries.Count == 0) { rt.Queue.Clear(); return; }

        string user = rt.Queue.Dequeue();
        int index = PickWheelIndex(w);
        var entry = w.Entries[index];

        // Roda muncul dulu kalau sedang disembunyikan
        ShowWheelOverlay(w);
        var overlay = rt.Overlay;
        if (overlay == null)
        {
            ApplyWheelResult(w, user, entry);
            PumpWheel(w);
            return;
        }

        rt.Busy = true;
        rt.Pending = (user, entry);

        // Jeda singkat supaya roda sempat muncul sebelum berputar
        var start = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        start.Tick += (s, e) =>
        {
            start.Stop();
            if (rt.Overlay != overlay || !rt.Busy) return;     // ditutup selagi menunggu

            if (w.ShuffleEachSpin)
                overlay.Shuffle();

            overlay.Spin(index, w.SpinSeconds, user, () =>
            {
                rt.Pending = null;
                overlay.ShowResult(user, entry.Text, entry.ScoreDelta);
                ApplyWheelResult(w, user, entry);

                // Biarkan hasil terbaca sebentar sebelum putaran berikutnya atau menyembunyikan roda
                var hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                hold.Tick += (s2, e2) =>
                {
                    hold.Stop();
                    if (rt.Overlay != overlay) return;          // sudah ditutup

                    rt.Busy = false;
                    if (rt.Queue.Count > 0)
                    {
                        PumpWheel(w);
                    }
                    else
                    {
                        overlay.ClearResult();
                        if (!w.AlwaysShow && !rt.Preview)
                            overlay.FadeOutAndClose();
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

        NoteGameEvent($"Roda \"{w.Name}\": {user} mendapat \"{entry.Text}\"", GameEventLevel.High);

        // Hadiah skor: langsung masuk ke nilai WIN (skor tetap di antara 0 dan target)
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
