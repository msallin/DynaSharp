using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA021, one for each of the two attributes: the <c>[DynamoDbIgnore]</c> of the property goes, or the key attribute the diagnostic stands on does. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IgnoredKeyCodeFix))]
[Shared]
internal sealed class IgnoredKeyCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that removes <c>[DynamoDbIgnore]</c>.</summary>
    internal const string IgnoreKey = "DYNA021.Ignore";

    /// <summary>The equivalence key of the fix that removes the key attribute.</summary>
    internal const string KeyKey = "DYNA021.Key";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.IgnoredPlainKey.Id];

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
            // The key attribute, on a property or on the parameter of a positional record; the type where a base type in a library declares it. An override may be the one that ignores it.
            if (SourceEdits.AttributeAt(root, diagnostic.Location.SourceSpan) is not { } key)
            {
                continue;
            }

            var property = model.GetDeclaredSymbol(key.Parent!.Parent!, token) switch
            {
                IParameterSymbol parameter => SourceEdits.PropertyOf(parameter.ContainingType, parameter.Name),
                var symbol => (IPropertySymbol)symbol!,
            };
            if (SourceEdits.RemoveAttribute(solution, AttributeReader.FindOnProperty(property, WellKnownNames.IgnoreAttribute)?.ApplicationSyntaxReference, token) is { } ignore)
            {
                context.RegisterCodeFix(CodeAction.Create("Remove [DynamoDbIgnore]", edit => SourceEdits.ApplyAsync(solution, [ignore], edit), IgnoreKey), diagnostic);
            }

            if (SourceEdits.RemoveAttribute(solution, key.GetReference(), token) is { } removal)
            {
                var name = SourceEdits.ShortName(model.GetSymbolInfo(key, token).Symbol!.ContainingType);
                context.RegisterCodeFix(CodeAction.Create("Remove [" + name + "]", edit => SourceEdits.ApplyAsync(solution, [removal], edit), KeyKey), diagnostic);
            }
        }
    }
}
