namespace DynaSharp.UnitTests;

/// <summary>
/// A random source that returns the same value every time, so that a test can pin the retry jitter to the lower end (0), the upper end
/// (1, which a real source never returns but which is the largest delay the policy allows) or anywhere between.
/// </summary>
internal sealed class FixedRandom(double value) : Random
{
    /// <summary>Gets a source that always returns the lower bound: no jitter delay at all.</summary>
    public static FixedRandom Zero { get; } = new(0);

    /// <summary>Gets a source that always returns the upper bound: the longest delay the policy allows.</summary>
    public static FixedRandom One { get; } = new(1);

    public override double NextDouble() => value;
}
