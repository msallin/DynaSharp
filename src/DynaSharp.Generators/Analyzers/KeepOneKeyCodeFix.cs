using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA009, one for each declaration of the key: it stays, on the type or on its property, and the others go. The diagnostic finds the entity by the id of the type in its property
/// <c>Fix</c>, since a declaration may be on a property of a base type. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(KeepOneKeyCodeFix))]
[Shared]
internal sealed class KeepOneKeyCodeFix : CodeFixProvider
{
    /// <summary>The start of the equivalence key of each fix, which ends with the position of the declaration it keeps, the type's first.</summary>
    internal const string KeyPrefix = "DYNA009.";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.DuplicateKey.Id];

    /// <inheritdoc/>
    /// <remarks>None: each fix chooses a declaration of one key.</remarks>
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var token = context.CancellationToken;
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var compilation = (await document.Project.GetCompilationAsync(token).ConfigureAwait(false))!;
        var solution = document.Project.Solution;
        foreach (var diagnostic in context.Diagnostics)
        {
            // A declaration in a library stands on the type, which tells nothing of the key.
            if (SourceEdits.AttributeAt(root, diagnostic.Location.SourceSpan) is not { } at)
            {
                continue;
            }

            var key = compilation.GetSemanticModel(root.SyntaxTree).GetSymbolInfo(at, token).Symbol!.ContainingType;
            var type = (INamedTypeSymbol)DocumentationCommentId.GetFirstSymbolForDeclarationId(diagnostic.Properties[DiagnosticInfo.FixProperty]!, compilation)!;
            var declarations = Declarations(type, key, compilation);
            for (var index = 0; index < declarations.Count; index++)
            {
                var kept = declarations[index];
                var removals = declarations.Where(other => other.Attribute != kept.Attribute).Select(other => SourceEdits.RemoveAttribute(solution, other.Attribute.ApplicationSyntaxReference, token)).ToList();
                if (removals.All(static removal => removal is not null))
                {
                    var changes = removals.Select(static removal => removal!.Value).ToList();
                    var title = "Keep only the [" + SourceEdits.ShortName(key) + "] on " + kept.Owner;
                    context.RegisterCodeFix(CodeAction.Create(title, edit => SourceEdits.ApplyAsync(solution, changes, edit), KeyPrefix + index), diagnostic);
                }
            }
        }
    }

    // The declarations of the key as the generator reads them: on the type, then on each property it maps, by the name of their owner.
    private static List<(string Owner, AttributeData Attribute)> Declarations(INamedTypeSymbol type, INamedTypeSymbol key, Compilation compilation)
    {
        var name = key.ToDisplayString();
        var onProperties = PropertyCollector.Collect(type, compilation, default)
            .Select(property => (property.Name, Attribute: AttributeReader.FindOnProperty(property.Symbol, name)))
            .Where(static pair => pair.Attribute is not null)
            .Select(static pair => (pair.Name, pair.Attribute!));
        return [.. (AttributeReader.Find(type, name) is { } onType ? [(type.Name, onType)] : Enumerable.Empty<(string, AttributeData)>()).Concat(onProperties)];
    }
}
