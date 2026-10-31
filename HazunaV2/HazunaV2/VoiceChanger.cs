using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HazunaV2;

public record VcDevice(int Index, string Name);

// Parameter efek yang bisa diubah kapan saja saat voice changer berjalan
public class VoiceParams
{
    public volatile float Semitones;   // -12 sampai +12
    public volatile bool Robot;
    public volatile bool Echo;
    public volatile float Gain = 1f;   // volume keluaran, 0 sampai 3
    public volatile float InputGain = 2f; // penguat mikrofon, 0.5 sampai 5
}

// Pengubah nada sederhana: dua "kepala baca" bergeser pada penyangga suara lalu dicampur halus.
// Latensinya kecil (sekitar 40 ms), kualitasnya cukup untuk suara lucu / berat / anak-anak.
public class PitchShifter
{
    private readonly float[] _buf;
    private readonly int _win;
    private int _w;
    private double _phase;

    public PitchShifter(int sampleRate)
    {
        _win = sampleRate * 40 / 1000;
        _buf = new float[_win * 2 + 8];
    }

    public float Process(float x, double ratio, bool bypass)
    {
        _buf[_w] = x;

        float y = x;
        if (!bypass)
        {
            _phase += (1.0 - ratio) / _win;
            _phase -= Math.Floor(_phase);

            double p2 = _phase + 0.5;
            if (p2 >= 1.0) p2 -= 1.0;

            y = Head(_phase) * Weight(_phase) + Head(p2) * Weight(p2);
        }

        _w++;
        if (_w >= _buf.Length) _w = 0;
        return y;
    }

    private float Head(double phase)
    {
        double delay = 1.0 + phase * (_win - 1);
        double rp = _w - delay;
        if (rp < 0) rp += _buf.Length;

        int i0 = (int)rp;
        double frac = rp - i0;
        int i1 = i0 + 1;
        if (i1 >= _buf.Length) i1 = 0;

        return (float)(_buf[i0] * (1.0 - frac) + _buf[i1] * frac);
    }

    private static float Weight(double phase)
    {
        double s = Math.Sin(Math.PI * phase);
        return (float)(s * s);
    }
}

// Rantai efek: ubah nada -> robot -> gema -> volume
public class VoiceEffectProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly VoiceParams _p;
    private readonly PitchShifter _pitch;
    private readonly float[] _echo;
    private int _echoPos;
    private double _robotPhase;
    private readonly double _robotStep;
    private volatile float _level;

    public WaveFormat WaveFormat => _source.WaveFormat;

    // Puncak suara masuk (0..1), dipakai untuk indikator level
    public float Level => _level;

    public VoiceEffectProvider(ISampleProvider source, VoiceParams p)
    {
        _source = source;
        _p = p;
        int sr = source.WaveFormat.SampleRate;
        _pitch = new PitchShifter(sr);
        _echo = new float[sr * 280 / 1000];
        _robotStep = 2.0 * Math.PI * 70.0 / sr;
    }

    // Batas lembut: di atas 0.8 suara ditekan halus, bukan dipotong keras (mengurangi distorsi)
    private static float SoftLimit(float y)
    {
        float a = Math.Abs(y);
        if (a <= 0.8f) return y;
        float limited = 0.8f + 0.2f * (float)Math.Tanh((a - 0.8f) / 0.2f);
        return y < 0 ? -limited : limited;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _source.Read(buffer, offset, count);

        float semis = _p.Semitones;
        double ratio = Math.Pow(2.0, semis / 12.0);
        bool bypass = Math.Abs(semis) < 0.05f;
        bool robot = _p.Robot;
        bool echo = _p.Echo;
        float gain = _p.Gain;
        float inGain = _p.InputGain;
        float peak = 0;

        for (int i = 0; i < n; i++)
        {
            float x = Math.Clamp(buffer[offset + i] * inGain, -1f, 1f);
            float ax = Math.Abs(x);
            if (ax > peak) peak = ax;

            float y = _pitch.Process(x, ratio, bypass);

            if (robot)
            {
                _robotPhase += _robotStep;
                if (_robotPhase > Math.PI * 2) _robotPhase -= Math.PI * 2;
                y = y * (0.2f + 0.8f * (float)Math.Sin(_robotPhase));
            }

            float e = _echo[_echoPos];
            if (echo)
            {
                _echo[_echoPos] = y + e * 0.4f;
                y += e * 0.45f;
            }
            else
            {
                _echo[_echoPos] = 0f;
            }
            _echoPos++;
            if (_echoPos >= _echo.Length) _echoPos = 0;

            y *= gain;
            buffer[offset + i] = SoftLimit(y);
        }

        _level = peak;
        return n;
    }
}

// Mikrofon -> efek -> perangkat keluaran (misalnya VB-Cable agar bisa dipakai TikTok LIVE Studio / OBS)
public class VoiceChangerService : IDisposable
{
    public const int SampleRate = 48000;

    private WaveInEvent? _in;
    private WaveOutEvent? _out;
    private BufferedWaveProvider? _buf;
    private VoiceEffectProvider? _fx;

    public VoiceParams Params { get; } = new();
    public bool Running { get; private set; }
    public Action<string>? OnError { get; set; }

    public float Level => _fx?.Level ?? 0f;

    public static List<VcDevice> GetInputDevices()
    {
        var list = new List<VcDevice>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            list.Add(new VcDevice(i, WaveInEvent.GetCapabilities(i).ProductName));
        return list;
    }

    public static List<VcDevice> GetOutputDevices()
    {
        var list = new List<VcDevice> { new(-1, "Default Windows") };
        for (int i = 0; i < WaveOut.DeviceCount; i++)
            list.Add(new VcDevice(i, WaveOut.GetCapabilities(i).ProductName));
        return list;
    }

    public void Start(int inputDevice, int outputDevice)
    {
        if (Running) return;

        try
        {
            var format = new WaveFormat(SampleRate, 16, 1);

            _in = new WaveInEvent
            {
                DeviceNumber = inputDevice,
                WaveFormat = format,
                BufferMilliseconds = 30,
                NumberOfBuffers = 3
            };

            _buf = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            _fx = new VoiceEffectProvider(_buf.ToSampleProvider(), Params);

            _out = new WaveOutEvent
            {
                DeviceNumber = outputDevice,
                DesiredLatency = 80,
                NumberOfBuffers = 3
            };
            _out.Init(new SampleToWaveProvider16(_fx));

            _in.DataAvailable += OnData;
            _in.RecordingStopped += OnRecordingStopped;

            _in.StartRecording();
            _out.Play();
            Running = true;
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public void Stop()
    {
        Cleanup();
        Running = false;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var buf = _buf;
        if (buf == null) return;

        // Jaga latensi: kalau antrean sudah menumpuk, buang data baru
        if (buf.BufferedDuration.TotalMilliseconds > 200) return;
        buf.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            OnError?.Invoke(e.Exception.Message);
    }

    private void Cleanup()
    {
        var input = _in;
        var output = _out;
        _in = null;
        _out = null;
        _buf = null;
        _fx = null;

        try
        {
            if (input != null)
            {
                input.DataAvailable -= OnData;
                input.RecordingStopped -= OnRecordingStopped;
                input.StopRecording();
                input.Dispose();
            }
        }
        catch { }

        try
        {
            output?.Stop();
            output?.Dispose();
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}
