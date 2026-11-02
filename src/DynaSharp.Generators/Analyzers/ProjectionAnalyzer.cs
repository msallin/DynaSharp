using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// What a read of part of an item cannot get from it. DYNA208: <c>orders.Query(pk).Project("Total").ItemsAsync()</c> where the entity has a member that a read throws without, which is what the generator's
/// <see cref="MissingBehavior.Throw"/> says. A read adds its discriminator and its keys, so a member that one of those restores is not missing. DYNA209: <c>Select(handle)</c> of an attribute that an item can
/// lack where a read of the entity goes on: a stored property that a key of the table holds, which an item created by an update does not have and a read restores from the key, and a version, which an item
/// written without one does not have and a read takes for 0.
/// </summary>
/// <remarks>
/// A chain says what it reads when it is followed from the table to its terminal, so a chain that came from a variable, goes on in one, or passes a method of the consumer says nothing, and neither does a
/// path that is not constant. A read of an index adds the keys of that index, and which one it is does not show in the type, so such a read counts as holding every key of an index.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ProjectionAnalyzer : DiagnosticAnalyzer
{
    private const string ProjectVerb = "Project";

    private const string SelectVerb = "Select";

    private const string HandlesClass = "Attrs";

    private const string NestedPathMethod = "At";

    private const string NameMethod = "Attr";

    private const string IndexType = "Index";

    private const string SelectOfVersion = "is its version, and an item written without one has no such attribute: a read of the entity gives 0, Select does not and throws. Read the entity, or Select only from items that have a version.";

    // The terminals that read no item: a count has no use for a projection, and it throws for one anyway.
    private static readonly HashSet<string> ReadNoItem = ["ToRequestJson", "CountAsync"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.ProjectionLeavesOutRequired, Descriptors.SelectOfKeyInput];

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
                start.RegisterOperationAction(operation => Analyze(operation, models), OperationKind.Invocation);
            }
        });
    }

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsOfTheLibrary(invocation))
        {
            return;
        }

        if (invocation.TargetMethod.Name == ProjectVerb)
        {
            AnalyzeProject(context, invocation, models);
        }
        else if (invocation.TargetMethod.Name == SelectVerb)
        {
            AnalyzeSelect(context, invocation, models);
        }
    }

    private static bool IsOfTheLibrary(IInvocationOperation invocation) => DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType);

    private static void AnalyzeSelect(OperationAnalysisContext context, IInvocationOperation select, ModelCatalog models)
    {
        var argument = select.Arguments[0].Value;
        if (argument is IPropertyReferenceOperation { Property: { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } handle }
            && models.ModelOf(owner) is { Entity: { } entity } model
            && WhySelectThrows(entity, model.Property(handle.Name)) is { } reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.SelectOfKeyInput, argument.Syntax.GetLocation(), handle.Name, owner.Name, reason));
        }
    }

    // What a read of the entity gets past and Select does not. A version that is required or a parameter without a default fails both reads alike, and a version cannot be nullable or feed a key.
    private static string? WhySelectThrows(EntityModel entity, PropertyModel property)
    {
        if (property.FallbackKey is { } key && IsKeyOfTheTable(entity, key))
        {
            return $"is held by the key '{key}' of the table, and an item that an Update created has no attribute of its own for it: a read of the entity restores it from the key, Select does not and throws. Read the entity, or Select only from items a Put wrote.";
        }

        return property.Name == entity.VersionProperty && property.Missing == MissingBehavior.KeepCurrent ? SelectOfVersion : null;
    }

    // An update stores no input of a key of the table, which is what leaves an item with the key alone. An input of an index is set like any other attribute, and its key follows.
    private static bool IsKeyOfTheTable(EntityModel entity, string key) => key == entity.PartitionKey.AttributeName || key == entity.SortKey?.AttributeName;

    private static void AnalyzeProject(OperationAnalysisContext context, IInvocationOperation project, ModelCatalog models)
    {
        if (CommandThatReads(project) is not { } command || Projected(project, models, out var source) is not { Count: > 0 } names)
        {
            return;
        }

        var types = command.TypeArguments.OfType<INamedTypeSymbol>().Select(models.ModelOf).ToList();
        if (types.Count != command.TypeArguments.Length || types.Contains(null))
        {
            return;
        }

        var keys = KeysOf(types!, source.Type!.Name == IndexType);
        foreach (var type in types)
        {
            if (type!.Properties.FirstOrDefault(property => IsNeeded(property) && !Reaches(property, names, keys)) is { } missing)
            {
                var name = ((InvocationExpressionSyntax)project.Syntax).Expression.GetLastToken();
                var select = project.Type!.GetMembers(SelectVerb).IsEmpty ? string.Empty : ", or use Select(handle) to read one attribute";
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.ProjectionLeavesOutRequired, name.GetLocation(), missing.Name, type.Name, select));
                return;
            }
        }
    }

    // Follows the chain from this call to its end. Another Project says more, and is the one to look at. What comes after Select is no entity.
    private static INamedTypeSymbol? CommandThatReads(IInvocationOperation project)
    {
        IOperation command = project;
        while (NextCall(command) is { } next)
        {
            if (!IsOfTheLibrary(next) || next.TargetMethod.Name is ProjectVerb or SelectVerb || ReadNoItem.Contains(next.TargetMethod.Name))
            {
                return null;
            }

            if (!DynaSharpTypes.IsCommand(next.Type))
            {
                return (INamedTypeSymbol)command.Type!;
            }

            command = next;
        }

        return command.Parent is IAwaitOperation or IExpressionStatementOperation ? (INamedTypeSymbol)command.Type! : null;
    }

    private static IInvocationOperation? NextCall(IOperation command) => command.Parent switch
    {
        IInvocationOperation call when call.Instance == command => call,
        IArgumentOperation { Parent: IInvocationOperation call } argument when call.TargetMethod.IsExtensionMethod && call.Arguments[0] == argument => call,
        _ => null,
    };

    // A method of the library that a chain goes through is an instance or an extension method, so there is a receiver.
    private static IOperation ReceiverOf(IInvocationOperation invocation) => invocation.Instance ?? invocation.Arguments[0].Value;

    // Everything the chain projects, or nothing if that is not known. The source is what the chain started from.
    private static HashSet<string>? Projected(IInvocationOperation last, ModelCatalog models, out IOperation source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        source = last;
        while (source is IInvocationOperation call && IsOfTheLibrary(call))
        {
            source = ReceiverOf(call);
            if (!DynaSharpTypes.IsCommand(source.Type))
            {
                return names;
            }

            if (call.TargetMethod.Name == ProjectVerb && !AddRoots(call, names, models))
            {
                return null;
            }
        }

        return null;
    }

    private static bool AddRoots(IInvocationOperation project, HashSet<string> names, ModelCatalog models)
    {
        var value = project.Arguments[0].Value;
        var elements = (value is IConversionOperation conversion ? conversion.Operand : value) switch
        {
            IArrayCreationOperation { Initializer: { } initializer } => initializer.ElementValues,
            ICollectionExpressionOperation collection => collection.Elements,
            _ => default,
        };
        if (elements.IsDefault)
        {
            return false;
        }

        foreach (var element in elements)
        {
            if (RootOf(element, models) is not { } root)
            {
                return false;
            }

            names.Add(root);
        }

        return true;
    }

    // The attribute a path starts with: Total for "Total", "Total.Net" and Order.Attrs.Total, and ShipTo for Order.Attrs.ShipTo.At(Address.Attrs.City). Null for what cannot be known here.
    private static string? RootOf(IOperation value, ModelCatalog models) => value switch
    {
        { ConstantValue: { HasValue: true, Value: string path } } => PathSyntax.IsValid(path) ? PathSyntax.Root(path) : null,
        IConversionOperation conversion => RootOf(conversion.Operand, models),
        IPropertyReferenceOperation { Property: { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } handle } => models.ModelOf(owner)?.Property(handle.Name).AttributeName,
        IInvocationOperation { TargetMethod.Name: NestedPathMethod or NameMethod } call when IsOfTheLibrary(call) => RootOf(call.Arguments[0].Value, models),
        IObjectCreationOperation { Type: { } type } creation when DynaSharpTypes.IsDeclaredByDynaSharp(type) => RootOf(creation.Arguments[0].Value, models),
        _ => null,
    };

    private static HashSet<string> KeysOf(List<TypeModel> types, bool readsAnIndex)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in types.Select(static type => type.Entity).OfType<EntityModel>())
        {
            keys.Add(entity.PartitionKey.AttributeName);
            if (entity.SortKey is { } sortKey)
            {
                keys.Add(sortKey.AttributeName);
            }

            if (readsAnIndex)
            {
                keys.UnionWith(entity.Keys().Select(static key => key.AttributeName));
            }
        }

        return keys;
    }

    private static bool IsNeeded(PropertyModel property) => property.Missing == MissingBehavior.Throw && !property.IsWriteOnly;

    // A member gets to the read by its attribute, or by the key that restores it. A [KeyOnly] property has no attribute to go by.
    private static bool Reaches(PropertyModel property, HashSet<string> names, HashSet<string> keys)
    {
        var restoredBy = property.RestoredFromKey ?? property.FallbackKey;
        return (restoredBy is not null && (names.Contains(restoredBy) || keys.Contains(restoredBy)))
            || (property.RestoredFromKey is null && (names.Contains(property.AttributeName) || keys.Contains(property.AttributeName)));
    }
}
