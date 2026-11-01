using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA215: <c>invoices.Query(key).Filter(Subscription.Attrs.Seats.Gt(1))</c>. A handle is a path and a converter, not tied to its entity, so this compiles, and DynamoDB answers by finding
/// nothing. A mixed read takes a handle of any of its types, a type and its base types share their attributes, and the inside of <c>.At(...)</c> is a path in a nested document, which is
/// what handles of documents are for.
/// </summary>
/// <remarks>
/// A command says what it is on by a type argument, and a step of a transaction by the table of the verb it came from, which is found by walking the chain back. Whatever cannot be followed,
/// such as a generic helper or a step in a variable, is not reported.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class HandleOwnerAnalyzer : DiagnosticAnalyzer
{
    private const string HandlesClass = "Attrs";

    private const string HandleType = "Attr";

    private const string NestedPathMethod = "At";

    private const string ValueMethod = "Value";

    private const string TableType = "Table";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.HandleOfAnotherType];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(WellKnownNames.ExpressionHandler) is not null)
            {
                start.RegisterOperationAction(Analyze, OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType) || !DynaSharpTypes.IsRefStruct(ReceiverOf(invocation)?.Type))
        {
            return;
        }

        var handles = new List<IPropertyReferenceOperation>();
        foreach (var argument in ArgumentsOf(invocation))
        {
            CollectHandles(argument.Value, handles);
        }

        if (handles.Count > 0 && TypesOf(ReceiverOf(invocation)) is { Count: > 0 } types)
        {
            foreach (var handle in handles)
            {
                var owner = handle.Property.ContainingType.ContainingType;
                if (!types.Exists(type => AreRelated(owner, type)))
                {
                    var name = $"{owner.Name}.{HandlesClass}.{handle.Property.Name}";
                    var commandTypes = string.Join(", ", types.Select(static type => type.Name));
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.HandleOfAnotherType, handle.Syntax.GetLocation(), name, owner.Name, commandTypes));
                }
            }
        }
    }

    // A command is the instance, and for a method of an extension class the first argument, which is no value of the clause.
    private static IOperation? ReceiverOf(IInvocationOperation invocation) =>
        invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments[0].Value : invocation.Instance;

    private static IEnumerable<IArgumentOperation> ArgumentsOf(IInvocationOperation invocation) =>
        invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments.Skip(1) : invocation.Arguments;

    private static void CollectHandles(IOperation operation, List<IPropertyReferenceOperation> handles)
    {
        switch (operation)
        {
            case IPropertyReferenceOperation reference when IsHandle(reference.Property):
                handles.Add(reference);
                return;
            case IInvocationOperation { TargetMethod.Name: ValueMethod } value when DynaSharpTypes.IsDeclaredByDynaSharp(value.TargetMethod.ContainingType):
                return;
            case IInvocationOperation { TargetMethod.Name: NestedPathMethod } nested when DynaSharpTypes.IsDeclaredByDynaSharp(nested.TargetMethod.ContainingType):
                CollectHandles(nested.Arguments[0].Value, handles);
                return;
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectHandles(child, handles);
        }
    }

    private static bool IsHandle(IPropertySymbol property) =>
        property is { IsStatic: true, Type.Name: HandleType, ContainingType: { Name: HandlesClass, ContainingType: { } owner } }
        && DynaSharpTypes.IsDeclaredByDynaSharp(property.Type)
        && IsMapped(owner);

    private static bool IsMapped(ITypeSymbol type) =>
        AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null || AttributeReader.Find(type, WellKnownNames.DocumentAttribute) is not null;

    // The mapped types of a command, or nothing for one without a schema or one that cannot be followed, where every handle is fine.
    private static List<INamedTypeSymbol>? TypesOf(IOperation? command) => command switch
    {
        { Type: INamedTypeSymbol { IsGenericType: true } type } => MappedArguments(type),
        IInvocationOperation call => TypesOf(TableOf(call) ?? ReceiverOf(call)),
        _ => null,
    };

    private static List<INamedTypeSymbol>? MappedArguments(INamedTypeSymbol type)
    {
        var arguments = type.TypeArguments.OfType<INamedTypeSymbol>().ToList();
        return arguments.TrueForAll(IsMapped) ? arguments : null;
    }

    // A verb of a transaction names the table of its action. The table is a value, so its type goes the way of a command's.
    private static IOperation? TableOf(IInvocationOperation call)
    {
        foreach (var argument in ArgumentsOf(call))
        {
            if (argument.Value.Type is { Name: TableType } type && DynaSharpTypes.IsDeclaredByDynaSharp(type))
            {
                return argument.Value;
            }
        }

        return null;
    }

    private static bool AreRelated(INamedTypeSymbol owner, INamedTypeSymbol type) => DerivesFrom(owner, type) || DerivesFrom(type, owner);

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol other)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, other.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }
}
