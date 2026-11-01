namespace DynaSharp.Generators;

/// <summary>The hint name of a type whose usual one clashes with another type's.</summary>
/// <param name="FullName">The type, by <see cref="TypeModel.FullName"/>.</param>
/// <param name="HintName">The hint name it gets.</param>
internal sealed record RenamedHint(string FullName, string HintName);
