using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA034, the two ways its message names: <c>private set</c> of the property of the base type becomes <c>protected set</c>, or the property gets <c>[DynamoDbIgnore]</c>.
/// Only a public property is mapped without <c>[DynamoDbAttribute]</c>, and one with it is not reported, so <c>protected</c> is always more restrictive than the property. The diagnostic
/// stands on the derived type and finds the property by its id in its property <c>Fix</c>. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnreachableSetterCodeFix))]
[Shared]
internal sealed class UnreachableSetterCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that makes the setter protected.</summary>
    internal const string ProtectedKey = "DYNA034.Protected";

    /// <summary>The equivalence key of the fix that ignores the property.</summary>
    internal const string IgnoreKey = "DYNA034.Ignore";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.UnreachableSetter.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var compilation = (await document.Project.GetCompilationAsync(token).ConfigureAwait(false))!;
        var solution = document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            var property = (IPropertySymbol)DocumentationCommentId.GetFirstSymbolForDeclarationId(diagnostic.Properties[DiagnosticInfo.FixProperty]!, compilation)!;
            if (SourceEdits.Editable(solution, property.SetMethod!.DeclaringSyntaxReferences.FirstOrDefault(), token) is { } setter)
            {
                var changes = SourceEdits.ReplaceAccessibility(await setter.Node.SyntaxTree.GetTextAsync(token).ConfigureAwait(false), ((AccessorDeclarationSyntax)setter.Node).Modifiers, "protected").Select(change => (setter.Document.Id, change)).ToList();
                context.RegisterCodeFix(CodeAction.Create("Make the setter of " + property.Name + " protected", edit => SourceEdits.ApplyAsync(solution, changes, edit), ProtectedKey), diagnostic);
            }

            if (SourceEdits.Editable(solution, property.DeclaringSyntaxReferences.FirstOrDefault(), token) is { } declaration)
            {
                var change = (declaration.Document.Id, SourceEdits.AddAttribute(compilation.GetSemanticModel(declaration.Node.SyntaxTree), declaration.Node, WellKnownNames.IgnoreAttribute, null, token));
                context.RegisterCodeFix(CodeAction.Create("Add [DynamoDbIgnore] to " + property.Name, edit => SourceEdits.ApplyAsync(solution, [change], edit), IgnoreKey), diagnostic);
            }
        }
    }
}
