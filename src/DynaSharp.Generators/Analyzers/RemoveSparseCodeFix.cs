using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>The fix of DYNA110: <c>Sparse = true</c> is removed from an index whose inputs are never absent, with the comma before it.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveSparseCodeFix))]
[Shared]
internal sealed class RemoveSparseCodeFix : TextEditCodeFix
{
    /// <summary>The equivalence key of the fix.</summary>
    internal const string Key = "DYNA110";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.SparseWithoutNullableInput.Id];

    /// <inheritdoc/>
    protected override async Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        // The index is named first, so a named Sparse has an argument before it.
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var arguments = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<AttributeSyntax>()!.ArgumentList!.Arguments;
        var sparse = arguments.First(static argument => argument.NameEquals?.Name.Identifier.ValueText == "Sparse");
        var span = TextSpan.FromBounds(arguments[arguments.IndexOf(sparse) - 1].Span.End, sparse.Span.End);
        return [new FixOption("Remove Sparse = true", Key, [(document.Id, new TextChange(span, string.Empty))])];
    }
}
