using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Collects what is wrong with one type while its model is built.</summary>
internal sealed class DiagnosticBag
{
    private readonly List<DiagnosticInfo> _items = [];

    /// <summary>Gets a value indicating whether the type cannot produce a model.</summary>
    public bool HasErrors { get; private set; }

    public void Report(DiagnosticDescriptor descriptor, Spot spot, params string[] arguments) => Report(descriptor, spot, null, arguments);

    /// <summary>As <see cref="Report(DiagnosticDescriptor, Spot, string[])"/>, with what a code fix needs, which the diagnostic carries in <see cref="DiagnosticInfo.FixProperty"/>.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="spot">Where it is.</param>
    /// <param name="fix">What the code fix needs; null for no fix.</param>
    /// <param name="arguments">The arguments of the message.</param>
    public void Report(DiagnosticDescriptor descriptor, Spot spot, string? fix, string[] arguments)
    {
        _items.Add(new DiagnosticInfo(descriptor.Id, new EquatableArray<string>(arguments), spot.FilePath, spot.Span, Fix: fix));
        HasErrors |= descriptor.DefaultSeverity == DiagnosticSeverity.Error;
    }

    /// <summary>
    /// Reports an error that leaves the model whole, so the type is generated beside it and every use of it compiles: only the error fails the build, not the <c>CS0311</c> and <c>CS0426</c> a missing
    /// <c>Keys</c> would add on each use. That holds for a missing discriminator alone, whose model is the one of an opt-out, and for a key format that does not fit its type or is out of range,
    /// whose placeholder takes the default encoding of the type. Every other error means that some part of the model cannot be built.
    /// </summary>
    /// <param name="descriptor">The descriptor, an error.</param>
    /// <param name="spot">Where it is.</param>
    /// <param name="fix">What a code fix needs, which the diagnostic carries in <see cref="DiagnosticInfo.FixProperty"/>; null for no fix.</param>
    /// <param name="arguments">The arguments of the message.</param>
    public void ReportKeepingTheModel(DiagnosticDescriptor descriptor, Spot spot, string? fix, string[] arguments) =>
        _items.Add(new DiagnosticInfo(descriptor.Id, new EquatableArray<string>(arguments), spot.FilePath, spot.Span, Fix: fix));

    /// <summary>Records that the compiler already reports a problem, such as a type that does not resolve, so no model can exist.</summary>
    public void MarkBroken() => HasErrors = true;

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => new([.. _items]);
}
