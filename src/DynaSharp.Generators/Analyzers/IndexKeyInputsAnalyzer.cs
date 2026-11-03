using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// DYNA226: an update that sets by handle some inputs of a key of an index and not the others, <c>Set(WorkItem.Attrs.Status, closed)</c> under <c>{Status}#{CreatedAt}</c>, or one key of a
/// sparse index and not what its other key is made of. <c>KeyInputChanges</c> throws for both when the update is sent, because DynamoDB cannot build a key from what an item holds, so the
/// update brings every input, also one that does not change. An input of the key of the table is known from the key the update was given, and a key of literals needs nothing.
/// </summary>
/// <remarks>
/// A chain is read at its last call, and only when all of it is there: from the verb <c>Update</c>, on a table or as a step of a transaction, to a terminal, an <c>await</c> or the next step, with no
/// method of the consumer between them. An update kept in a variable, or that a helper returns, can get the rest elsewhere, so it is not looked at, and neither is a handle in a variable or of
/// another type, which may name an input. An index with an input that the update removes, or changes by <c>Add</c>, <c>SetIfNotExists</c> or <c>Delete</c>, leaves the index or throws something
/// else, and a write to what <c>DYNA205</c> or <c>DYNA219</c> reports throws that first, so those are left to them.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class IndexKeyInputsAnalyzer : DiagnosticAnalyzer
{
    private const string UpdateVerb = "Update";

    private const string HandlesClass = "Attrs";

    private const string NestedPath = "At";

    // The commands of an update. A step of a transaction has its own type, and the verb of the next step returns it too.
    private static readonly HashSet<string> Updates = ["UpdateCommand", "UpdateStep"];

    private enum Change
    {
        Set,
        Removed,
        Unknown,
    }

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.IndexKeyWithoutAllInputs];

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
        if (invocation.Type is not INamedTypeSymbol type || !Updates.Contains(type.Name) || !DynaSharpTypes.IsDeclaredByDynaSharp(type) || UpdatePathAnalyzer.GoesOn(invocation) || !IsSent(invocation)
            || Read(invocation, type, models) is not { } update)
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in update.Entity.Indexes)
        {
            if (Problem(update, index) is { } problem && reported.Add(problem.Key.AttributeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.IndexKeyWithoutAllInputs,
                    problem.At.Syntax.GetLocation(),
                    update.Symbol.Name,
                    string.Join(", ", problem.Set),
                    string.Join(", ", problem.Missing),
                    TextOf(problem.Key),
                    index.IndexName,
                    Remedy(update, problem.Missing)));
            }
        }
    }

    // A terminal, an await, or the next step of a transaction: what comes after it is no longer this update.
    private static bool IsSent(IInvocationOperation invocation) => invocation.Parent switch
    {
        IAwaitOperation => true,
        IInvocationOperation next when next.Instance == invocation => DynaSharpTypes.IsDeclaredByDynaSharp(next.TargetMethod.ContainingType),
        IArgumentOperation { Parent: IInvocationOperation next } argument when next.TargetMethod.IsExtensionMethod && next.Arguments[0] == argument => DynaSharpTypes.IsDeclaredByDynaSharp(next.TargetMethod.ContainingType),
        _ => false,
    };

    // From the end back to the verb, the latest action first, or null when the chain cannot be followed that far, or touches what another rule reports.
    private static Update? Read(IInvocationOperation invocation, INamedTypeSymbol type, ModelCatalog models)
    {
        var calls = new List<IInvocationOperation>();
        IOperation? current = invocation;
        while (current is IInvocationOperation call && SymbolEqualityComparer.Default.Equals(call.Type, type) && DynaSharpTypes.IsDeclaredByDynaSharp(call.TargetMethod.ContainingType))
        {
            // The verb of a table and of a transaction step: no other method of the library is called Update and returns an update.
            if (call.TargetMethod.Name == UpdateVerb)
            {
                var entity = (UpdatePathAnalyzer.IsStepVerb(call) ? call.TargetMethod.TypeArguments[1] : type.TypeArguments[0]) as INamedTypeSymbol;
                return entity is not null && models.ModelOf(entity) is { Entity: { } model } typeModel ? Actions(calls, entity, typeModel, model, models) : null;
            }

            calls.Add(call);
            current = UpdatePathAnalyzer.ReceiverOf(call);
        }

        return null;
    }

    private static Update? Actions(List<IInvocationOperation> calls, INamedTypeSymbol entity, TypeModel type, EntityModel model, ModelCatalog models)
    {
        var actions = new List<(string Property, Change Change, IOperation Handle)>();
        foreach (var call in calls)
        {
            if (!EntityCommandAnalyzer.WriteMethods.Contains(call.TargetMethod.Name) || call.TargetMethod.Parameters[0].Name != EntityCommandAnalyzer.HandleParameter)
            {
                continue;
            }

            var target = UpdatePathAnalyzer.ArgumentOf(call, 0);
            var handle = EntityCommandAnalyzer.HandleUnder(target);
            if (handle is not { ContainingType: { Name: HandlesClass, ContainingType: { } owner } } || !EntityCommandAnalyzer.Inherits(entity, owner)
                || EntityCommandAnalyzer.FeedsTableKey(model, handle.Name) || EntityCommandAnalyzer.IndexKeysDerivedFrom(model, handle.Name).Any())
            {
                return null;
            }

            // A path inside a document is not the attribute of an input, whose value is all of it.
            if (target is not IInvocationOperation { TargetMethod.Name: NestedPath } && ChangeOf(call, target, models) is { } change)
            {
                actions.Add((handle.Name, change, target));
            }
        }

        return new Update(entity, type, model, actions);
    }

    // Set of what counts as null is a Remove, and SetIfNotNull of it adds nothing. A Set of a value that may be null is one or the other, which only the run time knows, and a Remove of
    // an input of a sparse index is how an item leaves it, so such an index is left alone.
    private static Change? ChangeOf(IInvocationOperation call, IOperation target, ModelCatalog models) => call.TargetMethod.Name switch
    {
        "Set" when MayBeNull(UpdatePathAnalyzer.ArgumentOf(call, 1)) => Change.Unknown,
        "Set" => UpdatePathAnalyzer.AddsNothing(UpdatePathAnalyzer.ArgumentOf(call, 1), target, models) ? Change.Removed : Change.Set,
        "SetIfNotNull" => UpdatePathAnalyzer.AddsNothing(UpdatePathAnalyzer.ArgumentOf(call, 1), target, models) ? (Change?)null : Change.Set,
        "Remove" => Change.Removed,
        _ => Change.Unknown,
    };

    // Through the conversion to the type of the handle: Set(Attrs.Since, day) of a DateOnly converts it to DateOnly?, and that does not make it null.
    private static bool MayBeNull(IOperation value)
    {
        while (value is IConversionOperation { IsImplicit: true } conversion)
        {
            value = conversion.Operand;
        }

        // Without a nullable context the compiler knows nothing of the flow, so any reference and any Nullable<T> may be null there.
        var state = value.SemanticModel!.GetTypeInfo(value.Syntax).Nullability.FlowState;
        return !value.ConstantValue.HasValue
            && (state == NullableFlowState.MaybeNull || (state == NullableFlowState.None && value.Type is { IsReferenceType: true } or { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }));
    }

    // What KeyInputChanges throws for first, for one index: a key of a template that only some of its inputs reached, then, on a sparse index, a key the update sets without the other.
    private static (KeyModel Key, List<string> Set, List<string> Missing, IOperation At)? Problem(Update update, IndexModel index)
    {
        var keys = new[] { index.IsLocal ? null : index.PartitionKey, index.SortKey }.OfType<KeyModel>().Select(update.Owner).ToList();
        if (keys.Exists(key => Inputs(key).Any(input => update.Has(input, Change.Removed) || update.Has(input, Change.Unknown))))
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (key.Template is not null && !update.IsTableKey(key.AttributeName) && Missing(update, key) is { Count: > 0 } missing && SetOf(update, key) is { Count: > 0 } set)
            {
                return (key, set, missing, update.LatestOf(set));
            }
        }

        if (index is { Sparse: true, SortKey: { } sortKey })
        {
            var (partition, sort) = (update.Owner(index.PartitionKey), update.Owner(sortKey));
            foreach (var (key, other) in new[] { (partition, sort), (sort, partition) })
            {
                if (SetOf(update, key) is { Count: > 0 } set && !update.IsTableKey(other.AttributeName) && Missing(update, other) is { Count: > 0 } missing)
                {
                    return (other, set, missing, update.LatestOf(set));
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Inputs(KeyModel key) =>
        key.Template is null ? [key.PlainProperty!] : key.Template.Segments.OfType<PlaceholderSegment>().Select(static placeholder => placeholder.Property).Distinct();

    private static List<string> SetOf(Update update, KeyModel key) => [.. Inputs(key).Where(input => update.Has(input, Change.Set))];

    private static List<string> Missing(Update update, KeyModel key) => [.. Inputs(key).Where(input => !update.Has(input, Change.Set) && !update.IsTableKeyInput(input))];

    private static string TextOf(KeyModel key) => key.Template?.Text ?? "{" + key.PlainProperty + "}";

    // A property only a key holds, and one its getter computes, have no handle that could set them.
    private static string Remedy(Update update, List<string> missing)
    {
        var settable = missing.TrueForAll(input => update.Type.Properties.Any(property => property.Name == input && property.Converter is not null)
            && !update.Entity.ComputedInputs.Any(computed => computed.Property == input));
        return settable
            ? $"Set {string.Join(" and ", missing)} as well, with {(missing.Count == 1 ? "the value" : "the values")} the item holds"
            : "Put the changed entity instead, or write the attribute of the key by a raw path";
    }

    /// <summary>An update that can be followed from its verb, and what it does by handle to the properties of its entity.</summary>
    /// <param name="Symbol">The entity.</param>
    /// <param name="Type">Its model.</param>
    /// <param name="Entity">Its keys.</param>
    /// <param name="Actions">The actions by handle, the latest first.</param>
    private sealed record Update(INamedTypeSymbol Symbol, TypeModel Type, EntityModel Entity, List<(string Property, Change Change, IOperation Handle)> Actions)
    {
        public bool Has(string property, Change change) => Actions.Exists(action => action.Property == property && action.Change == change);

        public bool IsTableKey(string attribute) => attribute == Entity.PartitionKey.AttributeName || attribute == Entity.SortKey?.AttributeName;

        public bool IsTableKeyInput(string property) => new[] { Entity.PartitionKey, Entity.SortKey }.OfType<KeyModel>().Any(key => Inputs(key).Contains(property));

        public IOperation LatestOf(List<string> properties) => Actions.First(action => action.Change == Change.Set && properties.Contains(action.Property)).Handle;

        // The key that writes an attribute: a key of the table, or the first index key that claims it, as the metadata of an update holds it.
        public KeyModel Owner(KeyModel key) => new[] { Entity.PartitionKey, Entity.SortKey }.Concat(Entity.Indexes.SelectMany(static index => new[] { index.PartitionKey, index.SortKey }))
            .OfType<KeyModel>().First(candidate => candidate.AttributeName == key.AttributeName);
    }
}
