using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA240: <c>orders.CreateTable().Including&lt;Customer&gt;().SendAsync(ct)</c> where another entity declares the table of <c>Order</c> as well, in this compilation or in an assembly it references, and
/// the chain leaves it out. The table is created without the indexes of that entity, and a local one cannot be added later. <see cref="FixProperty"/> holds the names the fix appends.
/// </summary>
/// <remarks>
/// It checks the chain as written, from <c>CreateTable()</c> on a <c>Table&lt;T&gt;</c> of an entity that declares a table to the call that leaves the command: <c>SendAsync</c>, <c>ToRequest</c>,
/// <c>ToRequestJson</c>, an <c>await</c>, or a <c>return</c> or expression body that hands the command to the caller as it is. A chain that goes into a variable, a field, an argument or a method of its own,
/// which may add an entity later, is not checked, nor one with <c>IncludingAll()</c>, nor a table
/// opened with a name, <c>client.Table&lt;T&gt;("name")</c>, which may be another table. An entity that the code cannot name, such as an <c>internal</c> one of another assembly, is left out of the message.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class LeftOutEntityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The property of the diagnostic with the entities to include, each as the code names it there, separated by <see cref="Separator"/>, in the order of their full names.</summary>
    internal const string FixProperty = "Including";

    /// <summary>What separates the names in <see cref="FixProperty"/>; no type name holds it.</summary>
    internal const char Separator = ';';

    private const string CreateTableVerb = "CreateTable";

    private const string IncludingClause = "Including";

    private const string IncludingAllClause = "IncludingAll";

    private const string CommandType = "DynaSharp.CreateTableCommand";

    private const string EntityInterface = "DynaSharp.Mapping.IDynamoDbEntity`1";

    private const string LibraryAssembly = "DynaSharp";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.EntityLeftOutOfTable];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(CommandType) is { } command && start.Compilation.GetTypeByMetadataName(EntityInterface) is { } entity)
            {
                // Only a compilation that creates a table walks the types of its references, and it does so once.
                var tables = new Lazy<ILookup<string, INamedTypeSymbol>>(() => EntitiesByTable(start.Compilation, entity));
                start.RegisterOperationAction(operation => Analyze(operation, command, tables), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol command, Lazy<ILookup<string, INamedTypeSymbol>> tables)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod is not { Name: CreateTableVerb, Parameters.Length: 0, ContainingType: { Name: "Table", Arity: 1 } holder }
            || !DynaSharpTypes.IsDeclaredByDynaSharp(holder)
            || holder.TypeArguments[0] is not INamedTypeSymbol owner
            || AttributeReader.Find(owner, WellKnownNames.EntityAttribute) is null
            || TableNames.Of(owner) is not { } table
            || OpenedWithName(invocation.Instance)
            || IncludedUpToTheEnd(invocation, command) is not { } included)
        {
            return;
        }

        included.Add(owner);
        var within = context.ContainingSymbol.ContainingType;
        var missing = tables.Value[table]
            .Where(entity => !included.Contains(entity) && context.Compilation.IsSymbolAccessibleWithin(entity, within))
            .OrderBy(static entity => entity.ToDisplayString(), StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var model = invocation.SemanticModel!;
        var position = invocation.Syntax.SpanStart;
        var names = missing.Select(entity => entity.ToMinimalDisplayString(model, position)).ToList();
        var properties = ImmutableDictionary<string, string?>.Empty.Add(FixProperty, string.Join(Separator.ToString(), names));
        var listed = string.Join(", ", names.Select(static name => $"'{name}'"));
        var name = ((InvocationExpressionSyntax)invocation.Syntax).Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name,
            var binding => ((MemberBindingExpressionSyntax)binding).Name,
        };
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.EntityLeftOutOfTable, name.GetLocation(), properties, owner.Name, table, listed));
    }

    // client.Table<Order>("orders-test"): a name of its own may be another table than the declared one, such as one per test run.
    private static bool OpenedWithName(IOperation? table) =>
        table is IInvocationOperation opened && DynaSharpTypes.IsDeclaredByDynaSharp(opened.TargetMethod.ContainingType)
        && opened.TargetMethod.Parameters.Any(static parameter => parameter.Type.SpecialType == SpecialType.System_String);

    // The entities the chain includes, up to the call that leaves the command. Null when the chain does not end in sight, or calls IncludingAll() or a method of its own on the way, which may include any entity.
    // A command a member returns, by return or expression body, is complete there: it is what the member offers, such as a table to create in a fake, and a caller that adds to it is rare.
    private static HashSet<INamedTypeSymbol>? IncludedUpToTheEnd(IInvocationOperation createTable, INamedTypeSymbol command)
    {
        var included = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        IOperation current = createTable;
        while (Next(current) is { } call)
        {
            if (call.TargetMethod.Name == IncludingAllClause || !DynaSharpTypes.IsDeclaredByDynaSharp(call.TargetMethod.ContainingType))
            {
                return null;
            }

            if (!SymbolEqualityComparer.Default.Equals(call.Type, command))
            {
                return included;
            }

            if (call.TargetMethod.Name == IncludingClause)
            {
                // A type parameter, in a helper of the caller's own, can be any entity.
                if (call.TargetMethod.TypeArguments[0] is not INamedTypeSymbol entity)
                {
                    return null;
                }

                included.Add(entity);
            }

            current = call;
        }

        return current.Parent is IAwaitOperation or IReturnOperation ? included : null;
    }

    // The call on the result of this one: x.Including<T>(), or an extension method, whose receiver is its first argument.
    private static IInvocationOperation? Next(IOperation current) => current.Parent switch
    {
        IInvocationOperation call when call.Instance == current => call,
        IArgumentOperation { Parent: IInvocationOperation call } argument when call.TargetMethod.IsExtensionMethod && call.Arguments[0] == argument => call,
        _ => null,
    };

    // The entities of this compilation and of each reference that can hold one, which is one that references DynaSharp, by the table they declare.
    private static ILookup<string, INamedTypeSymbol> EntitiesByTable(Compilation compilation, INamedTypeSymbol entityInterface)
    {
        var entities = new List<(string Table, INamedTypeSymbol Entity)>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.Where(ReferencesDynaSharp).Prepend(compilation.Assembly))
        {
            Collect(assembly.GlobalNamespace, entityInterface, entities);
        }

        return entities.ToLookup(static pair => pair.Table, static pair => pair.Entity, StringComparer.Ordinal);
    }

    private static bool ReferencesDynaSharp(IAssemblySymbol assembly) =>
        assembly.Modules.Any(static module => module.ReferencedAssemblies.Any(static identity => identity.Name == LibraryAssembly));

    private static void Collect(INamespaceSymbol container, INamedTypeSymbol entityInterface, List<(string Table, INamedTypeSymbol Entity)> entities)
    {
        foreach (var inner in container.GetNamespaceMembers())
        {
            Collect(inner, entityInterface, entities);
        }

        foreach (var type in container.GetTypeMembers())
        {
            Collect(type, entityInterface, entities);
        }
    }

    // Including<T>() takes an entity that implements IDynamoDbEntity<T>, which the generator gave it: one with an error has no code.
    private static void Collect(INamedTypeSymbol type, INamedTypeSymbol entityInterface, List<(string Table, INamedTypeSymbol Entity)> entities)
    {
        if (AttributeReader.Find(type, WellKnownNames.EntityAttribute) is not null
            && TableNames.Of(type) is { } table
            && type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, entityInterface) && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type)))
        {
            entities.Add((table, type));
        }

        foreach (var nested in type.GetTypeMembers())
        {
            Collect(nested, entityInterface, entities);
        }
    }
}
