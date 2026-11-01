using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA201 reports what the compiler refuses for a DynaSharp ref struct and says why DynaSharp did this: a value that crosses an <c>await</c> or a
/// <c>yield return</c> (CS4007), an async parameter or, before C# 13, an async local (CS4012, CS9202), and a capture (CS8175, CS9108).
/// </summary>
/// <remarks>
/// <para>
/// The compiler's diagnostics cannot be read instead: CS4007 comes from lowering, after analyzers ran. So this mirrors the rules, as a warning,
/// because a mirror that is wrong must not break a build of code that compiles. CS8345, a field of the type, is a declaration error that stops
/// the compiler before analyzers run, and only the documentation covers it. CS4012 on a method parameter is reported at the same stage: csc
/// never gets here, an IDE does.
/// </para>
/// <para>Most of the work is a look at the type of what is referenced, so that code without a DynaSharp ref struct pays next to nothing.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class CommandLifetimeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.CommandOutlivesExpression];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                start.RegisterOperationAction(AnalyzeLocal, OperationKind.LocalReference);
                start.RegisterOperationAction(AnalyzeParameter, OperationKind.ParameterReference);
                start.RegisterOperationAction(AnalyzeDeclarator, OperationKind.VariableDeclarator);
                start.RegisterOperationAction(AnalyzeAwait, OperationKind.Await);
                start.RegisterOperationAction(AnalyzeFunction, OperationKind.MethodBody, OperationKind.AnonymousFunction, OperationKind.LocalFunction);
            }
        });
    }

    private static void AnalyzeLocal(OperationAnalysisContext context)
    {
        var reference = (ILocalReferenceOperation)context.Operation;
        var local = reference.Local;
        if (!DynaSharpTypes.IsRefStruct(local.Type))
        {
            return;
        }

        if (IsCaptured(reference, local.ContainingSymbol)
            || (local.ContainingSymbol is IMethodSymbol function && (function.IsAsync || function.IsIterator) && CommandFlow.ReadsAcrossBoundary(context.GetControlFlowGraph(), reference)))
        {
            Report(context, reference.Syntax.GetLocation());
        }
    }

    // An iterator keeps its parameters in the state machine from the start, so any use of one is refused; an unused one compiles.
    private static void AnalyzeParameter(OperationAnalysisContext context)
    {
        var reference = (IParameterReferenceOperation)context.Operation;
        var parameter = reference.Parameter;
        if (DynaSharpTypes.IsRefStruct(parameter.Type) && (IsCaptured(reference, parameter.ContainingSymbol) || parameter.ContainingSymbol is IMethodSymbol { IsIterator: true }))
        {
            Report(context, reference.Syntax.GetLocation());
        }
    }

    private static void AnalyzeDeclarator(OperationAnalysisContext context)
    {
        var declarator = (IVariableDeclaratorOperation)context.Operation;
        var local = declarator.Symbol;
        if (DynaSharpTypes.IsRefStruct(local.Type)
            && local.ContainingSymbol is IMethodSymbol { IsAsync: true }
            && ((CSharpParseOptions)declarator.Syntax.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp13)
        {
            Report(context, local.Locations[0]);
        }
    }

    // `await table.Update(k).Set("a", await Value()).SendAsync()`: the command is on the stack while the inner await runs, so the compiler has to
    // spill it, which a ref struct cannot be. The chain is fine; it is the value that has to be computed first.
    private static void AnalyzeAwait(OperationAnalysisContext context)
    {
        var child = context.Operation;
        while (child.Parent is { } parent and not IBlockOperation)
        {
            if (parent is IInvocationOperation invocation && !ReferenceEquals(child, invocation.Instance))
            {
                ReportSpilled(context, invocation, child);
            }

            child = parent;
        }
    }

    private static void ReportSpilled(OperationAnalysisContext context, IInvocationOperation invocation, IOperation argument)
    {
        // The handler of an interpolated string is an implicit receiver; the compiler reports the command that the string belongs to.
        if (invocation.Instance is { IsImplicit: false } instance && DynaSharpTypes.IsRefStruct(instance.Type))
        {
            Report(context, instance.Syntax.GetLocation());
        }

        foreach (var earlier in invocation.Arguments.TakeWhile(earlier => !ReferenceEquals(earlier, argument)))
        {
            if (DynaSharpTypes.IsRefStruct(earlier.Value.Type))
            {
                Report(context, earlier.Value.Syntax.GetLocation());
            }
        }
    }

    private static void AnalyzeFunction(OperationAnalysisContext context)
    {
        var function = context.Operation switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol,
            ILocalFunctionOperation local => local.Symbol,
            _ => (IMethodSymbol)context.ContainingSymbol,
        };
        if (!function.IsAsync)
        {
            return;
        }

        foreach (var parameter in function.Parameters)
        {
            if (DynaSharpTypes.IsRefStruct(parameter.Type))
            {
                Report(context, parameter.Locations[0]);
            }
        }
    }

    // A local belongs to the innermost function around its declaration, so a use is a capture if that is not the innermost function around the use.
    private static bool IsCaptured(IOperation reference, ISymbol owner)
    {
        for (var operation = reference.Parent; operation is not null; operation = operation.Parent)
        {
            var function = operation switch
            {
                IAnonymousFunctionOperation lambda => lambda.Symbol,
                ILocalFunctionOperation local => local.Symbol,
                _ => null,
            };
            if (function is not null)
            {
                return !SymbolEqualityComparer.Default.Equals(function, owner);
            }
        }

        return false;
    }

    private static void Report(OperationAnalysisContext context, Location location) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.CommandOutlivesExpression, location));
}
