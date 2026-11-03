using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA202: <c>await shop.Query("a")</c> and <c>shop.Query("a").SendAsync()</c>. The extension methods of the transaction steps are generic, so the
/// compiler answers with a constraint on a type parameter (CS0315), which says nothing about what to write. This names the terminals that exist.
/// DYNA234: <c>events.Query(shards).Take(100)</c>, a method of LINQ on the read instead of on its items. A read is a ref struct and no enumerable, so the compiler finds
/// no such method (CS1061) or cannot infer one (CS0411), which points at LINQ and not at the chain. This names the terminal that streams the items.
/// </summary>
/// <remarks>
/// It works on syntax, because a call that failed to bind has no operation to look at. It reports only when the compiler found nothing, so an
/// extension method of the consumer that makes a query awaitable stays legal. A read command is known by <c>IReadCommand</c> and not by a terminal, which differs between the reads. That interface is internal,
/// and the compiler imports an internal type of a reference at the top level of a namespace, so the analyzer sees it in a consumer's compilation too.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ReadTerminalAnalyzer : DiagnosticAnalyzer
{
    private const string SendTerminal = "SendAsync";

    private const string ItemsTerminal = "ItemsAsync";

    private const string ValuesTerminal = "ValuesAsync";

    // What a reader of a read wants to call on its items: Take and ToListAsync of the asynchronous LINQ of .NET, and the synchronous ones written out of habit.
    private static readonly string[] LinqClasses = ["System.Linq.AsyncEnumerable", "System.Linq.Enumerable"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.ReadCommandNotAwaitable, Descriptors.ItemMethodOnRead];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ReadCommandInterface) is { } readCommand)
            {
                start.RegisterSyntaxNodeAction(context => AnalyzeAwait(context, readCommand), SyntaxKind.AwaitExpression);
                var linq = LinqClasses
                    .Select(start.Compilation.GetTypeByMetadataName)
                    .OfType<INamedTypeSymbol>()
                    .SelectMany(static type => type.MemberNames)
                    .ToImmutableHashSet(StringComparer.Ordinal);
                start.RegisterSyntaxNodeAction(context => AnalyzeInvocation(context, readCommand, linq), SyntaxKind.InvocationExpression);
            }
        });
    }

    private static void AnalyzeAwait(SyntaxNodeAnalysisContext context, INamedTypeSymbol readCommand)
    {
        var await = (AwaitExpressionSyntax)context.Node;
        if (IsReadCommand(context, await.Expression, readCommand) && context.SemanticModel.GetAwaitExpressionInfo(await).GetAwaiterMethod is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ReadCommandNotAwaitable, await.GetLocation()));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, INamedTypeSymbol readCommand, ImmutableHashSet<string> linq)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax access
            || (access.Name.Identifier.ValueText is var name && name != SendTerminal && !linq.Contains(name))
            || ReadCommandOf(context, access.Expression, readCommand) is not { } read
            || context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not null)
        {
            return;
        }

        if (name == SendTerminal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ReadCommandNotAwaitable, access.Name.GetLocation()));
        }
        else if (read.GetMembers(name).IsEmpty)
        {
            // A read after Select ends in values and has no ItemsAsync.
            var terminal = read.GetMembers(ItemsTerminal).IsEmpty ? ValuesTerminal : ItemsTerminal;
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ItemMethodOnRead, access.Name.GetLocation(), name, terminal));
        }
    }

    private static bool IsReadCommand(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, INamedTypeSymbol readCommand) => ReadCommandOf(context, expression, readCommand) is not null;

    private static ITypeSymbol? ReadCommandOf(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, INamedTypeSymbol readCommand) =>
        context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is { } type && type.AllInterfaces.Contains(readCommand, SymbolEqualityComparer.Default) ? type : null;
}
