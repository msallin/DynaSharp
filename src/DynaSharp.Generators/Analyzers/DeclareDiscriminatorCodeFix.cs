using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of DYNA020, the three ways its message names: <c>[Discriminator("ORDER")]</c> with the value the generator suggested, <c>[Discriminator(OptOut = true)]</c>, both next to
/// <c>[DynamoDbEntity]</c>, and <c>DiscriminatorOptOut = true</c> on the <c>[assembly: DynamoDbDefaults]</c> of the project, which is added if there is none. Only an IDE loads this class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DeclareDiscriminatorCodeFix))]
[Shared]
internal sealed class DeclareDiscriminatorCodeFix : CodeFixProvider
{
    /// <summary>The equivalence key of the fix that adds the suggested value, the same for every entity, so a fix-all gives each its own.</summary>
    internal const string ValueKey = "DYNA020.Value";

    /// <summary>The equivalence key of the fix that opts the type out.</summary>
    internal const string OptOutKey = "DYNA020.OptOut";

    /// <summary>The equivalence key of the fix that opts the project out, which fixes every DYNA020 of the project at once.</summary>
    internal const string ProjectKey = "DYNA020.Project";

    private const string ProjectTitle = "Opt out every entity of the project with [assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]";

    private const string OptOutArgument = "DiscriminatorOptOut";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.NoDiscriminator.Id];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => FixAll.Instance;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var editable = !SourceEdits.IsGenerated((await document.GetSyntaxTreeAsync(context.CancellationToken).ConfigureAwait(false))!, context.CancellationToken);
        var project = await TargetAsync(document.Project, editable ? document : null, context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            if (editable)
            {
                var value = SyntaxFactory.Literal(diagnostic.Properties[DiagnosticInfo.FixProperty]!).Text;
                Register(context, diagnostic, "Add [Discriminator(" + value + ")]", ValueKey, token => DeclareAsync(document, span, "(" + value + ")", token));
                Register(context, diagnostic, "Add [Discriminator(OptOut = true)]", OptOutKey, token => DeclareAsync(document, span, "(OptOut = true)", token));
            }

            if (project is { } target)
            {
                context.RegisterCodeFix(CodeAction.Create(ProjectTitle, token => OptOutAsync(document.Project.Solution, target, token), ProjectKey), diagnostic);
            }
        }
    }

    private static void Register(CodeFixContext context, Diagnostic diagnostic, string title, string key, Func<CancellationToken, Task<Document>> fix) =>
        context.RegisterCodeFix(CodeAction.Create(title, fix, key), diagnostic);

    private static async Task<Document> DeclareAsync(Document document, Microsoft.CodeAnalysis.Text.TextSpan span, string arguments, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var type = root.FindToken(span.Start).Parent!.FirstAncestorOrSelf<TypeDeclarationSyntax>()!;
        var entity = type.AttributeLists.SelectMany(list => list.Attributes).First(attribute => IsAttribute(model, attribute, WellKnownNames.EntityAttribute));
        var probe = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Discriminator"), SyntaxFactory.ParseAttributeArgumentList(arguments));
        return await SourceEdits.ApplyAsync(document, [SourceEdits.InsertAfter(entity, NameAt(model, entity, probe) + arguments)], token).ConfigureAwait(false);
    }

    // The short name where it binds to DynaSharp's attribute. Otherwise the file names the attributes of DynaSharp in full, as it does [DynamoDbEntity], or another type takes the name.
    private static string NameAt(SemanticModel model, AttributeSyntax entity, AttributeSyntax probe)
    {
        if (Binds(model, entity.SpanStart, probe, WellKnownNames.DiscriminatorAttribute))
        {
            return "Discriminator";
        }

        return entity.Name is QualifiedNameSyntax qualified ? qualified.Left.ToString() + ".Discriminator" : "global::DynaSharp.Mapping.Discriminator";
    }

    private static bool IsAttribute(SemanticModel model, AttributeSyntax attribute, string name) => model.GetSymbolInfo(attribute).Symbol?.ContainingType.ToDisplayString() == name;

    private static bool Binds(SemanticModel model, int position, AttributeSyntax attribute, string name) =>
        model.GetSpeculativeSymbolInfo(position, attribute).Symbol?.ContainingType.ToDisplayString() == name;

    /// <summary>
    /// Where the opt-out of a project goes: the <c>[assembly: DynamoDbDefaults]</c> it has, else beside the other attributes of the assembly in source, else into the document of the entity.
    /// Null when that place cannot be edited, since a second <c>DynamoDbDefaults</c> would not compile.
    /// </summary>
    private static async Task<Target?> TargetAsync(Project project, Document? entity, CancellationToken token)
    {
        var compilation = (await project.GetCompilationAsync(token).ConfigureAwait(false))!;
        if (AttributeReader.Find(compilation.Assembly, WellKnownNames.DefaultsAttribute)?.ApplicationSyntaxReference is { } defaults)
        {
            return SourceEdits.Editable(project.Solution, defaults.SyntaxTree, token) is { } document ? new Target(document.Id, defaults.Span) : null;
        }

        var beside = compilation.SyntaxTrees
            .Where(tree => ((CompilationUnitSyntax)tree.GetRoot(token)).AttributeLists.Count > 0)
            .Select(tree => SourceEdits.Editable(project.Solution, tree, token))
            .Where(document => document is not null)
            .OrderBy(document => document!.FilePath, StringComparer.Ordinal)
            .FirstOrDefault();
        return (beside ?? entity) is { } place ? new Target(place.Id, null) : null;
    }

    private static async Task<Solution> OptOutAsync(Solution solution, Target target, CancellationToken token)
    {
        var document = solution.GetDocument(target.Document)!;
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        if (target.Defaults is { } span)
        {
            var defaults = (AttributeSyntax)root.FindNode(span);
            return solution.WithDocumentSyntaxRoot(document.Id, root.ReplaceNode(defaults, SourceEdits.WithNamedArgument(defaults, OptOutArgument, "true")));
        }

        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var position = root.Members.FirstOrDefault()?.SpanStart ?? root.EndOfFileToken.SpanStart;
        var list = SyntaxFactory.ParseCompilationUnit("[assembly: DynamoDbDefaults(" + OptOutArgument + " = true)]").AttributeLists[0];
        var attribute = list.Attributes[0];
        if (!Binds(model, position, attribute, WellKnownNames.DefaultsAttribute))
        {
            list = list.ReplaceNode(attribute.Name, SyntaxFactory.ParseName("DynaSharp.Mapping.DynamoDbDefaults"));
        }

        var lineEnd = root.DescendantTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        lineEnd = lineEnd == default ? SyntaxFactory.LineFeed : lineEnd;

        // After the attributes of the assembly the file has, or after its usings with a blank line between, or at the top with one before the rest.
        if (root.AttributeLists.Count > 0)
        {
            list = list.WithTrailingTrivia(lineEnd);
        }
        else if (root.Usings.Count > 0)
        {
            list = list.WithLeadingTrivia(lineEnd).WithTrailingTrivia(lineEnd);
        }
        else
        {
            list = list.WithTrailingTrivia(lineEnd, lineEnd);
        }

        return solution.WithDocumentSyntaxRoot(document.Id, root.AddAttributeLists(list));
    }

    /// <summary>Where the opt-out of a project goes: a document, and the span of its <c>[assembly: DynamoDbDefaults]</c>, or null to add one.</summary>
    private sealed record Target(DocumentId Document, Microsoft.CodeAnalysis.Text.TextSpan? Defaults);

    /// <summary>
    /// The fix-all: a batch of the fixes of each entity, except the opt-out of a project, which is one edit per project, however many entities it fixes, so that no second
    /// <c>DynamoDbDefaults</c> is added.
    /// </summary>
    private sealed class FixAll : FixAllProvider
    {
        public static readonly FixAll Instance = new();

        public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            if (fixAllContext.CodeActionEquivalenceKey != ProjectKey)
            {
                return WellKnownFixAllProviders.BatchFixer.GetFixAsync(fixAllContext);
            }

            return Task.FromResult<CodeAction?>(CodeAction.Create(ProjectTitle, token => OptOutAllAsync(fixAllContext, token), ProjectKey));
        }

        private static async Task<Solution> OptOutAllAsync(FixAllContext context, CancellationToken token)
        {
            var solution = context.Solution;
            foreach (var project in context.Solution.Projects.Where(project => context.Scope == FixAllScope.Solution || project.Id == context.Project.Id))
            {
                var diagnostics = await context.GetAllDiagnosticsAsync(project).ConfigureAwait(false);
                var entity = diagnostics
                    .Select(diagnostic => SourceEdits.Editable(project.Solution, diagnostic.Location.SourceTree, token))
                    .Where(document => document is not null)
                    .OrderBy(document => document!.FilePath, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (!diagnostics.IsEmpty && await TargetAsync(solution.GetProject(project.Id)!, entity, token).ConfigureAwait(false) is { } target)
                {
                    solution = await OptOutAsync(solution, target, token).ConfigureAwait(false);
                }
            }

            return solution;
        }
    }
}
