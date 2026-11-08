using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fix of DYNA019: <c>#nullable enable</c> at the top of the file, after its leading comments, where no directive before the type decides the context. Where one does, such as a
/// <c>#nullable disable</c> above the type, that directive becomes <c>enable</c>, since a directive at the top would not reach past it. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EnableNullableCodeFix))]
[Shared]
internal sealed class EnableNullableCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for both edits, so that a fix-all enables each file once.</summary>
    internal const string Key = "DYNA019.Enable";

    private const string Enable = "#nullable enable";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NullableContextDisabled.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        if (GeneratedFiles.IsGenerated(root.SyntaxTree, context.CancellationToken))
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (Deciding(root, diagnostic.Location.SourceSpan.Start) is { } directive)
            {
                var enabled = directive.WithSettingToken(SyntaxFactory.Token(directive.SettingToken.LeadingTrivia, SyntaxKind.EnableKeyword, directive.SettingToken.TrailingTrivia));
                var title = "Change " + Text(directive) + " to " + Text(enabled);
                context.RegisterCodeFix(CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(directive, enabled))), Key), diagnostic);
            }
            else
            {
                context.RegisterCodeFix(CodeAction.Create("Add " + Enable + " at the top of the file", _ => Task.FromResult(document.WithSyntaxRoot(AtTop(root))), Key), diagnostic);
            }
        }
    }

    // The last directive before the type that sets the annotations, which a #nullable ... warnings leaves as they are.
    private static NullableDirectiveTriviaSyntax? Deciding(CompilationUnitSyntax root, int position)
    {
        NullableDirectiveTriviaSyntax? deciding = null;
        for (var directive = root.GetFirstDirective(); directive is not null && directive.SpanStart < position; directive = directive.GetNextDirective())
        {
            if (directive is NullableDirectiveTriviaSyntax nullable && !nullable.TargetToken.IsKind(SyntaxKind.WarningsKeyword))
            {
                deciding = nullable;
            }
        }

        return deciding;
    }

    private static string Text(DirectiveTriviaSyntax directive) => directive.ToString().Trim();

    // After the comments that head the file and before any directive there, such as an #if, so that it is not inside one; then a blank line.
    private static CompilationUnitSyntax AtTop(CompilationUnitSyntax root)
    {
        var first = root.GetFirstToken(includeZeroWidth: true);
        var leading = first.LeadingTrivia;
        var at = 0;
        while (at < leading.Count && !leading[at].IsDirective)
        {
            at++;
        }

        // Without a directive, before the indent of the first token, if it has one.
        if (at == leading.Count && at > 0 && leading[at - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            at--;
        }

        var lineEnd = root.DescendantTrivia().FirstOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        var lineEndText = lineEnd == default ? "\n" : lineEnd.ToString();
        var added = SyntaxFactory.ParseLeadingTrivia(Enable + lineEndText + lineEndText);
        return root.ReplaceToken(first, first.WithLeadingTrivia(leading.InsertRange(at, added)));
    }
}
