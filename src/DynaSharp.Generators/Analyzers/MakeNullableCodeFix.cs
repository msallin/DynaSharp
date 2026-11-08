using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA024: <c>public Level Level { get; set; }</c> of an enum without members becomes <c>public Level? Level { get; set; }</c>, which a write leaves out. The other ways the message
/// names, a member or a converter, need a name or a type only the author can write. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeNullableCodeFix))]
[Shared]
internal sealed class MakeNullableCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every property, so a fix-all makes each nullable.</summary>
    internal const string Key = "DYNA024.Nullable";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.EnumWithoutMembers.Id];

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

        foreach (var diagnostic in context.Diagnostics)
        {
            // A property, or the parameter of a positional record that declares it.
            var node = SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan);
            var (name, type) = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property
                ? (property.Identifier.ValueText, property.Type)
                : node.FirstAncestorOrSelf<ParameterSyntax>() is { Type: { } written } parameter ? (parameter.Identifier.ValueText, written) : (null, null);
            if (type is not null)
            {
                var change = new TextChange(new TextSpan(type.Span.End, 0), "?");
                context.RegisterCodeFix(CodeAction.Create("Make " + name + " nullable", edit => SourceEdits.ApplyAsync(document, [change], edit), Key), diagnostic);
            }
        }
    }
}
