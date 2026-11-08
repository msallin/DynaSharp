using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>The fixes of DYNA013, one for each of the two attributes: <c>[DynamoDbEntity]</c> goes, or <c>[DynamoDbDocument]</c> does, from whichever part of the type has it. Only an IDE loads this class.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityOrDocumentCodeFix))]
[Shared]
internal sealed class EntityOrDocumentCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that keeps the type an entity.</summary>
    internal const string EntityKey = "DYNA013.Entity";

    /// <summary>The equivalence key of the fix that keeps the type a document.</summary>
    internal const string DocumentKey = "DYNA013.Document";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.EntityAndDocument.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var solution = document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            var type = model.GetDeclaredSymbol(SourceEdits.NodeAt(root, diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>()!, token)!;
            Register(context, diagnostic, solution, type, WellKnownNames.EntityAttribute, "Remove [DynamoDbEntity]", DocumentKey);
            Register(context, diagnostic, solution, type, WellKnownNames.DocumentAttribute, "Remove [DynamoDbDocument]", EntityKey);
        }
    }

    private static void Register(CodeFixContext context, Diagnostic diagnostic, Solution solution, INamedTypeSymbol type, string attribute, string title, string key)
    {
        if (SourceEdits.RemoveAttribute(solution, AttributeReader.Find(type, attribute)!.ApplicationSyntaxReference, context.CancellationToken) is { } removal)
        {
            context.RegisterCodeFix(CodeAction.Create(title, edit => SourceEdits.ApplyAsync(solution, [removal], edit), key), diagnostic);
        }
    }
}
