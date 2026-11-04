using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HazunaV2;

public partial class MainWindow
{
    private WheelConfig _wheel = new();
    private WheelOverlayWindow? _wheelOverlay;
    private readonly Queue<string> _wheelQueue = new();
    private bool _wheelBusy;
    private bool _wheelReady;

    private void InitWheel()
    {
        _wheel = WheelStore.Load();
        WheelGrid.ItemsSource = _wheel.Entries;

        WheelGiftText.Text = _wheel.GiftDisplay;
        WheelPerGiftCheck.IsChecked = _wheel.SpinPerGift;
        WheelSecondsBox.Text = _wheel.SpinSeconds.ToString("0.#");
        WheelActionBox.Text = _wheel.ActionSeconds.ToString("0.#");
        WheelAutoOpenCheck.IsChecked = _wheel.AutoOpenOverlay;
        _wheelReady = true;

        if (_wheel.AutoOpenOverlay)
            Dispatcher.InvokeAsync(() => { if (_wheelOverlay == null) ToggleWheelOverlay_Click(this, new RoutedEventArgs()); },
                DispatcherPriority.ApplicationIdle);
    }

    private void SaveWheel() => WheelStore.Save(_wheel);

    // ---------- Pengaturan ----------

    private void WheelPickGift_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new GiftPickerWindow(_wheel.GiftId, _wheel.GiftName) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected != null)
        {
            _wheel.GiftId = dlg.Selected.Id;
            _wheel.GiftName = dlg.Selected.Name;
            WheelGiftText.Text = _wheel.GiftDisplay;
            SaveWheel();
            _wheelOverlay?.Rebuild();
        }
    }

    private void WheelOption_Click(object sender, RoutedEventArgs e) => CommitWheelSettings();
    private void WheelField_LostFocus(object sender, RoutedEventArgs e) => CommitWheelSettings();

    private void CommitWheelSettings()
    {
        if (!_wheelReady) return;

        _wheel.SpinPerGift = WheelPerGiftCheck.IsChecked == true;
        _wheel.AutoOpenOverlay = WheelAutoOpenCheck.IsChecked == true;

        if (double.TryParse(WheelSecondsBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var spin))
            _wheel.SpinSeconds = Math.Clamp(spin, 2, 30);
        WheelSecondsBox.Text = _wheel.SpinSeconds.ToString("0.#");

        if (double.TryParse(WheelActionBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var act))
            _wheel.ActionSeconds = Math.Clamp(act, 0.2, 60);
        WheelActionBox.Text = _wheel.ActionSeconds.ToString("0.#");

        SaveWheel();
    }

    // ---------- Isi roda ----------

    private void WheelAdd_Click(object sender, RoutedEventArgs e)
    {
        var entry = new WheelEntry { Text = "Isi " + (_wheel.Entries.Count + 1) };
        _wheel.Entries.Add(entry);
        WheelGrid.SelectedItem = entry;
        WheelGrid.ScrollIntoView(entry);
        SaveWheel();
        _wheelOverlay?.Rebuild();
    }

    private void WheelDelete_Click(object sender, RoutedEventArgs e)
    {
        if (WheelGrid.SelectedItem is not WheelEntry entry) return;
        _wheel.Entries.Remove(entry);
        SaveWheel();
        _wheelOverlay?.Rebuild();
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
            _wheelOverlay?.Rebuild();
        }, DispatcherPriority.Background);
    }

    // ---------- Overlay ----------

    private void ToggleWheelOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_wheelOverlay != null)
        {
            _wheelOverlay.Close();
            return;
        }

        _wheelOverlay = new WheelOverlayWindow(_wheel);
        _wheelOverlay.SetChromaKey(ChromaCheck.IsChecked == true);
        _wheelOverlay.Closed += (s, args) =>
        {
            _wheelOverlay = null;
            WheelOverlayBtn.Content = "Buka Overlay Roda";
            UpdateAllOverlayBtn();
        };
        _wheelOverlay.Show();
        WheelOverlayBtn.Content = "Tutup Overlay Roda";
        UpdateAllOverlayBtn();
    }

    // ---------- Pemicu dan putaran ----------

    private void OnWheelGift(GiftEvent g)
    {
        if (_wheel.GiftId == 0 && string.IsNullOrWhiteSpace(_wheel.GiftName)) return;

        bool match = (_wheel.GiftId != 0 && g.GiftId != 0)
            ? _wheel.GiftId == g.GiftId
            : string.Equals(_wheel.GiftName.Trim(), g.GiftName.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!match) return;

        int spins = _wheel.SpinPerGift ? Math.Clamp(g.Count, 1, 10) : 1;
        for (int i = 0; i < spins; i++)
            EnqueueWheelSpin(g.UserName);
    }

    private void WheelTest_Click(object sender, RoutedEventArgs e)
    {
        if (_wheel.Entries.Count == 0)
        {
            MessageBox.Show("Isi roda masih kosong. Tambah isi dulu.");
            return;
        }
        if (_wheelOverlay == null)
            ToggleWheelOverlay_Click(sender, e);
        EnqueueWheelSpin("Penguji");
    }

    private void EnqueueWheelSpin(string user)
    {
        if (_wheel.Entries.Count == 0) return;
        if (_wheelQueue.Count >= 30) return;

        _wheelQueue.Enqueue(user);
        PumpWheel();
    }

    private int PickWheelIndex()
    {
        int total = _wheel.Entries.Sum(x => Math.Max(1, x.Weight));
        int roll = Random.Shared.Next(total);
        for (int i = 0; i < _wheel.Entries.Count; i++)
        {
            roll -= Math.Max(1, _wheel.Entries[i].Weight);
            if (roll < 0) return i;
        }
        return _wheel.Entries.Count - 1;
    }

    private void PumpWheel()
    {
        if (_wheelBusy || _wheelQueue.Count == 0) return;
        if (_wheel.Entries.Count == 0) { _wheelQueue.Clear(); return; }

        string user = _wheelQueue.Dequeue();
        int index = PickWheelIndex();
        var entry = _wheel.Entries[index];

        // Overlay tertutup: hasil langsung diterapkan tanpa animasi
        if (_wheelOverlay == null)
        {
            ApplyWheelResult(user, entry);
            PumpWheel();
            return;
        }

        _wheelBusy = true;
        var overlay = _wheelOverlay;
        overlay.Spin(index, _wheel.SpinSeconds, user, () =>
        {
            overlay.ShowResult(user, entry.Text, !string.IsNullOrWhiteSpace(entry.Key));
            ApplyWheelResult(user, entry);

            // Biarkan hasil terbaca sebentar sebelum putaran berikutnya
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (_wheelQueue.Count == 0) overlay.ClearResult();
                _wheelBusy = false;
                PumpWheel();
            };
            timer.Start();
        });
    }

    private void ApplyWheelResult(string user, WheelEntry entry)
    {
        AddLog($"🎡 Lucky Wheel: {user} mendapat \"{entry.Text}\"");

        if (string.IsNullOrWhiteSpace(entry.Key)) return;

        _queue.EnqueueRange(new[]
        {
            new QueuedAction(user, "Lucky Wheel", 1, 1, entry.Key, 100, false, "", "",
                             _wheel.ActionSeconds, 0, 100, entry.Text)
        });
    }
}
