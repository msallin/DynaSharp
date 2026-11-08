using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA227: <c>Value</c> of a <c>SelectResult&lt;T&gt;</c> in a member that never reads its <c>Found</c>, as in <c>(await orders.Get(key).Select(Order.Attrs.Total)).Value</c>. <c>Value</c> throws
/// <c>InvalidOperationException</c> when the read found no item, which is a normal answer of DynamoDB, so code that never asks reads like code that forgot to.
/// </summary>
/// <remarks>
/// It is Info, because code that knows the item exists, one it has just written, is correct and looks the same. The test is by member and not by flow: a <c>Found</c> of any <c>SelectResult</c> anywhere in the
/// member, a property pattern such as <c>{ Found: true, Value: var total }</c> or a lambda in it included, keeps it quiet, so a guard clause, a check on another variable or a result handed to a helper that checks
/// it in a member that also asks is never reported. A reference of <c>Value</c> of this type is what starts it, and that walks the operations of its member once.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class SelectResultAnalyzer : DiagnosticAnalyzer
{
    private const string ValueProperty = "Value";

    private const string FoundProperty = "Found";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.ValueWithoutFound];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.SelectResult) is { } selectResult)
            {
                start.RegisterOperationAction(context => Analyze(context, selectResult), OperationKind.PropertyReference);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol selectResult)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (IsMember(reference, ValueProperty, selectResult) && !IsInNameOf(reference) && !AsksFound(MemberOf(reference), selectResult))
        {
            var location = reference.Syntax is MemberAccessExpressionSyntax access ? access.Name.GetLocation() : reference.Syntax.GetLocation();
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.ValueWithoutFound, location, EditsOf(reference.Syntax)));
        }
    }

    // result.Value: GetValueOrDefault(), and a test of Found where the result is a name, which reads the same twice. A property pattern has no fix.
    private static List<SuggestedEdit> EditsOf(SyntaxNode syntax)
    {
        if (syntax is not MemberAccessExpressionSyntax access)
        {
            return [];
        }

        List<SuggestedEdit> edits = [new("Use GetValueOrDefault()", access.Name.Span, "GetValueOrDefault()")];
        if (access.Expression is IdentifierNameSyntax result)
        {
            var test = result.Identifier.Text + "." + FoundProperty + " ? " + result.Identifier.Text + "." + ValueProperty + " : default";
            var text = access.Parent is ArgumentSyntax or EqualsValueClauseSyntax ? test : "(" + test + ")";
            edits.Add(new("Use " + test, access.Span, text));
        }

        return edits;
    }

    private static bool IsMember(IPropertyReferenceOperation reference, string name, INamedTypeSymbol selectResult) =>
        reference.Property.Name == name && SymbolEqualityComparer.Default.Equals(reference.Property.ContainingType.OriginalDefinition, selectResult);

    private static bool IsInNameOf(IOperation reference)
    {
        for (var around = reference.Parent; around is not null; around = around.Parent)
        {
            if (around is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation MemberOf(IOperation reference)
    {
        var member = reference;
        while (member.Parent is { } parent)
        {
            member = parent;
        }

        return member;
    }

    private static bool AsksFound(IOperation member, INamedTypeSymbol selectResult) =>
        member.DescendantsAndSelf().Any(operation => operation is IPropertyReferenceOperation reference && IsMember(reference, FoundProperty, selectResult));
}
