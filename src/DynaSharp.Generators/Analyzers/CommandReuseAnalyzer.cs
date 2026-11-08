using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA204: <c>var a = q.Limit(1); var b = q.Limit(2);</c> and <c>_ = command.ToRequestJson(); await command.SendAsync();</c>. Every call uses up its handle and gives the next one back, so a second call
/// on the same variable throws <see cref="System.InvalidOperationException"/> at run time. <see cref="CommandFlow"/> follows the variable through the branches and loops of the member.
/// </summary>
/// <remarks>
/// A use on any path counts, because that is what fails: the second call on a path behind an <c>if (debug)</c> throws when someone turns debug on. A warning, because a graph does not know that two
/// conditions go together, and a copy of a handle in a second variable is not followed. Passing a command to a method is no use: it may well give it back.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class CommandReuseAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.CommandReused];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                start.RegisterOperationAction(Analyze, OperationKind.LocalReference, OperationKind.ParameterReference);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var reference = context.Operation;
        var model = reference.SemanticModel!;
        if (DynaSharpTypes.IsCommand(reference.Type) && CommandFlow.IsCall(reference, model) && CommandFlow.IsUsedAgain(context.GetControlFlowGraph(), reference, VariableOf(reference), model))
        {
            var edits = AssignBack(reference.Syntax, VariableOf(reference), reference.Type!, model, context.CancellationToken);
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.CommandReused, reference.Syntax.GetLocation(), edits));
        }
    }

    /// <summary>
    /// <c>q.Limit(1);</c> or <c>_ = q.Limit(1);</c> becomes <c>q = q.Limit(1);</c>, where that statement is the last mention of the variable before this one, in a block this one is in, and what it
    /// gives fits the variable. Anything else, a result kept in another variable or a send, is a choice of the author.
    /// </summary>
    private static List<SuggestedEdit> AssignBack(SyntaxNode reference, ISymbol variable, ITypeSymbol type, SemanticModel model, CancellationToken token)
    {
        var earlier = reference.SyntaxTree.GetRoot(token).DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .LastOrDefault(name => name.SpanStart < reference.SpanStart && name.Identifier.ValueText == variable.Name && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, token).Symbol, variable));
        if (earlier?.Parent is not MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax } access)
        {
            return [];
        }

        var value = Outermost(access);
        var name = earlier.Identifier.Text;
        var (statement, span, text) = value.Parent switch
        {
            ExpressionStatementSyntax alone => (alone, new TextSpan(value.SpanStart, 0), name + " = "),
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "_" } discard, Parent: ExpressionStatementSyntax discarded } => (discarded, discard.Span, name),
            _ => default,
        };
        if (statement is not { Parent: BlockSyntax block }
            || !block.Span.Contains(reference.Span)
            || variable is IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter }
            || !model.ClassifyConversion(value, type).IsImplicit)
        {
            return [];
        }

        return [new SuggestedEdit("Assign the result back to " + name, span, text)];
    }

    // The whole chain that starts at a call on the variable: q.Limit(1).Descending().
    private static ExpressionSyntax Outermost(MemberAccessExpressionSyntax access)
    {
        ExpressionSyntax chain = (InvocationExpressionSyntax)access.Parent!;
        while (chain.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax next })
        {
            chain = next;
        }

        return chain;
    }

    private static ISymbol VariableOf(IOperation reference) => reference is ILocalReferenceOperation local ? local.Local : ((IParameterReferenceOperation)reference).Parameter;
}
