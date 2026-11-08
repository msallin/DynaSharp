using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA029, by what the diagnostic says in its property <c>Fix</c>: a second <c>[assembly: DynamoDbDefaultConverter]</c> for a type goes, and <c>typeof(int?)</c> becomes
/// <c>typeof(int)</c>. An open generic type needs the type argument only the author knows. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DefaultConverterCodeFix))]
[Shared]
internal sealed class DefaultConverterCodeFix : CodeFixProvider
{
    /// <summary>The value of <c>Fix</c> of a second default for a type, and the equivalence key of the fix that removes it.</summary>
    internal const string RemoveKey = "DYNA029.Remove";

    /// <summary>The value of <c>Fix</c> of a nullable type, and the equivalence key of the fix that drops its <c>?</c>.</summary>
    internal const string UnwrapKey = "DYNA029.Unwrap";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.InvalidDefaultConverter.Id];

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
            // A type that is missing or open generic is the author's to name.
            if (!diagnostic.Properties.TryGetValue(DiagnosticInfo.FixProperty, out var fix))
            {
                continue;
            }

            var attribute = SourceEdits.AttributeAt(root, diagnostic.Location.SourceSpan)!;
            var type = ((TypeOfExpressionSyntax)attribute.ArgumentList!.Arguments[0].Expression).Type;
            if (fix == RemoveKey)
            {
                var removal = SourceEdits.RemoveAttribute(root.SyntaxTree.GetText(token), attribute);
                context.RegisterCodeFix(CodeAction.Create("Remove the second default for " + type, edit => SourceEdits.ApplyAsync(document, [removal], edit), RemoveKey), diagnostic);
            }
            else if (type is NullableTypeSyntax nullable)
            {
                var change = new TextChange(nullable.QuestionToken.Span, string.Empty);
                context.RegisterCodeFix(CodeAction.Create("Drop the ? of " + nullable, edit => SourceEdits.ApplyAsync(document, [change], edit), UnwrapKey), diagnostic);
            }
        }
    }
}
