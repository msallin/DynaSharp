using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// Finds the reads of a local that the compiler refuses with CS4007, by the rule the compiler has: an <c>await</c> or a <c>yield return</c> forgets
/// every local, a write makes it known again, and a read of a forgotten local on any path would need it preserved. It runs on the
/// control flow graph, because a local that is only used on the other branch of an <c>if</c>, or written again after the <c>await</c>, compiles.
/// </summary>
internal static class CommandFlow
{
    /// <summary>Whether a read of a local comes after a boundary it cannot cross.</summary>
    /// <param name="graph">The graph of the whole member, which holds the graphs of its lambdas and local functions.</param>
    /// <param name="reference">The reference.</param>
    /// <returns>Whether the compiler would have to preserve the local for this reference.</returns>
    public static bool ReadsAcrossBoundary(ControlFlowGraph graph, ILocalReferenceOperation reference)
    {
        var reads = new HashSet<SyntaxNode>();
        Collect(graph, reference.Local, reads);
        return reads.Contains(reference.Syntax);
    }

    private static void Collect(ControlFlowGraph graph, ILocalSymbol local, HashSet<SyntaxNode> reads)
    {
        Flow(graph, local, reads);
        foreach (var function in graph.LocalFunctions)
        {
            Collect(graph.GetLocalFunctionControlFlowGraph(function), local, reads);
        }

        foreach (var block in graph.Blocks)
        {
            foreach (var lambda in OperationsOf(block).SelectMany(static operation => operation.DescendantsAndSelf()).OfType<IFlowAnonymousFunctionOperation>())
            {
                Collect(graph.GetAnonymousFunctionControlFlowGraph(lambda), local, reads);
            }
        }
    }

    // The state of a block only ever turns stale, so a read found on the way to the fixed point stays a finding.
    private static void Flow(ControlFlowGraph graph, ILocalSymbol local, HashSet<SyntaxNode> reads)
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
                    Visit(operation, local, ref stale, reads);
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
    private static void Visit(IOperation operation, ILocalSymbol local, ref bool stale, HashSet<SyntaxNode> reads)
    {
        if (operation is ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment && SymbolEqualityComparer.Default.Equals(target.Local, local))
        {
            Visit(assignment.Value, local, ref stale, reads);
            stale = false;
            return;
        }

        foreach (var child in operation.ChildOperations)
        {
            Visit(child, local, ref stale, reads);
        }

        if (operation.Kind is OperationKind.Await or OperationKind.YieldReturn)
        {
            stale = true;
        }
        else if (operation is ILocalReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Local, local))
        {
            if (IsOutArgument(reference))
            {
                stale = false;
            }
            else if (stale)
            {
                reads.Add(reference.Syntax);
            }
        }
    }

    private static bool IsOutArgument(IOperation reference) =>
        reference.Syntax.Parent is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
}
