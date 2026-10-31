using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HazunaV2;

public record VcPreset(string Name, int Pitch, bool Robot, bool Echo);

public partial class MainWindow
{
    private static readonly VcPreset[] VcPresets =
    {
        new("Normal", 0, false, false),
        new("Anak-anak", 6, false, false),
        new("Suara cewek", 4, false, false),
        new("Suara berat", -4, false, false),
        new("Monster", -8, false, true),
        new("Robot", 0, true, false),
        new("Robot berat", -4, true, false),
        new("Gema", 0, false, true),
        new("Kustom", 0, false, false),
    };

    private readonly VoiceChangerService _vc = new();
    private readonly DispatcherTimer _vcMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _vcSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _vcReady;
    private bool _vcLoading;

    private void InitVoiceChanger()
    {
        _vc.OnError = msg => Dispatcher.BeginInvoke(new Action(() =>
        {
            AddLog("   ! Voice Changer: " + msg);
            StopVoiceChanger();
        }));

        _vcSaveTimer.Tick += (s, e) =>
        {
            _vcSaveTimer.Stop();
            SettingsStore.Save(_settings);
        };

        _vcMeterTimer.Tick += (s, e) => UpdateVcMeter();

        _vcLoading = true;

        try
        {
            var inputs = VoiceChangerService.GetInputDevices();
            var outputs = VoiceChangerService.GetOutputDevices();

            VcInputBox.ItemsSource = inputs;
            VcOutputBox.ItemsSource = outputs;

            VcInputBox.SelectedItem =
                inputs.FirstOrDefault(d => d.Name == _settings.VcInputDevice) ?? inputs.FirstOrDefault();
            VcOutputBox.SelectedItem =
                outputs.FirstOrDefault(d => d.Name == _settings.VcOutputDevice) ?? outputs.FirstOrDefault();

            if (inputs.Count == 0)
                VcNote.Text = "Tidak ada mikrofon yang terdeteksi di Windows ini.";
        }
        catch (Exception ex)
        {
            VcNote.Text = "Gagal membaca perangkat audio: " + ex.Message;
        }

        VcPresetBox.ItemsSource = VcPresets;
        VcPresetBox.SelectedItem = VcPresets.FirstOrDefault(p => p.Name == _settings.VcPreset)
                                   ?? VcPresets[VcPresets.Length - 1];

        VcPitchSlider.Value = Math.Clamp(_settings.VcPitch, -12, 12);
        VcVolumeSlider.Value = Math.Clamp(_settings.VcVolume, 0, 300);
        VcGainSlider.Value = Math.Clamp(_settings.VcMicGain, 50, 500);
        VcRobotCheck.IsChecked = _settings.VcRobot;
        VcEchoCheck.IsChecked = _settings.VcEcho;
        VcPitchLabel.Text = ((int)VcPitchSlider.Value).ToString("+0;-0;0");
        VcVolumeLabel.Text = (int)VcVolumeSlider.Value + "%";
        VcGainLabel.Text = (int)VcGainSlider.Value + "%";

        _vcLoading = false;
        _vcReady = true;

        ApplyVcParams();
        UpdateVcStatus();
    }

    private void ApplyVcParams()
    {
        _vc.Params.Semitones = (float)VcPitchSlider.Value;
        _vc.Params.Robot = VcRobotCheck.IsChecked == true;
        _vc.Params.Echo = VcEchoCheck.IsChecked == true;
        _vc.Params.Gain = (float)(VcVolumeSlider.Value / 100.0);
        _vc.Params.InputGain = (float)(VcGainSlider.Value / 100.0);
    }

    private void CommitVcSettings()
    {
        if (!_vcReady || _vcLoading) return;

        _settings.VcPitch = (int)VcPitchSlider.Value;
        _settings.VcVolume = (int)VcVolumeSlider.Value;
        _settings.VcMicGain = (int)VcGainSlider.Value;
        _settings.VcRobot = VcRobotCheck.IsChecked == true;
        _settings.VcEcho = VcEchoCheck.IsChecked == true;
        _settings.VcPreset = (VcPresetBox.SelectedItem as VcPreset)?.Name ?? "Kustom";
        _settings.VcInputDevice = (VcInputBox.SelectedItem as VcDevice)?.Name ?? "";
        _settings.VcOutputDevice = (VcOutputBox.SelectedItem as VcDevice)?.Name ?? "";

        ApplyVcParams();

        _vcSaveTimer.Stop();
        _vcSaveTimer.Start();
    }

    private void FlushVcSettings()
    {
        if (!_vcSaveTimer.IsEnabled) return;
        _vcSaveTimer.Stop();
        SettingsStore.Save(_settings);
    }

    private void StartVoiceChanger()
    {
        if (_vc.Running) return;

        if (VcInputBox.SelectedItem is not VcDevice input)
        {
            MessageBox.Show("Pilih mikrofon dulu.");
            return;
        }
        var output = VcOutputBox.SelectedItem as VcDevice ?? new VcDevice(-1, "Default Windows");

        ApplyVcParams();

        try
        {
            _vc.Start(input.Index, output.Index);
        }
        catch (Exception ex)
        {
            AddLog("   ! Voice Changer gagal dimulai: " + ex.Message);
            MessageBox.Show("Voice Changer gagal dimulai:\n" + ex.Message);
            UpdateVcStatus();
            return;
        }

        _vcMeterTimer.Start();
        AddLog($"Voice Changer aktif: {input.Name} -> {output.Name}");
        UpdateVcStatus();
    }

    private void StopVoiceChanger()
    {
        if (_vc.Running)
            AddLog("Voice Changer dihentikan.");
        _vc.Stop();
        _vcMeterTimer.Stop();
        VcLevelBar.Width = 0;
        UpdateVcStatus();
    }

    private void UpdateVcStatus()
    {
        VcToggleButton.Content = _vc.Running ? "Hentikan" : "Mulai";
        VcToggleButton.Style = (Style)FindResource(_vc.Running ? "DangerButton" : "PrimaryButton");
        VcStatus.Text = _vc.Running ? "Aktif: suara mikrofon sedang diubah" : "Tidak aktif";
    }

    private void UpdateVcMeter()
    {
        double track = VcLevelTrack.ActualWidth;
        double level = Math.Min(1.0, Math.Sqrt(_vc.Level));
        VcLevelBar.Width = track * level;
    }

    // ---------- Event kontrol ----------

    private void VcToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_vc.Running) StopVoiceChanger();
        else StartVoiceChanger();
    }

    private void VcPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_vcReady || _vcLoading) return;
        if (VcPresetBox.SelectedItem is not VcPreset p) return;

        if (p.Name != "Kustom")
        {
            _vcLoading = true;
            VcPitchSlider.Value = p.Pitch;
            VcRobotCheck.IsChecked = p.Robot;
            VcEchoCheck.IsChecked = p.Echo;
            VcPitchLabel.Text = p.Pitch.ToString("+0;-0;0");
            _vcLoading = false;
        }
        CommitVcSettings();
    }

    // Mengubah slider / opsi secara manual membuat preset menjadi "Kustom"
    private void MarkVcCustom()
    {
        if (!_vcReady || _vcLoading) return;
        _vcLoading = true;
        VcPresetBox.SelectedItem = VcPresets[VcPresets.Length - 1];
        _vcLoading = false;
    }

    private void VcPitch_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_vcReady) return;
        VcPitchLabel.Text = ((int)VcPitchSlider.Value).ToString("+0;-0;0");
        MarkVcCustom();
        CommitVcSettings();
    }

    private void VcVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_vcReady) return;
        VcVolumeLabel.Text = (int)VcVolumeSlider.Value + "%";
        CommitVcSettings();
    }

    private void VcGain_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_vcReady) return;
        VcGainLabel.Text = (int)VcGainSlider.Value + "%";
        CommitVcSettings();
    }

    private void VcOption_Click(object sender, RoutedEventArgs e)
    {
        MarkVcCustom();
        CommitVcSettings();
    }

    private void VcDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_vcReady || _vcLoading) return;
        CommitVcSettings();

        // Ganti perangkat saat aktif: mulai ulang agar langsung berlaku
        if (_vc.Running)
        {
            _vc.Stop();
            StartVoiceChanger();
        }
    }

    private void VcRefresh_Click(object sender, RoutedEventArgs e)
    {
        bool wasRunning = _vc.Running;
        if (wasRunning) StopVoiceChanger();

        string? inName = (VcInputBox.SelectedItem as VcDevice)?.Name;
        string? outName = (VcOutputBox.SelectedItem as VcDevice)?.Name;

        _vcLoading = true;
        var inputs = VoiceChangerService.GetInputDevices();
        var outputs = VoiceChangerService.GetOutputDevices();
        VcInputBox.ItemsSource = inputs;
        VcOutputBox.ItemsSource = outputs;
        VcInputBox.SelectedItem = inputs.FirstOrDefault(d => d.Name == inName) ?? inputs.FirstOrDefault();
        VcOutputBox.SelectedItem = outputs.FirstOrDefault(d => d.Name == outName) ?? outputs.FirstOrDefault();
        _vcLoading = false;

        CommitVcSettings();
        if (wasRunning) StartVoiceChanger();
    }
}
