using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA202: <c>await shop.Query("a")</c> and <c>shop.Query("a").SendAsync()</c>. The extension methods of the transaction steps are generic, so the
/// compiler answers with a constraint on a type parameter (CS0315), which says nothing about what to write. This names the terminals that exist.
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

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.ReadCommandNotAwaitable];

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
                start.RegisterSyntaxNodeAction(context => AnalyzeInvocation(context, readCommand), SyntaxKind.InvocationExpression);
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

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, INamedTypeSymbol readCommand)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: SendTerminal } access
            && IsReadCommand(context, access.Expression, readCommand)
            && context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ReadCommandNotAwaitable, access.Name.GetLocation()));
        }
    }

    private static bool IsReadCommand(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, INamedTypeSymbol readCommand) =>
        context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is { } type && type.AllInterfaces.Contains(readCommand, SymbolEqualityComparer.Default);
}
