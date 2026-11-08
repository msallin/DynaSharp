using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA042: <c>ref int count</c> of the constructor a read uses becomes <c>int count</c>, or <c>in int count</c>. The third way the message names, another constructor, is the fix
/// of DYNA005 once that one has the attribute. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PassByValueCodeFix))]
[Shared]
internal sealed class PassByValueCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that takes the parameter by value.</summary>
    internal const string ValueKey = "DYNA042.Value";

    /// <summary>The equivalence key of the fix that takes the parameter as <c>in</c>.</summary>
    internal const string InKey = "DYNA042.In";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.ByReferenceParameter.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        if (SourceEdits.IsGenerated(root.SyntaxTree, token))
        {
            return;
        }

        var text = await document.GetTextAsync(token).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            var parameter = (ParameterSyntax)SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan);
            var name = parameter.Identifier.ValueText;

            // ref, out and ref readonly go; scoped too for a value, which it cannot be, but stays on in.
            var modifiers = parameter.Modifiers.Where(static modifier => modifier.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.ReadOnlyKeyword).ToList();
            var byValue = parameter.Modifiers.Where(static modifier => modifier.IsKind(SyntaxKind.ScopedKeyword)).Concat(modifiers).Select(modifier => SourceEdits.Remove(text, modifier)).ToList();
            var asIn = modifiers.Skip(1).Select(modifier => SourceEdits.Remove(text, modifier)).Prepend(new TextChange(modifiers[0].Span, "in")).ToList();
            context.RegisterCodeFix(CodeAction.Create("Take " + name + " by value", edit => SourceEdits.ApplyAsync(document, byValue, edit), ValueKey), diagnostic);
            context.RegisterCodeFix(CodeAction.Create("Take " + name + " as in", edit => SourceEdits.ApplyAsync(document, asIn, edit), InKey), diagnostic);
        }
    }
}
