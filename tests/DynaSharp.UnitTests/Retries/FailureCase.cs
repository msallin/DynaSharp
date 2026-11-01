using DynaSharp.Retries;

namespace DynaSharp.UnitTests.Retries;

/// <summary>One failed attempt and how the retry policy must classify it. The display name in test output is <paramref name="Name"/>.</summary>
/// <param name="Name">The name shown in test output.</param>
/// <param name="Exception">What the attempt failed with.</param>
/// <param name="Expected">The expected classification.</param>
internal readonly record struct FailureCase(string Name, Exception Exception, FailureKind Expected)
{
    public override string ToString() => Name;
}
