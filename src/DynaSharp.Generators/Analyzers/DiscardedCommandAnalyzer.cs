using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA203: <c>events.Put(e);</c> and <c>query.Descending();</c>. A call that gives a command back is the only thing that goes on: the command is never sent, and the handle the call was made on is
/// used up, so the next use of it throws. The compiler has at most <c>CS4014</c> to say, in an <c>async</c> method, on a command that can be awaited.
/// </summary>
/// <remarks>
/// It looks at the value of a statement and at nothing else: an assignment, a <c>return</c> and an <c>await</c> are other operations, and <c>_ = command</c> is someone saying so. An error, because the
/// code does nothing that it says, and a value of such a type has no other use. An expression body of a <c>void</c> member or lambda is a statement to the compiler, and is one here.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class DiscardedCommandAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.CommandDiscarded];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                start.RegisterOperationAction(Analyze, OperationKind.ExpressionStatement);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        if (((IExpressionStatementOperation)context.Operation).Operation is IInvocationOperation invocation && DynaSharpTypes.IsCommand(invocation.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.CommandDiscarded, invocation.Syntax.GetLocation()));
        }
    }
}
