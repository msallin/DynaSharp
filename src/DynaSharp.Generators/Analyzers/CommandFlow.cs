using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// Follows one variable through the control flow graph of a member, because what is wrong on one path may be right on the other branch of an <c>if</c>, and a write makes a variable good again. Two
/// rules run on it. The reads of a local that the compiler refuses with CS4007 go by the rule the compiler has: an <c>await</c> or a <c>yield return</c> forgets every local, and a read of a forgotten
/// local on any path would need it preserved. The second use of a command goes by the rule of the handle: a call uses it up, on any path, so a later call on that path throws.
/// </summary>
internal static class CommandFlow
{
    private static readonly Rule Boundary = new(
        static operation => operation.Kind is OperationKind.Await or OperationKind.YieldReturn,
        static _ => true,
        IsOutArgument);

    /// <summary>Whether a read of a local comes after a boundary it cannot cross.</summary>
    /// <param name="graph">The graph of the whole member, which holds the graphs of its lambdas and local functions.</param>
    /// <param name="reference">The reference.</param>
    /// <returns>Whether the compiler would have to preserve the local for this reference.</returns>
    public static bool ReadsAcrossBoundary(ControlFlowGraph graph, ILocalReferenceOperation reference) => Finds(graph, reference, reference.Local, Boundary);

    /// <summary>Whether a call on a command comes after another one on some path, with no write of the variable between them.</summary>
    /// <param name="graph">The graph of the whole member, which holds the graphs of its lambdas and local functions.</param>
    /// <param name="reference">The reference, which <see cref="IsCall"/> found to be a call.</param>
    /// <param name="variable">The local or parameter that holds the command.</param>
    /// <param name="model">The semantic model of the reference. The operations of a graph have none of their own.</param>
    /// <returns>Whether an earlier call has used the command up by here.</returns>
    public static bool IsUsedAgain(ControlFlowGraph graph, IOperation reference, ISymbol variable, SemanticModel model)
    {
        var rule = new Rule(
            operation => IsReferenceTo(operation, variable) && IsCall(operation, model),
            operation => IsCall(operation, model),
            static operation => IsOutArgument(operation) || IsRefArgument(operation));
        return Finds(graph, reference, variable, rule);
    }

    /// <summary>
    /// Whether a reference is the receiver of a call that uses a command up: a call in the library, a helper that returns a command, or an <c>await</c>. A method of the user that returns anything else
    /// may never touch the command. It reads the syntax, because the graph takes a receiver out of its call when an argument branches.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="model">The semantic model.</param>
    /// <returns>Whether the reference is such a receiver.</returns>
    public static bool IsCall(IOperation reference, SemanticModel model)
    {
        var receiver = reference.Syntax;
        while (receiver.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            receiver = parenthesized;
        }

        return receiver.Parent is AwaitExpressionSyntax
            || (receiver.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } access
                && access.Expression == receiver
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && (DynaSharpTypes.IsDeclaredByDynaSharp(method.ContainingType) || DynaSharpTypes.IsCommand(method.ReturnType)));
    }

    private static bool Finds(ControlFlowGraph graph, IOperation reference, ISymbol variable, Rule rule)
    {
        var reads = new HashSet<SyntaxNode>();
        Collect(graph, variable, rule, reads);
        return reads.Contains(reference.Syntax);
    }

    private static void Collect(ControlFlowGraph graph, ISymbol variable, Rule rule, HashSet<SyntaxNode> reads)
    {
        Flow(graph, variable, rule, reads);
        foreach (var function in graph.LocalFunctions)
        {
            Collect(graph.GetLocalFunctionControlFlowGraph(function), variable, rule, reads);
        }

        foreach (var block in graph.Blocks)
        {
            foreach (var lambda in OperationsOf(block).SelectMany(static operation => operation.DescendantsAndSelf()).OfType<IFlowAnonymousFunctionOperation>())
            {
                Collect(graph.GetAnonymousFunctionControlFlowGraph(lambda), variable, rule, reads);
            }
        }
    }

    // The state of a block only ever turns stale, so a read found on the way to the fixed point stays a finding.
    private static void Flow(ControlFlowGraph graph, ISymbol variable, Rule rule, HashSet<SyntaxNode> reads)
    {
        var entry = new bool[graph.Blocks.Length];
        bool changed;
        do
        {
            changed = false;
            foreach (var block in graph.Blocks)
            {
                var stale = entry[block.Ordinal];
                foreach (var operation in OperationsOf(block))
                {
                    Visit(operation, variable, rule, ref stale, reads);
                }

                if (stale)
                {
                    changed |= MarkSuccessors(graph, block, entry);
                }
            }
        }
        while (changed);
    }

    private static bool MarkSuccessors(ControlFlowGraph graph, BasicBlock block, bool[] entry)
    {
        var changed = Mark(entry, block.FallThroughSuccessor?.Destination);
        changed |= Mark(entry, block.ConditionalSuccessor?.Destination);

        // A catch or a finally has no predecessor in the graph: it follows from anywhere in its try. The end of a finally has no destination
        // either: it goes on where the branch that entered it was going, which is how `await using` reaches the code after it.
        var endsFinally = block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling;

        // The walk ends at the root of this graph: above the root of a lambda are the regions of the function around it, whose block numbers mean something else here.
        for (var region = block.EnclosingRegion; region.Kind != ControlFlowRegionKind.Root; region = region.EnclosingRegion!)
        {
            var parent = region.EnclosingRegion!;
            if (region.Kind == ControlFlowRegionKind.Try)
            {
                foreach (var handler in parent.NestedRegions.Where(nested => nested != region))
                {
                    changed |= Mark(entry, graph.Blocks[handler.FirstBlockOrdinal]);
                }
            }
            else if (endsFinally && region.Kind == ControlFlowRegionKind.Finally)
            {
                changed |= MarkAfterFinally(graph, region, entry);
            }
        }

        return changed;
    }

    private static bool MarkAfterFinally(ControlFlowGraph graph, ControlFlowRegion region, bool[] entry)
    {
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            foreach (var branch in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor })
            {
                if (branch?.FinallyRegions.Contains(region) == true)
                {
                    changed |= Mark(entry, branch.Destination);
                }
            }
        }

        return changed;
    }

    private static bool Mark(bool[] entry, BasicBlock? block)
    {
        if (block is null || entry[block.Ordinal])
        {
            return false;
        }

        entry[block.Ordinal] = true;
        return true;
    }

    private static IEnumerable<IOperation> OperationsOf(BasicBlock block) => block.BranchValue is { } value ? block.Operations.Append(value) : block.Operations;

    // Children come in evaluation order, and an operation happens after its children: the await of `await cmd.SendAsync()` follows the read of cmd.
    private static void Visit(IOperation operation, ISymbol variable, Rule rule, ref bool stale, HashSet<SyntaxNode> reads)
    {
        if (operation is ISimpleAssignmentOperation assignment && Writes(assignment.Target, variable))
        {
            Visit(assignment.Value, variable, rule, ref stale, reads);
            stale = false;
            return;
        }

        if (operation is IDeconstructionAssignmentOperation deconstruction)
        {
            var writes = VisitTargets(deconstruction.Target, variable, rule, ref stale, reads);
            Visit(deconstruction.Value, variable, rule, ref stale, reads);
            stale = stale && !writes;
            return;
        }

        foreach (var child in operation.ChildOperations)
        {
            Visit(child, variable, rule, ref stale, reads);
        }

        if (IsReferenceTo(operation, variable))
        {
            if (rule.Clears(operation))
            {
                stale = false;
            }
            else if (stale && rule.Counts(operation))
            {
                reads.Add(operation.Syntax);
            }
        }

        stale |= rule.Spoils(operation);

        // These read the variable before they write it, which the children above have seen.
        if (IsWrittenInPlace(operation, variable))
        {
            stale = false;
        }
    }

    // What a target holds besides the variable is evaluated before the value: in (q, a[i]) = ..., i is read and q is written.
    private static bool VisitTargets(IOperation target, ISymbol variable, Rule rule, ref bool stale, HashSet<SyntaxNode> reads)
    {
        if (target is IDeclarationExpressionOperation declaration)
        {
            return VisitTargets(declaration.Expression, variable, rule, ref stale, reads);
        }

        if (target is ITupleOperation tuple)
        {
            var writes = false;
            foreach (var element in tuple.Elements)
            {
                writes |= VisitTargets(element, variable, rule, ref stale, reads);
            }

            return writes;
        }

        if (Writes(target, variable))
        {
            return true;
        }

        Visit(target, variable, rule, ref stale, reads);
        return false;
    }

    private static bool IsWrittenInPlace(IOperation operation, ISymbol variable) => operation switch
    {
        ICompoundAssignmentOperation compound => Writes(compound.Target, variable),
        IIncrementOrDecrementOperation step => Writes(step.Target, variable),
        _ => false,
    };

    // When the value branches or holds an initializer, the graph takes the target out of the assignment first, and then only the syntax still names it.
    private static bool Writes(IOperation target, ISymbol variable) =>
        IsReferenceTo(target, variable)
        || (target is IFlowCaptureReferenceOperation { Syntax: IdentifierNameSyntax name } && name.Identifier.ValueText == variable.Name);

    private static bool IsReferenceTo(IOperation operation, ISymbol variable) => operation switch
    {
        ILocalReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Local, variable),
        IParameterReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Parameter, variable),
        _ => false,
    };

    private static bool IsOutArgument(IOperation reference) =>
        reference.Syntax.Parent is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);

    private static bool IsRefArgument(IOperation reference) =>
        reference.Syntax.Parent is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword);

    /// <summary>What a rule looks for in the flow of one variable.</summary>
    /// <param name="Spoils">Whether an operation makes the variable stale.</param>
    /// <param name="Counts">Whether a reference to a stale variable is a finding.</param>
    /// <param name="Clears">Whether a reference to the variable writes it, as an assignment does.</param>
    private sealed record Rule(Func<IOperation, bool> Spoils, Func<IOperation, bool> Counts, Func<IOperation, bool> Clears);
}
