using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HazunaV2;

// Memutar suara aturan dengan volume 1 - 200 persen. Di atas 100% sinyal diperkeras lalu dibatasi
// secara halus supaya tidak pecah kasar (lebih dari itu tetap bisa terdengar bising).
public static class SoundService
{
    private static readonly object Gate = new();
    private static WaveOutEvent? _output;
    private static AudioFileReader? _reader;

    public static event Action<string>? Error;

    public static void Play(string path, int percent)
    {
        Stop();

        try
        {
            var reader = new AudioFileReader(path);
            float gain = Math.Clamp(percent, 1, 200) / 100f;

            ISampleProvider chain = new VolumeSampleProvider(reader) { Volume = gain };
            if (gain > 1f) chain = new SoftLimiter(chain);

            var output = new WaveOutEvent();
            output.Init(chain);
            output.PlaybackStopped += (s, e) =>
            {
                lock (Gate)
                {
                    if (!ReferenceEquals(_output, output)) { output.Dispose(); reader.Dispose(); return; }
                    _output = null;
                    _reader = null;
                }
                output.Dispose();
                reader.Dispose();
                if (e.Exception != null) Error?.Invoke(e.Exception.Message);
            };

            lock (Gate)
            {
                _output = output;
                _reader = reader;
            }
            output.Play();
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    public static void Stop()
    {
        WaveOutEvent? output;
        lock (Gate)
        {
            output = _output;
            _output = null;
            _reader = null;
        }

        try { output?.Stop(); } catch { /* sudah berhenti */ }
    }

    // Biarkan sinyal normal lewat apa adanya, lalu lipat puncak yang melebihi 0,8 dengan lembut ke 1,0
    internal sealed class SoftLimiter : ISampleProvider
    {
        private readonly ISampleProvider _source;
        public SoftLimiter(ISampleProvider source) => _source = source;
        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                float x = buffer[offset + i];
                float a = Math.Abs(x);
                if (a > 0.8f)
                    buffer[offset + i] = Math.Sign(x) * (0.8f + 0.2f * MathF.Tanh((a - 0.8f) / 0.2f));
            }
            return read;
        }
    }
}
