using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA235: <c>IfVersion</c> on a <c>Put</c>, <c>locks.Put(held).IfVersion(3)</c>, in a transaction too. A put has no such clause, because it takes the version from the entity, so the compiler
/// says <c>CS1061</c>, which does not say where the version comes from. This says it, for an entity with a <c>[Version]</c> and for one without.
/// </summary>
/// <remarks>
/// It works on syntax, because a call that failed to bind has no operation to look at. It reports only when the compiler found nothing, and no candidate of the consumer, so an extension method
/// of the consumer named <c>IfVersion</c> stays legal, and only for an entity the model knows: <c>Item</c>, a document and a type parameter are the compiler's alone.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class VersionClauseAnalyzer : DiagnosticAnalyzer
{
    private const string Clause = "IfVersion";

    private const string PutVerb = "Put";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.IfVersionOnPut];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                var models = new ModelCatalog(start.Compilation);
                start.RegisterSyntaxNodeAction(context => Analyze(context, models), SyntaxKind.InvocationExpression);
            }
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ModelCatalog models)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: Clause } access
            || !FoundNothingOfTheConsumer(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken))
            || context.SemanticModel.GetOperation(access.Expression, context.CancellationToken) is not { Type: INamedTypeSymbol type } receiver
            || !DynaSharpTypes.IsDeclaredByDynaSharp(type)
            || EntityCommandAnalyzer.EntityOfCommand(receiver, PutVerb) is not { } entity
            || models.ModelOf(entity)?.Entity is not { } model)
        {
            return;
        }

        object[] parts = model.VersionProperty is null
            ? [$"'{entity.Name}' has no [Version], so a Put has no version to check", "Mark a long property with [Version], or state the condition with If(...)"]
            : [$"A Put of '{entity.Name}' checks its version by itself: it writes only where the stored item holds the version of the entity, and stores the next one, so it has no IfVersion", "Put the entity as it was read, or call IgnoreVersion() to write without the check"];
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IfVersionOnPut, access.Name.GetLocation(), parts));
    }

    // A clause of the library that does not apply has no symbol, and a candidate at most of the library: the IfVersion of an update or a delete.
    private static bool FoundNothingOfTheConsumer(SymbolInfo info) =>
        info.Symbol is null && info.CandidateSymbols.All(static candidate => DynaSharpTypes.IsDeclaredByDynaSharp(candidate.ContainingType));
}
