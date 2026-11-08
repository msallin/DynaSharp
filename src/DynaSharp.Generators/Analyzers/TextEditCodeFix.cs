using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// A code fix whose actions are text edits computed from one diagnostic each, so that its fix-all applies the edits of every diagnostic of the scope at once: an edit two diagnostics share
/// once, such as the <c>Sparse = true</c> that two placeholders of one index ask for, and insertions at one place in the order of the diagnostics. Nothing is offered where an edit would
/// land in a file a tool writes. Only an IDE loads this class.
/// </summary>
internal abstract class TextEditCodeFix : CodeFixProvider
{
    /// <inheritdoc/>
    public sealed override FixAllProvider GetFixAllProvider() => FixAll.Instance;

    /// <inheritdoc/>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var solution = context.Document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            foreach (var option in await OptionsAsync(context.Document, diagnostic, context.CancellationToken).ConfigureAwait(false))
            {
                context.RegisterCodeFix(CodeAction.Create(option.Title, token => SourceEdits.ApplyAsync(solution, option.Edits, token), option.Key), diagnostic);
            }
        }
    }

    /// <summary>The parts of the property <see cref="DiagnosticInfo.FixProperty"/> of a diagnostic, or null when it has none.</summary>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>The parts, split at <see cref="DiagnosticInfo.FixSeparator"/>.</returns>
    protected static string[]? FixOf(Diagnostic diagnostic) =>
        diagnostic.Properties.TryGetValue(DiagnosticInfo.FixProperty, out var fix) ? fix!.Split(DiagnosticInfo.FixSeparator) : null;

    /// <summary>
    /// Where a property of the type around a place is declared, by the name the generator gave: a property declaration, also in another part of the type or in a base type in source, or the
    /// parameter of a record. Null for a property from metadata or of a generated file.
    /// </summary>
    /// <param name="document">The document of the place.</param>
    /// <param name="span">The place, inside the type.</param>
    /// <param name="name">The name of the property.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The document of the declaration and the declaration, or null.</returns>
    protected static async Task<(Document Document, SyntaxNode Node)?> PropertyAsync(Document document, TextSpan span, string name, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var type = model.GetDeclaredSymbol(root.FindNode(span).FirstAncestorOrSelf<TypeDeclarationSyntax>()!, token)!;
        return SourceEdits.Editable(document.Project.Solution, SourceEdits.PropertyOf(type, name).DeclaringSyntaxReferences.FirstOrDefault(), token);
    }

    /// <summary>The ways to fix one diagnostic, in the order of its message, none where the diagnostic gives no safe edit.</summary>
    /// <param name="document">The document of the diagnostic.</param>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>The options.</returns>
    protected abstract Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token);

    private async Task<List<FixOption>> OptionsAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var options = new List<FixOption>();
        foreach (var option in await FixesAsync(document, diagnostic, token).ConfigureAwait(false))
        {
            var editable = true;
            foreach (var id in option.Edits.Select(static edit => edit.Document).Distinct())
            {
                editable &= !SourceEdits.IsGenerated((await document.Project.Solution.GetDocument(id)!.GetSyntaxTreeAsync(token).ConfigureAwait(false))!, token);
            }

            if (editable)
            {
                options.Add(option);
            }
        }

        return options;
    }

    /// <summary>The fix-all: the edits of the option with the equivalence key, of every diagnostic of the scope, in one change of the solution.</summary>
    private sealed class FixAll : FixAllProvider
    {
        public static readonly FixAll Instance = new();

        public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var fix = (TextEditCodeFix)fixAllContext.CodeFixProvider;
            var solution = fixAllContext.Solution;
            var token = fixAllContext.CancellationToken;
            var edits = new List<(DocumentId Document, TextChange Change)>();
            string? title = null;
            foreach (var diagnostic in await DiagnosticsAsync(fixAllContext).ConfigureAwait(false))
            {
                // A diagnostic of a base type in another project of the workspace has no place in this one.
                if (solution.GetDocument(diagnostic.Location.SourceTree) is { } document
                    && (await fix.OptionsAsync(document, diagnostic, token).ConfigureAwait(false)).Find(option => option.Key == fixAllContext.CodeActionEquivalenceKey) is { } option)
                {
                    title ??= option.Title;
                    edits.AddRange(option.Edits);
                }
            }

            return title is null ? null : CodeAction.Create(title, cancellation => SourceEdits.ApplyAsync(solution, edits, cancellation), fixAllContext.CodeActionEquivalenceKey);
        }

        private static async Task<IEnumerable<Diagnostic>> DiagnosticsAsync(FixAllContext context)
        {
            if (context.Scope == FixAllScope.Document)
            {
                return await context.GetDocumentDiagnosticsAsync(context.Document!).ConfigureAwait(false);
            }

            var all = new List<Diagnostic>();
            foreach (var project in context.Solution.Projects.Where(project => context.Scope == FixAllScope.Solution || project.Id == context.Project.Id))
            {
                all.AddRange(await context.GetAllDiagnosticsAsync(project).ConfigureAwait(false));
            }

            return all;
        }
    }
}
