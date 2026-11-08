using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>The fix of DYNA043: <c>public Keys Key { get; set; }</c> becomes <c>public global::Shop.Keys Key { get; set; }</c>, the name in full the message gives. Only an IDE loads this class.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NameInFullCodeFix))]
[Shared]
internal sealed class NameInFullCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key, the same for every name, so a fix-all writes each in full.</summary>
    internal const string Key = "DYNA043.InFull";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NameTakenOverByGeneratedClass.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        if (SourceEdits.IsGenerated((await document.GetSyntaxTreeAsync(context.CancellationToken).ConfigureAwait(false))!, context.CancellationToken))
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var full = diagnostic.Properties[DiagnosticInfo.FixProperty]!;
            var change = new TextChange(diagnostic.Location.SourceSpan, full);
            context.RegisterCodeFix(CodeAction.Create("Write " + full, token => SourceEdits.ApplyAsync(document, [change], token), Key), diagnostic);
        }
    }
}
