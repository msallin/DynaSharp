using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>The fix of DYNA123: <c>[KeyOnly]</c> on the property that a raw placeholder of the table key holds, <c>[property: KeyOnly]</c> on a parameter of a record.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(KeyOnlyCodeFix))]
[Shared]
internal sealed class KeyOnlyCodeFix : TextEditCodeFix
{
    /// <summary>The equivalence key of the fix.</summary>
    internal const string Key = "DYNA123";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.RawWithoutKeyOnly.Id];

    /// <inheritdoc/>
    protected override async Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        // The diagnostic stands on the name of the property, or on the type for a property of a compiled base.
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var member = root.FindNode(diagnostic.Location.SourceSpan);
        if (member is not (PropertyDeclarationSyntax or ParameterSyntax))
        {
            return [];
        }

        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        return [new FixOption("Add [KeyOnly]", Key, [(document.Id, SourceEdits.AddAttribute(model, member, WellKnownNames.KeyOnlyAttribute, member is ParameterSyntax ? "property" : null, token))])];
    }
}
