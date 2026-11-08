using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA239 and DYNA236, the clauses their messages name, right after the call they report: <c>locks.Update(key)</c> becomes <c>locks.Update(key).IgnoreVersion()</c> or
/// <c>locks.Update(key).IfVersion(version)</c>, also on a step of a transaction, and <c>locks.BatchWrite().Put(a)</c> becomes <c>locks.BatchWrite().Put(a).IgnoreVersion()</c>. Only an IDE loads this class.
/// </summary>
/// <remarks>
/// Only the caller knows the version it read, so <c>IfVersion</c> gets a name that nothing in scope has, <c>version</c> unless that is taken: the code does not compile until the author puts the
/// version there, where a local that happened to be called so would compile with a value nobody chose.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SettleVersionCodeFix))]
[Shared]
internal sealed class SettleVersionCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that adds <c>IgnoreVersion()</c> to a write by key.</summary>
    internal const string IgnoreKey = "DYNA239.IgnoreVersion";

    /// <summary>The equivalence key of the fix that adds <c>IfVersion(version)</c> to a write by key.</summary>
    internal const string IfVersionKey = "DYNA239.IfVersion";

    /// <summary>The equivalence key of the fix that adds <c>IgnoreVersion()</c> to a batch.</summary>
    internal const string BatchKey = "DYNA236.IgnoreVersion";

    private const string IgnoreVersion = "IgnoreVersion()";

    // The names the placeholder of IfVersion tries, the first one free at the call, and then the last with a number.
    private static readonly string[] Placeholders = ["version", "expectedVersion"];

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.UnsettledVersionByKey.Id, Descriptors.VersionedBatchWrite.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var model = (await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false))!;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id == Descriptors.VersionedBatchWrite.Id)
            {
                Register(context, diagnostic, "Add .IgnoreVersion() to the batch", BatchKey, IgnoreVersion);
                continue;
            }

            var placeholder = FreeName(model, diagnostic.Location.SourceSpan.Start);
            Register(context, diagnostic, "Add .IgnoreVersion()", IgnoreKey, IgnoreVersion);
            Register(context, diagnostic, "Add .IfVersion(" + placeholder + ")", IfVersionKey, "IfVersion(" + placeholder + ")");
        }
    }

    private static void Register(CodeFixContext context, Diagnostic diagnostic, string title, string key, string call)
    {
        var document = context.Document;
        var span = diagnostic.Location.SourceSpan;
        context.RegisterCodeFix(CodeAction.Create(title, token => AppendAsync(document, span, call, token), key), diagnostic);
    }

    private static string FreeName(SemanticModel model, int position)
    {
        var name = Placeholders.FirstOrDefault(candidate => model.LookupSymbols(position, name: candidate).IsEmpty);
        for (var number = 2; name is null; number++)
        {
            var candidate = Placeholders[Placeholders.Length - 1] + number;
            name = model.LookupSymbols(position, name: candidate).IsEmpty ? candidate : null;
        }

        return name;
    }

    // The diagnostic is on the name of the verb or of the write of the batch, whose call the clause follows.
    private static async Task<Document> AppendAsync(Document document, TextSpan span, string call, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var write = root.FindNode(span, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>()!;
        return document.WithSyntaxRoot(root.ReplaceNode(write, ChainSyntax.Append(write, [call])));
    }
}
