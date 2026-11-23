using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>
/// What a command says that the model of its entity contradicts. DYNA205: a write by handle to a property the key of the table is made of, which an update cannot do. The rule is
/// <c>KeyInputChanges</c>, for the property itself and nothing else: what follows from a key of the table, such as a key of an index, is its business at run time. DYNA206: <c>IfNotExists</c> on a
/// <c>Put</c> whose item shows a version above 0 in the chain, which <c>WriteState.Settle</c> refuses. Version 0 is a first write, where the two conditions agree, so a version that is not a constant says
/// nothing and is not reported. DYNA207: a constant name in the <c>Index(string)</c> that takes the names from the entity, which throws at run time when the entity does not declare it. DYNA219: a write by handle,
/// also to a path inside it, to a property that a computed input of a key of an index is derived from, or to that computed input itself, which <c>KeyInputChanges</c> throws for. Behind a key of the table that is DYNA205, one step removed.
/// DYNA225: <c>Including&lt;T&gt;</c> of an entity whose key names differ from those of the table or of the entity included first, which <c>TableSchema.Add</c> throws for.
/// DYNA236: a write of a batch to a table of an entity with a version, without <c>IgnoreVersion</c> in the batch, which <c>WriteState.Settle</c> throws for when the batch is sent.
/// DYNA239: an <c>Update</c> or a <c>Delete</c> by key of an entity with a version, a command or a step of a transaction, without <c>IfVersion</c> or <c>IgnoreVersion</c>, which <c>WriteState.Settle</c> throws for as well.
/// </summary>
/// <remarks>A command says what it is on by a type argument, and a step of a transaction by the table of the verb it came from. Whatever cannot be followed, such as a generic helper or a step in a variable, is not reported.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class EntityCommandAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The static class of the library whose <c>Index</c> takes the name of an index an entity declares.</summary>
    internal const string EntityIndexes = "EntityTableExtensions";

    private const string UpdateVerb = "Update";

    private const string PutVerb = "Put";

    private const string IgnoreVersion = "IgnoreVersion";

    private const string IfVersion = "IfVersion";

    private const string IgnoreVersionCall = IgnoreVersion + "()";

    private const string TableClass = "Table";

    private const string ItemParameter = "item";

    private const string IncludingClause = "Including";

    private const string CreateTableVerb = "CreateTable";

    private const string BatchWriteCommand = "BatchWriteCommand";

    // The writes of BatchWriteCommand<T>, each of which settles a version that a batch cannot carry, and the deletes by an entity that extend it.
    private static readonly HashSet<string> BatchWrites = ["Put", "PutAll", "Delete", "DeleteAll"];

    // The names the placeholder of IfVersion tries, the first one free at the call, and then the last with a number.
    private static readonly string[] VersionPlaceholders = ["version", "expectedVersion"];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptors.WriteOfTableKeyInput, Descriptors.WriteOfComputedInputSource, Descriptors.IfNotExistsOnVersionedPut, Descriptors.UndeclaredIndex, Descriptors.IncludedKeyDiffers, Descriptors.VersionedBatchWrite, Descriptors.UnsettledVersionByKey];

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
        if (!DynaSharpCalls.IsOfTheLibrary(invocation))
        {
            return;
        }

        if (IsBatchWrite(invocation))
        {
            AnalyzeBatchWrite(context, invocation, models);
        }
        else if (DynaSharpCalls.IsWriteByKey(invocation.TargetMethod))
        {
            AnalyzeWriteByKey(context, invocation, models);
        }
        else if (DynaSharpCalls.WriteMethods.Contains(invocation.TargetMethod.Name))
        {
            AnalyzeWrite(context, invocation, models);
        }
        else if (invocation.TargetMethod.Name == "IfNotExists")
        {
            AnalyzeIfNotExists(context, invocation, models);
        }
        else if (invocation.TargetMethod is { Name: "Index", ContainingType.Name: EntityIndexes })
        {
            AnalyzeIndex(context, invocation, models);
        }
        else if (invocation.TargetMethod.Name == IncludingClause)
        {
            AnalyzeIncluding(context, invocation, models);
        }
    }

    // Index<T>(this Table<T> table, string name) and Index<TEntity>(this Table<Item> table, string name): the entity is the one type argument, and a type parameter has no model.
    private static void AnalyzeIndex(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        var name = invocation.Arguments[1].Value;
        if (name.ConstantValue is { HasValue: true, Value: string index }
            && invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol entity
            && models.ModelOf(entity)?.Entity is { } model
            && !model.Indexes.Any(declared => declared.IndexName == index))
        {
            var type = entity.ToMinimalDisplayString(invocation.SemanticModel!, name.Syntax.SpanStart);
            var edits = Spelling.Closest(index, model.Indexes.Select(static declared => declared.IndexName), maximum: null)
                .Select(declared => type + ".Indexes." + Names.IndexConstant(declared))
                .Select(constant => new SuggestedEdit("Use " + constant, name.Syntax.Span, constant))
                .ToList();
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.UndeclaredIndex, name.Syntax.GetLocation(), edits, entity.Name, index));
        }
    }

    // orders.CreateTable().Including<Customer>(): the table has the key of its entity, and a table without one the key of whatever came first, because every entity has to agree with the table.
    private static void AnalyzeIncluding(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (invocation.TargetMethod.TypeArguments[0] is not INamedTypeSymbol included || models.ModelOf(included)?.Entity is not { } entity)
        {
            return;
        }

        (INamedTypeSymbol Type, EntityModel Entity)? first = null;
        var current = invocation.Instance;
        while (current is IInvocationOperation call && SymbolEqualityComparer.Default.Equals(call.Type, invocation.Type))
        {
            if (call.TargetMethod.Name == IncludingClause && call.TargetMethod.TypeArguments[0] is INamedTypeSymbol earlier && models.ModelOf(earlier)?.Entity is { } known)
            {
                first = (earlier, known);
            }
            else if (call.TargetMethod.Name == CreateTableVerb && call.Instance?.Type is INamedTypeSymbol { TypeArguments.Length: 1 } holder && holder.TypeArguments[0] is INamedTypeSymbol table && models.ModelOf(table)?.Entity is { } own)
            {
                first = (table, own);
            }

            current = call.Instance;
        }

        if (first is { } other && KeyOf(other.Entity) != KeyOf(entity))
        {
            var name = ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)invocation.Syntax).Expression).Name;
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IncludedKeyDiffers, name.GetLocation(), other.Type.Name, KeyOf(other.Entity), included.Name, KeyOf(entity)));
        }
    }

    // TableSchema.Add compares the names of the keys, so this is the text its message gives: (pk, sk) or (pk).
    private static string KeyOf(EntityModel entity) => entity.SortKey is { } sortKey ? $"({entity.PartitionKey.AttributeName}, {sortKey.AttributeName})" : $"({entity.PartitionKey.AttributeName})";

    // shop.BatchWrite().Put(lockedA).Put(lockedB): BatchWriteOperation notes every write to a table of an entity with a version, and WriteState.Settle throws for it at the terminal
    // unless IgnoreVersion stands anywhere in the batch. The first such write of a chain is reported, and a batch that leaves the member, where a caller may add IgnoreVersion, is not.
    private static void AnalyzeBatchWrite(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (VersionedEntityOfBatchWrite(invocation, models) is not { } entity
            || DynaSharpCalls.ChainCalls(invocation, IgnoreVersion)
            || DynaSharpCalls.LeavesMember(invocation)
            || EarlierVersionedBatchWrite(invocation, models))
        {
            return;
        }

        var syntax = (InvocationExpressionSyntax)invocation.Syntax;
        List<SuggestedEdit> edits = [SuggestedEdit.Of("Add .IgnoreVersion() to the batch", ChainSyntax.Append(syntax, [IgnoreVersionCall]))];
        context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.VersionedBatchWrite, ((MemberAccessExpressionSyntax)syntax.Expression).Name.GetLocation(), edits, entity.Name));
    }

    // orders.Update(key).Set(...): a key carries no version, so WriteRules.ExpectVersion leaves it to the chain, and WriteState.Settle throws at the terminal unless IfVersion or IgnoreVersion
    // came. A command is decided as a batch is, by its type in all of the member. A step is decided by its own clauses up to the next verb, because every update of a transaction is an UpdateStep.
    private static void AnalyzeWriteByKey(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        var method = invocation.TargetMethod;
        var entity = method.ContainingType.Name == TableClass ? method.ContainingType.TypeArguments[0] : DynaSharpTypes.TableTypeOf(method);
        if (entity is not INamedTypeSymbol named
            || models.ModelOf(named)?.Entity?.VersionProperty is null
            || (method.ContainingType.Name == TableClass ? CommandSettlesVersion(invocation) : StepSettlesVersion(invocation)))
        {
            return;
        }

        var syntax = (InvocationExpressionSyntax)invocation.Syntax;
        var name = ((MemberAccessExpressionSyntax)syntax.Expression).Name;
        var placeholder = FreeName(invocation.SemanticModel!, name.SpanStart);
        List<SuggestedEdit> edits =
        [
            SuggestedEdit.Of("Add .IgnoreVersion()", ChainSyntax.Append(syntax, [IgnoreVersionCall])),
            SuggestedEdit.Of("Add .IfVersion(" + placeholder + ")", ChainSyntax.Append(syntax, [IfVersion + "(" + placeholder + ")"])),
        ];
        context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.UnsettledVersionByKey, name.GetLocation(), edits, named.Name, method.Name == UpdateVerb ? "update" : "delete"));
    }

    // Only the caller knows the version it read, so IfVersion gets a name that nothing in scope has, version unless that is taken: the code does not compile until the author puts the version there,
    // where a local that happened to be called so would compile with a value nobody chose.
    private static string FreeName(SemanticModel model, int position)
    {
        var name = VersionPlaceholders.FirstOrDefault(candidate => model.LookupSymbols(position, name: candidate).IsEmpty);
        for (var number = 2; name is null; number++)
        {
            var candidate = VersionPlaceholders[VersionPlaceholders.Length - 1] + number.ToString(CultureInfo.InvariantCulture);
            name = model.LookupSymbols(position, name: candidate).IsEmpty ? candidate : null;
        }

        return name;
    }

    // A command that leaves the member can get the clause where it goes.
    private static bool CommandSettlesVersion(IInvocationOperation invocation) =>
        DynaSharpCalls.ChainCalls(invocation, IfVersion) || DynaSharpCalls.ChainCalls(invocation, IgnoreVersion) || DynaSharpCalls.LeavesMember(invocation);

    // The calls on the step itself, up to the next verb or the terminal. A step that goes anywhere else, into a local, an argument or a conversion, or into a method of the consumer, counts as settled.
    private static bool StepSettlesVersion(IInvocationOperation verb)
    {
        IOperation current = verb;
        while (DynaSharpCalls.NextCall(current) is { } next)
        {
            if (!DynaSharpCalls.IsOfTheLibrary(next))
            {
                return true;
            }

            // A verb takes a table; IfExists and IfNotExists of a step are extension methods beside the verbs and take none.
            if (!SymbolEqualityComparer.Default.Equals(next.Type, verb.Type) || (DynaSharpCalls.IsStepVerb(next) && DynaSharpTypes.TableTypeOf(next.TargetMethod) is not null))
            {
                return false;
            }

            if (next.TargetMethod.Name is IfVersion or IgnoreVersion)
            {
                return true;
            }

            current = next;
        }

        return true;
    }

    // A write of a batch returns the batch: an instance method of BatchWriteCommand<T>, or an extension method such as DeleteAll(entities) of EntityTableExtensions.
    private static bool IsBatchWrite(IInvocationOperation invocation) =>
        invocation.Type is INamedTypeSymbol { Name: BatchWriteCommand, Arity: 1 } && BatchWrites.Contains(invocation.TargetMethod.Name);

    // Put<TOther>(Table<TOther> table, TOther item) and the deletes on another table write to the table they take, the others to the table of the batch.
    private static INamedTypeSymbol? VersionedEntityOfBatchWrite(IInvocationOperation invocation, ModelCatalog models)
    {
        var entity = DynaSharpTypes.TableTypeOf(invocation.TargetMethod) ?? ((INamedTypeSymbol)invocation.Type!).TypeArguments[0];
        return entity is INamedTypeSymbol named && models.ModelOf(named)?.Entity?.VersionProperty is not null ? named : null;
    }

    private static bool EarlierVersionedBatchWrite(IInvocationOperation invocation, ModelCatalog models)
    {
        var current = DynaSharpCalls.ReceiverOf(invocation);
        while (current is IInvocationOperation call && SymbolEqualityComparer.Default.Equals(call.Type, invocation.Type))
        {
            if (IsBatchWrite(call) && VersionedEntityOfBatchWrite(call, models) is not null)
            {
                return true;
            }

            current = DynaSharpCalls.ReceiverOf(call);
        }

        return false;
    }

    private static void AnalyzeIfNotExists(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (EntityOf(invocation, PutVerb) is { } entity
            && models.ModelOf(entity)?.Entity?.VersionProperty is { } version
            && !DynaSharpCalls.ChainCalls(invocation, IgnoreVersion)
            && VersionAboveZero(ItemOfPut(invocation), version) is { } value)
        {
            var syntax = (InvocationExpressionSyntax)invocation.Syntax;
            var name = syntax.Expression.GetLastToken();
            List<SuggestedEdit> edits =
            [
                new("Set " + version + " = 0", value.Syntax.Span, "0"),
                SuggestedEdit.Of("Add IgnoreVersion()", ChainSyntax.Append(syntax, [IgnoreVersionCall])),
            ];
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.IfNotExistsOnVersionedPut, name.GetLocation(), edits, entity.Name));
        }
    }

    private static IOperation? ItemOfPut(IInvocationOperation invocation)
    {
        var call = DynaSharpCalls.ReceiverOf(invocation);
        while (call is IInvocationOperation { TargetMethod.Name: not PutVerb } clause)
        {
            call = DynaSharpCalls.ReceiverOf(clause);
        }

        return (call as IInvocationOperation)?.Arguments.Where(static argument => argument.Parameter!.Name == ItemParameter).Select(static argument => argument.Value).FirstOrDefault();
    }

    // The value above 0 that the item of the put sets its version to, or null.
    private static IOperation? VersionAboveZero(IOperation? item, string version) => item switch
    {
        IObjectCreationOperation creation => SetAboveZero(creation.Initializer, version),
        IWithOperation copy => SetAboveZero(copy.Initializer, version),
        _ => null,
    };

    private static IOperation? SetAboveZero(IObjectOrCollectionInitializerOperation? initializer, string property) =>
        initializer?.Initializers.OfType<ISimpleAssignmentOperation>().FirstOrDefault(assignment =>
            assignment.Target is IPropertyReferenceOperation target
            && target.Property.Name == property
            && assignment.Value.ConstantValue is { HasValue: true, Value: var value }
            && Convert.ToDecimal(value, CultureInfo.InvariantCulture) > 0)?.Value;

    private static void AnalyzeWrite(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (invocation.TargetMethod.Parameters[0].Name != DynaSharpCalls.HandleParameter
            || DynaSharpCalls.HandleUnder(invocation.Arguments[0].Value) is not { } handle
            || DynaSharpCalls.OwnerOfHandle(handle) is not { } owner
            || EntityOf(invocation, UpdateVerb) is not { } entity
            || !DynaSharpCalls.Inherits(entity, owner)
            || models.ModelOf(entity)?.Entity is not { } model)
        {
            return;
        }

        var location = invocation.Arguments[0].Value.Syntax.GetLocation();
        if (model.FeedsTableKey(handle.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.WriteOfTableKeyInput, location, handle.Name, entity.Name));
        }
        else if (model.IndexKeysDerivedFrom(handle.Name).FirstOrDefault() is { Key: not null } derived)
        {
            var text = derived.Key.Template?.Text ?? "{" + derived.Key.PlainProperty + "}";
            var reason = derived.Input == handle.Name
                ? $"is computed by its getter for the key template {text} of the index {derived.Index}"
                : $"feeds '{derived.Input}', which the key template {text} of the index {derived.Index} is computed from";
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.WriteOfComputedInputSource, location, handle.Name, reason));
        }
    }

    // A method of the library with one of these names is an instance or an extension method, so there is a receiver.
    private static INamedTypeSymbol? EntityOf(IInvocationOperation invocation, string verb) => DynaSharpCalls.EntityOfCommand(DynaSharpCalls.ReceiverOf(invocation)!, verb);
}
