namespace DynaSharp.UnitTests;

/// <summary>
/// A clock that only moves when the test says so. Timers fire during <see cref="Advance"/>, on the calling thread.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 10, 31, 12, 34, 56, TimeSpan.Zero))
    {
    }

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    /// <summary>Gets the number of timers created so far.</summary>
    public int TimersCreated { get; private set; }

    /// <summary>Gets the number of timers that are created and not disposed.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    /// <summary>Gets the time until the earliest armed timer fires, or <see langword="null"/> if none is armed.</summary>
    public TimeSpan? NextDueIn
    {
        get
        {
            lock (_gate)
            {
                return _timers.Select(timer => timer.DueAt - _utcNow).Min();
            }
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            TimersCreated++;
            _timers.Add(timer);
            timer.Change(dueTime, period);
        }

        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _utcNow += delta;
            _timestamp += delta.Ticks;
            due = _timers.FindAll(timer => timer.IsDue(_utcNow));
            due.ForEach(timer => timer.Disarm());
        }

        due.ForEach(timer => timer.Fire());
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._utcNow + dueTime;
            return true;
        }

        public bool IsDue(DateTimeOffset now) => DueAt is { } dueAt && dueAt <= now;

        public void Disarm() => DueAt = null;

        public void Fire() => callback(state);

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
