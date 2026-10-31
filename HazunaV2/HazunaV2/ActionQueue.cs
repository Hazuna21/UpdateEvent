using System.Threading.Channels;

namespace HazunaV2;

public record QueuedAction(
    string UserName, string GiftName, int Index, int Total,
    string Key, int TapHoldMs, bool HoldKey,
    string SoundPath, string MediaPath,
    double DurationSeconds, double StartDelaySeconds);

public class ActionQueue
{
    private readonly Channel<(QueuedAction Action, int Generation)> _channel =
        Channel.CreateUnbounded<(QueuedAction Action, int Generation)>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly object _lock = new();
    private CancellationTokenSource? _currentCts;
    private double _pendingSeconds;
    private int _pending;
    private int _generation;

    public Func<QueuedAction, CancellationToken, Task>? Executor { get; set; }
    public Action<string>? OnError { get; set; }
    public event Action? Changed;

    public QueuedAction? Current { get; private set; }
    public int Pending => Math.Max(0, Volatile.Read(ref _pending));

    public double PendingSeconds
    {
        get { lock (_lock) return Math.Max(0, _pendingSeconds); }
    }

    public void Start()
    {
        _ = WorkerAsync();
    }

    public void EnqueueRange(IEnumerable<QueuedAction> actions)
    {
        int gen = Volatile.Read(ref _generation);
        foreach (var action in actions)
        {
            Interlocked.Increment(ref _pending);
            lock (_lock) _pendingSeconds += Cost(action);
            _channel.Writer.TryWrite((action, gen));
        }
        RaiseChanged();
    }

    // Membatalkan semua giliran yang menunggu dan menghentikan giliran yang sedang berjalan.
    public int Clear()
    {
        Interlocked.Increment(ref _generation);
        int dropped = Interlocked.Exchange(ref _pending, 0);
        lock (_lock) _pendingSeconds = 0;
        _currentCts?.Cancel();
        RaiseChanged();
        return dropped;
    }

    private async Task WorkerAsync()
    {
        await foreach (var (action, generation) in _channel.Reader.ReadAllAsync())
        {
            if (generation != Volatile.Read(ref _generation))
                continue; // giliran lama yang sudah dibatalkan

            Interlocked.Decrement(ref _pending);
            lock (_lock) _pendingSeconds -= Cost(action);

            using var cts = new CancellationTokenSource();
            _currentCts = cts;
            Current = action;
            RaiseChanged();

            try
            {
                if (Executor != null)
                    await Executor(action, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
            }
            finally
            {
                _currentCts = null;
                Current = null;
                RaiseChanged();
            }
        }
    }

    private static double Cost(QueuedAction a) =>
        Math.Max(0.2, a.DurationSeconds) + a.StartDelaySeconds;

    private void RaiseChanged()
    {
        if (_ui != null)
            _ui.Post(_ => Changed?.Invoke(), null);
        else
            Changed?.Invoke();
    }

    public static string FormatSeconds(double seconds)
    {
        int total = (int)Math.Ceiling(seconds);
        if (total < 60) return total + " dtk";

        int m = total / 60;
        int s = total % 60;
        if (m < 60) return s == 0 ? $"{m} mnt" : $"{m} mnt {s} dtk";

        int h = m / 60;
        m %= 60;
        return $"{h} jam {m} mnt";
    }
}