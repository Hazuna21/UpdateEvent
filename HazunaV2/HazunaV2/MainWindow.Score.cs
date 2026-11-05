using System.Windows;
using System.Windows.Threading;

namespace HazunaV2;

public partial class MainWindow
{
    private GameScore _score = null!;
    private ScoreOverlayWindow? _scoreOverlay;
    private readonly DispatcherTimer _scoreTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private void InitScore()
    {
        _score = new GameScore(_settings.ScoreValue, _settings.ScoreTarget, _settings.ScoreWin, _settings.ScoreDeath);

        _score.Changed += () =>
        {
            _settings.ScoreValue = _score.Value;
            _settings.ScoreTarget = _score.Target;
            _settings.ScoreWin = _score.WinDelta;
            _settings.ScoreDeath = _score.DeathDelta;
            SettingsStore.Save(_settings);
            ShowScoreInUi();
        };

        _score.Event += kind =>
        {
            AddLog(kind == "win"
                ? $"🏆 Game: sampai puncak, skor +{_score.WinDelta}"
                : $"💀 Game: mati, skor -{_score.DeathDelta}");

            NoteGameEvent(kind == "win"
                ? "Streamer berhasil sampai puncak Gunung Chiliad dan kabur dengan helikopter (menang)"
                : "Streamer mati di game", GameEventLevel.High);
        };

        _scoreTimer.Tick += (s, e) => _score.Poll();
        _scoreTimer.Start();

        ScoreTargetBox.Text = _score.Target.ToString();
        ScoreWinBox.Text = _score.WinDelta.ToString();
        ScoreDeathBox.Text = _score.DeathDelta.ToString();
        ShowScoreInUi();
    }

    private void ToggleSim_Click(object sender, RoutedEventArgs e)
    {
        bool show = SimCard.Visibility != Visibility.Visible;
        SimCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SimToggleBtn.Content = show ? "Simulasi / uji ▴" : "Simulasi / uji ▾";
    }

    private void ToggleBoardOptions_Click(object sender, RoutedEventArgs e)
    {
        bool show = BoardOptions.Visibility != Visibility.Visible;
        BoardOptions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BoardOptionsBtn.Content = show ? "Opsi ▴" : "Opsi ▾";
    }

    private void ShowScoreInUi()
    {
        ScoreValueText.Text = $"WIN = {_score.Value} / {_score.Target}";
        ScoreSetBox.Text = _score.Value.ToString();
    }

    private void ScorePlus_Click(object sender, RoutedEventArgs e) => _score.Add(1);
    private void ScoreMinus_Click(object sender, RoutedEventArgs e) => _score.Add(-1);

    private void ScoreSet_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ScoreSetBox.Text.Trim(), out var v))
        {
            MessageBox.Show("Skor harus berupa angka.");
            return;
        }
        _score.Set(v);
    }

    private void ScoreReset_Click(object sender, RoutedEventArgs e) => _score.Set(0);

    private void ScoreRules_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ScoreTargetBox.Text.Trim(), out var target) || target < 1 ||
            !int.TryParse(ScoreWinBox.Text.Trim(), out var win) || win < 0 ||
            !int.TryParse(ScoreDeathBox.Text.Trim(), out var death) || death < 0)
        {
            MessageBox.Show("Target minimal 1, tambah dan kurang berupa angka 0 atau lebih.");
            return;
        }
        _score.Configure(target, win, death);
    }

    private void ToggleScoreOverlay_Click(object sender, RoutedEventArgs e)
    {
        var o = EnsureScoreOverlay();
        SetOverlayVisible(o, !_overlayShown.Contains(o));
    }
}
