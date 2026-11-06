using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA238: <c>client.Table&lt;Order&gt;()</c> of an entity that declares no table, neither with <c>[DynamoDbEntity(Table = ...)]</c> nor through <c>[assembly: DynamoDbDefaults(Table = ...)]</c> of its
/// assembly. The call compiles and throws <c>InvalidOperationException</c>; the overload that takes a name is fine.
/// </summary>
/// <remarks>
/// It covers every member of the library that opens the table of an entity without a name, which <see cref="Members"/> lists: those of the client and of the fake of <c>DynaSharp.Testing</c>. A type
/// without <c>[DynamoDbEntity]</c>, such as one written by hand, says its table in code, so it is not reported, nor is a type parameter.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class DeclaredTableAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The members that open the declared table, by the metadata name of their type. Each has an overload that takes the name as a string, which this one lacks.</summary>
    public static readonly IReadOnlyDictionary<string, ImmutableArray<string>> Members = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
    {
        ["DynaSharp.DynamoDbClient"] = ["Table"],
        ["DynaSharp.Testing.FakeDynamoDb"] = ["CreateTable", "Get", "Items", "Put"],
    };

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.NoDeclaredTable];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        if (method.TypeArguments.Length != 1 || method.TypeArguments[0] is not INamedTypeSymbol entity
            || !Members.TryGetValue(method.ContainingType.ToDisplayString(), out var names) || !names.Contains(method.Name)
            || method.Parameters.Any(static parameter => parameter.Type.SpecialType == SpecialType.System_String)
            || AttributeReader.Find(entity, WellKnownNames.EntityAttribute) is null || TableNames.Of(entity) is not null)
        {
            return;
        }

        var syntax = context.Operation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } ? access.Name : context.Operation.Syntax;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.NoDeclaredTable, syntax.GetLocation(), entity.Name, method.Name));
    }
}
