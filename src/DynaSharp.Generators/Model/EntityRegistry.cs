namespace DynaSharp.Generators;

/// <summary>What the entities of an assembly give together: the class with <c>IncludingAll()</c>, or why there is none.</summary>
/// <param name="Class">The class, or null.</param>
/// <param name="Diagnostics">Why there is no class, if the assembly has entities.</param>
internal sealed record EntityRegistry(EntitiesClass? Class, EquatableArray<DiagnosticInfo> Diagnostics);
