namespace DynaSharp.Generators;

/// <summary>Where the class <c>DynamoDbEntities</c> of an assembly goes, and what of the assembly takes a name it needs.</summary>
/// <param name="Namespace">The namespace, the name of the assembly made a valid one, as code.</param>
/// <param name="Clash">A type of the assembly with the name of that class or of a part of the namespace, as the arguments of <c>DYNA038</c> and its place, or null.</param>
internal sealed record AssemblyHome(string Namespace, DiagnosticInfo? Clash);
