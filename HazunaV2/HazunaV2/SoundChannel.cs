using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HazunaV2;

// Jalur suara tersendiri (tidak saling memotong dengan suara aturan gift atau jalur lain).
public sealed class SoundChannel
{
    private readonly object _gate = new();
    private WaveOutEvent? _output;

    public event Action<string>? Error;

    public void Play(string path, int percent)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var reader = new AudioFileReader(path);
            float gain = Math.Clamp(percent, 1, 200) / 100f;

            ISampleProvider chain = new VolumeSampleProvider(reader) { Volume = gain };
            if (gain > 1f) chain = new SoundService.SoftLimiter(chain);

            var output = new WaveOutEvent();
            output.Init(chain);
            output.PlaybackStopped += (s, e) =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_output, output)) _output = null;
                }
                output.Dispose();
                reader.Dispose();
                if (e.Exception != null) Error?.Invoke(e.Exception.Message);
            };

            lock (_gate) _output = output;
            output.Play();
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    public void Stop()
    {
        WaveOutEvent? output;
        lock (_gate)
        {
            output = _output;
            _output = null;
        }

        try { output?.Stop(); } catch { /* sudah berhenti */ }
    }
}
