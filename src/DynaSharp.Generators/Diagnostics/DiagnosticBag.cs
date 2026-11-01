using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Collects what is wrong with one type while its model is built.</summary>
internal sealed class DiagnosticBag
{
    private readonly List<DiagnosticInfo> _items = [];

    /// <summary>Gets a value indicating whether the type cannot produce a model.</summary>
    public bool HasErrors { get; private set; }

    public void Report(DiagnosticDescriptor descriptor, Spot spot, params string[] arguments)
    {
        _items.Add(new DiagnosticInfo(descriptor.Id, new EquatableArray<string>(arguments), spot.FilePath, spot.Span));
        HasErrors |= descriptor.DefaultSeverity == DiagnosticSeverity.Error;
    }

    /// <summary>Records that the compiler already reports a problem, such as a type that does not resolve, so no model can exist.</summary>
    public void MarkBroken() => HasErrors = true;

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => new([.. _items]);
}
