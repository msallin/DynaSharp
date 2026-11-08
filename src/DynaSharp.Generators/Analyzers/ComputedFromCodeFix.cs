using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The fixes of <c>[ComputedFrom]</c>. DYNA114: a computed property in an index gets <c>[ComputedFrom(nameof(First), nameof(Last))]</c> with the stored properties of the same instance its getter
/// reads, of those the generator says it can name; a getter that reads none is offered nothing. DYNA032: the property the getter reads is added to the <c>[ComputedFrom]</c> it has.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ComputedFromCodeFix))]
[Shared]
internal sealed class ComputedFromCodeFix : TextEditCodeFix
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [Descriptors.ComputedInIndex.Id, Descriptors.UnnamedComputedSource.Id];

    /// <inheritdoc/>
    protected override Task<IEnumerable<FixOption>> FixesAsync(Document document, Diagnostic diagnostic, CancellationToken token) =>
        diagnostic.Id == Descriptors.ComputedInIndex.Id ? DeclareAsync(document, diagnostic, token) : NameAsync(document, diagnostic, token);

    private static async Task<IEnumerable<FixOption>> DeclareAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var fix = FixOf(diagnostic)!;
        if (await PropertyAsync(document, diagnostic.Location.SourceSpan, fix[0], token).ConfigureAwait(false) is not { Node: PropertyDeclarationSyntax property } found)
        {
            return [];
        }

        var model = (await found.Document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var getter = (SyntaxNode?)property.ExpressionBody
            ?? property.AccessorList!.Accessors.Select(static accessor => (SyntaxNode?)accessor.ExpressionBody ?? accessor.Body).FirstOrDefault(static body => body is not null);
        var nameable = fix.Skip(1).ToList();
        var reads = getter is null
            ? []
            : model.GetOperation(getter, token)!.DescendantsAndSelf()
                .OfType<IPropertyReferenceOperation>()
                .Where(static reference => ComputedGetterAnalyzer.IsOfThisInstance(reference) && !ComputedGetterAnalyzer.IsInNameOf(reference))
                .Select(static reference => reference.Property.Name)
                .Where(nameable.Contains)
                .Distinct()
                .ToList();
        if (reads.Count == 0)
        {
            return [];
        }

        var arguments = "(" + string.Join(", ", reads.Select(static name => "nameof(" + name + ")")) + ")";
        var name = SourceEdits.AttributeName(model, property.SpanStart, WellKnownNames.ComputedFromAttribute);
        var text = await found.Document.GetTextAsync(token).ConfigureAwait(false);
        return [new FixOption("Add [ComputedFrom" + arguments + "]", Descriptors.ComputedInIndex.Id, [(found.Document.Id, SourceEdits.AddAttributeList(text, property, "[" + name + arguments + "]"))])];
    }

    private static async Task<IEnumerable<FixOption>> NameAsync(Document document, Diagnostic diagnostic, CancellationToken token)
    {
        var root = (await document.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(token).ConfigureAwait(false))!;
        var property = model.GetDeclaredSymbol(root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<PropertyDeclarationSyntax>()!, token)!;

        // An override may take the attribute of the property it overrides, which a compiled base holds where no edit reaches.
        if (AttributeReader.FindOnProperty(property, WellKnownNames.ComputedFromAttribute)!.ApplicationSyntaxReference is not { } reference)
        {
            return [];
        }

        var attribute = (AttributeSyntax)await reference.GetSyntaxAsync(token).ConfigureAwait(false);
        var name = FixOf(diagnostic)![0];
        var change = new TextChange(new TextSpan(attribute.ArgumentList!.CloseParenToken.SpanStart, 0), ", nameof(" + name + ")");
        var target = document.Project.Solution.GetDocument(reference.SyntaxTree)!;
        return [new FixOption("Add nameof(" + name + ") to [ComputedFrom]", Descriptors.UnnamedComputedSource.Id, [(target.Id, change)])];
    }
}
