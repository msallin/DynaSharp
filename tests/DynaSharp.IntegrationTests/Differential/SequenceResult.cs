namespace DynaSharp.IntegrationTests.Differential;

/// <summary>What one run of a sequence showed.</summary>
/// <param name="Operations">The operations that ran.</param>
/// <param name="Steps">The answers of the fake and of the backend for each operation that ran, for a report.</param>
/// <param name="Stop">The divergence that ended the run: one in the results, known or not; <see langword="null"/> when every result agreed.</param>
/// <param name="Passed">The known divergences the run went on after: of a read or of the consumed capacity alone, which leave the tables alike.</param>
/// <param name="Messages">The operations whose results agreed and whose error messages did not.</param>
internal sealed record SequenceResult(
    IReadOnlyList<Operation> Operations,
    IReadOnlyList<(Outcome Fake, Outcome Emulator)> Steps,
    Divergence? Stop,
    IReadOnlyList<Divergence> Passed,
    IReadOnlyList<Divergence> Messages)
{
    /// <summary>Gets the divergence that fails the test: one in the results that no known divergence explains.</summary>
    public Divergence? Failure => Stop is { KnownAs: null } ? Stop : null;
}
