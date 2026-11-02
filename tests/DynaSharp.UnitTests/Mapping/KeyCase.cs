namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>One row of the table of <see cref="GeneratedTryOfTests"/>.</summary>
/// <param name="Name">What the row is about, which is also how it shows in a report.</param>
/// <param name="IsKey">Whether the values can be a key.</param>
/// <param name="Try">The call that must not throw.</param>
/// <param name="Throwing">The call that it stands in for.</param>
public readonly record struct KeyCase(string Name, bool IsKey, GeneratedTryOfTests.TryKey Try, GeneratedTryOfTests.KeyCall Throwing)
{
    public override string ToString() => Name;
}
