using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.CommandReused, reference.Syntax.GetLocation()));
        }
    }

    private static ISymbol VariableOf(IOperation reference) => reference is ILocalReferenceOperation local ? local.Local : ((IParameterReferenceOperation)reference).Parameter;
}
