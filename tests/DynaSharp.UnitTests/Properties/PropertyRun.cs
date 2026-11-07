namespace DynaSharp.UnitTests.Properties;

/// <summary>
/// The settings every FsCheck property shares: <c>[FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, ...)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// A fixed seed makes a run the same on every machine, so a property fails in CI only for a change of the code, never for a draw of the
/// generator. A failure prints the seed and the step it failed at, <c>Replay directly at failing step with (seed,gamma,size)</c>, and the
/// shrunk arguments; put that triple into <c>Replay</c> of the one property to run just that case.
/// </para>
/// <para>To look further than the fixed seed, set <c>Replay</c> to another pair locally, or remove it for a seed of the clock, and keep the seed in a regression test if a case fails.</para>
/// </remarks>
internal static class PropertyRun
{
    /// <summary>The seed and gamma of every property. The gamma must be odd.</summary>
    public const string Seed = "20261107,1442695040888963407";

    /// <summary>The number of cases of a property whose case is cheap: a value written and read.</summary>
    public const int Cases = 500;

    /// <summary>The number of cases of a property whose case is a request or a tree of conditions.</summary>
    public const int FewerCases = 200;
}
