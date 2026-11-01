using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace HazunaV2;

public partial class MainWindow
{
    private readonly ObservableCollection<string> _log = new();
    private GiftEngine _engine = null!;
    private ActionQueue _queue = null!;
    private int _giftsProcessed;

    private void InitDashboard()
    {
        LogList.ItemsSource = _log;

        MediaPopup.Failed += msg => AddLog("   ! Media gagal diputar: " + msg);

        _queue = new ActionQueue
        {
            Executor = ExecuteQueuedAsync,
            OnError = msg => AddLog("   ! Kesalahan pada antrean: " + msg)
        };
        _queue.Changed += UpdateQueueText;
        _queue.Start();

        _engine = new GiftEngine(_rules, _board, _queue)
        {
            OnLog = AddLog,
            OnRulesChanged = () => RuleStore.Save(_rules),
            OnRuleFired = r => _menuOverlay?.Pulse(r)
        };

        UpdateQueueText();
        AddLog("Aplikasi siap. Gunakan Simulasi Gift untuk menguji aturan.");

        AddLog($"Daftar gift tersimpan: {GiftCatalog.Items.Count} gift" +
        (GiftCatalog.LoadError != null ? $" (gagal membaca file: {GiftCatalog.LoadError})" : ""));
    }

    // ---------- Satu giliran dalam antrean ----------

    private async Task ExecuteQueuedAsync(QueuedAction a, CancellationToken ct)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0.2, a.DurationSeconds));
        Action? closeMedia = null;
        Task? keyTask = null;

        try
        {
            if (a.StartDelaySeconds > 0)
            {
                AddLog($"   Menunggu {a.StartDelaySeconds:0.#} dtk sebelum mulai. Pindah ke jendela tujuan sekarang.");
                await Task.Delay(TimeSpan.FromSeconds(a.StartDelaySeconds), ct);
            }

            AddLog($"▶ {a.UserName} · {a.GiftName} ({a.Index}/{a.Total}), {a.DurationSeconds:0.#} dtk");

            if (!string.IsNullOrEmpty(a.SoundPath))
            {
                if (File.Exists(a.SoundPath))
                    PlaySound(a.SoundPath);
                else
                    AddLog("   ! File suara tidak ditemukan: " + a.SoundPath);
            }

            if (!string.IsNullOrEmpty(a.MediaPath))
            {
                if (File.Exists(a.MediaPath))
                    closeMedia = MediaPopup.Show(a.MediaPath, duration);
                else
                    AddLog("   ! File media tidak ditemukan: " + a.MediaPath);
            }

            if (!string.IsNullOrWhiteSpace(a.Key))
            {
                // Catat nama pengirim dulu, lalu beri jeda singkat agar mod sempat membacanya
                SpawnName.Write(a);
                await Task.Delay(60, ct);
                keyTask = PressKeyAsync(a, duration, ct);
            }

            // Giliran berlangsung selama durasi, apa pun yang terjadi dengan suara/media
            await Task.Delay(duration, ct);

            if (keyTask != null)
                await keyTask;
        }
        finally
        {
            // Akhir giliran (selesai atau dibatalkan): bersihkan semuanya
            _player.Stop();
            closeMedia?.Invoke();
        }
    }

    private async Task PressKeyAsync(QueuedAction a, TimeSpan duration, CancellationToken ct)
    {
        if (KeyEnableCheck.IsChecked != true)
        {
            AddLog($"   ! Aksi keyboard NONAKTIF, tombol {a.Key} tidak dikirim.");
            return;
        }

        if (!KeySender.TryParse(a.Key, out _, out var error))
        {
            AddLog($"   ! Tombol \"{a.Key}\" tidak valid: {error}");
            return;
        }

        try
        {
            var tap = TimeSpan.FromMilliseconds(a.TapHoldMs);
            var hold = a.HoldKey ? duration : (tap < duration ? tap : duration);
            await KeySender.HoldAsync(a.Key, hold, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddLog("   ! Gagal mengirim tombol: " + ex.Message);
        }
    }

    // ---------- Status antrean ----------

    private void UpdateQueueText()
    {
        var cur = _queue.Current;
        var now = cur == null
            ? "Tidak ada yang berjalan"
            : $"Berjalan: {cur.UserName} · {cur.GiftName} ({cur.Index}/{cur.Total})";

        var waiting = _queue.Pending == 0
            ? "antrean kosong"
            : $"{_queue.Pending} menunggu (≈ {ActionQueue.FormatSeconds(_queue.PendingSeconds)})";

        QueueText.Text = $"{now}   |   {waiting}";
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        int dropped = _queue.Clear();
        _player.Stop();
        AddLog($"Dihentikan: {dropped} giliran dibatalkan, giliran yang berjalan dihentikan.");
    }

    // ---------- Log dan ringkasan ----------

    private void AddLog(string text)
    {
        _log.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
        while (_log.Count > 200)
            _log.RemoveAt(0);
        LogList.ScrollIntoView(_log[_log.Count - 1]);
    }

    private void RefreshDashboard()
    {
        int aktif = _rules.Count(r => r.Enabled);
        SummaryText.Text =
            $"Aturan aktif: {aktif} dari {_rules.Count}    |    " +
            $"File di pustaka: {_library.Count}    |    " +
            $"Gift diproses sesi ini: {_giftsProcessed}    |    " +
            LiveStatusShort();

            var names = _rules
            .Select(r => r.GiftName.Trim())
            .Concat(GiftCatalog.Items.Select(i => i.Name.Trim()))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var current = GiftCombo.Text;
        GiftCombo.ItemsSource = names;
        GiftCombo.Text = current;
    }

    private void SendTestGift_Click(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        var gift = GiftCombo.Text.Trim();

        if (user.Length == 0 || gift.Length == 0)
        {
            MessageBox.Show("Isi nama penonton dan nama gift dulu.");
            return;
        }
        if (!int.TryParse(CountBox.Text, out var count) || count < 1)
        {
            MessageBox.Show("Jumlah harus berupa angka 1 atau lebih.");
            return;
        }
        if (!long.TryParse(DiamondBox.Text, out var diamonds) || diamonds < 0)
        {
            MessageBox.Show("Diamond per gift harus berupa angka 0 atau lebih.");
            return;
        }

        var delay = _settings.SimDelaySeconds;

        var known = GiftCatalog.FindByName(gift);
        _engine.Process(new GiftEvent(user, gift, count, diamonds, delay, known?.Id ?? 0));
        _giftsProcessed += count;
        RefreshDashboard();
    }

    // ---------- Simulasi Tap-tap / Follow / Share ----------

    private bool TryReadSimUser(out string user)
    {
        user = UserBox.Text.Trim();
        if (user.Length == 0)
        {
            MessageBox.Show("Isi nama penonton dulu.");
            return false;
        }
        return true;
    }

    private void SendTestTap_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSimUser(out var user)) return;
        if (!int.TryParse(SimTapBox.Text.Trim(), out var taps) || taps < 1)
        {
            MessageBox.Show("Jumlah tap harus berupa angka 1 atau lebih.");
            return;
        }

        _engine.ProcessSocial(RuleTrigger.Like, user, taps, _settings.SimDelaySeconds);
    }

    private void SendTestFollow_Click(object sender, RoutedEventArgs e)
    {
        if (TryReadSimUser(out var user))
            _engine.ProcessSocial(RuleTrigger.Follow, user, 1, _settings.SimDelaySeconds);
    }

    private void SendTestShare_Click(object sender, RoutedEventArgs e)
    {
        if (TryReadSimUser(out var user))
            _engine.ProcessSocial(RuleTrigger.Share, user, 1, _settings.SimDelaySeconds);
    }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _log));
            AddLog("Log disalin ke clipboard. Tempel dengan Ctrl+V.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyalin log: " + ex.Message);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();
}