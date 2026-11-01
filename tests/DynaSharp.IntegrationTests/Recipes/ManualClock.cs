namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>The time of a recipe: it stands still until a test moves it, so a lease can run out without a sleep. Recipes take a <see cref="TimeProvider"/>, as production code would.</summary>
/// <param name="start">The time it starts at.</param>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    /// <summary>Moves the time on.</summary>
    /// <param name="time">How far.</param>
    public void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
}
