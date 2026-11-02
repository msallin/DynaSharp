namespace DynaSharp.Generators;

/// <summary>A computed property that feeds a key, with what <c>[ComputedFrom]</c> says it is derived from.</summary>
/// <param name="Property">The property, by <see cref="PropertyModel.Name"/>.</param>
/// <param name="Dependencies">The properties it is derived from, by <see cref="PropertyModel.Name"/>, which is how a handle names them.</param>
/// <param name="DependencyAttributes">The attributes of those properties, which is how an update sees them.</param>
internal sealed record ComputedInput(string Property, EquatableArray<string> Dependencies, EquatableArray<string> DependencyAttributes);
