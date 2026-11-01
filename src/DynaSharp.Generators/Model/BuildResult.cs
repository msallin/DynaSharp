namespace DynaSharp.Generators;

/// <summary>
/// What the transform of one attributed type hands to the rest of the pipeline. It compares by value, so that an unchanged type stops
/// the pipeline. Nothing bound to a compilation may be a member.
/// </summary>
/// <param name="Type">The model, or null when an error makes the type unusable: emitting from it would only add follow-up errors.</param>
/// <param name="Diagnostics">What is wrong with the type.</param>
internal sealed record BuildResult(TypeModel? Type, EquatableArray<DiagnosticInfo> Diagnostics);
