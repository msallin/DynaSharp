using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

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
    /// <summary>The parameter that takes the handle in a write of <see cref="WriteMethods"/>.</summary>
    internal const string HandleParameter = "attribute";

    /// <summary>The static class of the library whose <c>Index</c> takes the name of an index an entity declares.</summary>
    internal const string EntityIndexes = "EntityTableExtensions";

    /// <summary>The members of <c>IUpdatable&lt;TSelf&gt;</c> that take a handle, by name.</summary>
    internal static readonly HashSet<string> WriteMethods = ["Set", "SetIfNotNull", "Add", "Remove", "SetIfNotExists", "Delete"];

    /// <summary>The writes by key that leave the version to the chain, by type and name: the commands of <c>Table&lt;T&gt;</c> and the steps of a transaction that take a key.</summary>
    internal static readonly HashSet<string> WritesByKey = ["Table.Update", "Table.Delete", "TransactWriteExtensions.Update", "TransactWriteExtensions.Delete"];

    private const string UpdateVerb = "Update";

    private const string PutVerb = "Put";

    private const string IgnoreVersion = "IgnoreVersion";

    private const string IfVersion = "IfVersion";

    private const string DeleteVerb = "Delete";

    private const string KeyParameter = "key";

    private const string TableClass = "Table";

    private const string ItemParameter = "item";

    private const string HandlesClass = "Attrs";

    private const string StepVerbs = "TransactWriteExtensions";

    private const string NestedPath = "At";

    private const string IncludingClause = "Including";

    private const string CreateTableVerb = "CreateTable";

    private const string BatchWriteCommand = "BatchWriteCommand";

    // The writes of BatchWriteCommand<T>, each of which settles a version that a batch cannot carry, and the deletes by an entity that extend it.
    private static readonly HashSet<string> BatchWrites = ["Put", "PutAll", "Delete", "DeleteAll"];

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

    /// <summary>Whether a clause stands in the chain of a command. It holds wherever it stands, also after this call, so this looks at all of the member.</summary>
    /// <param name="invocation">A call on the chain.</param>
    /// <param name="clause">The name of the clause.</param>
    /// <returns>Whether the member calls it on a command of this type.</returns>
    internal static bool ChainCalls(IInvocationOperation invocation, string clause)
    {
        IOperation root = invocation;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        return root.Descendants().OfType<IInvocationOperation>()
            .Any(other => other.TargetMethod.Name == clause && SymbolEqualityComparer.Default.Equals(other.Type, invocation.Type));
    }

    /// <summary>Whether a type is the owner of a handle or derives from it, so that the handle is one of its attributes.</summary>
    /// <param name="entity">The type of the command.</param>
    /// <param name="owner">The type that declares the handle.</param>
    /// <returns>Whether it is that type or one of its base types.</returns>
    internal static bool Inherits(INamedTypeSymbol entity, INamedTypeSymbol? owner)
    {
        for (var type = entity; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, owner))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The handle a path starts at: <c>Order.Attrs.Message.At(Msg.Attrs.Arrival).At(...)</c> starts at <c>Order.Attrs.Message</c>, because <c>At</c> is an extension method, so its document comes first.</summary>
    /// <param name="value">The argument that takes the handle.</param>
    /// <returns>The property of the handle, or null for one in a variable or from a method.</returns>
    internal static IPropertySymbol? HandleUnder(IOperation value)
    {
        while (value is IInvocationOperation { TargetMethod.Name: NestedPath } nested)
        {
            value = nested.Arguments[0].Value;
        }

        return (value as IPropertyReferenceOperation)?.Property;
    }

    /// <summary>The keys of indexes that a computed input feeds, for that input and for each property it is derived from.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="property">The property a handle writes.</param>
    /// <returns>The computed input, the index and the key, for each key the property reaches through a getter.</returns>
    internal static IEnumerable<(string Input, string Index, KeyModel Key)> IndexKeysDerivedFrom(EntityModel entity, string property) =>
        from input in entity.ComputedInputs.Where(input => input.Property == property || input.Dependencies.Contains(property))
        from index in entity.Indexes
        from key in new[] { index.PartitionKey, index.SortKey }.OfType<KeyModel>()
        where Holds(key, input.Property)
        select (input.Property, index.IndexName, key);

    /// <summary>Whether a property feeds a key of the table, itself or through a computed input.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="property">The property.</param>
    /// <returns>Whether an update of it by handle throws, because DynamoDB cannot update a key.</returns>
    internal static bool FeedsTableKey(EntityModel entity, string property) =>
        new[] { entity.PartitionKey, entity.SortKey }.OfType<KeyModel>().Any(key => Holds(key, property) || entity.ComputedInputs.Any(input => input.Dependencies.Contains(property) && Holds(key, input.Property)));

    /// <summary>Whether a key holds a property, as its plain key or as a placeholder of its template.</summary>
    /// <param name="key">The key.</param>
    /// <param name="property">The property.</param>
    /// <returns>Whether the key is made of it.</returns>
    internal static bool Holds(KeyModel key, string property) =>
        key.PlainProperty == property || (key.Template?.Segments ?? default).OfType<PlaceholderSegment>().Any(placeholder => placeholder.Property == property);

    /// <summary>The entity of a command or a step of a verb: the type argument of <c>PutCommand&lt;T&gt;</c>, or the table of the verb a <c>PutStep</c> came from.</summary>
    /// <param name="receiver">The command or the step.</param>
    /// <param name="verb">The verb, such as <c>Put</c>.</param>
    /// <returns>The entity, or null for another type, a type parameter and a step that cannot be followed to its verb.</returns>
    internal static INamedTypeSymbol? EntityOfCommand(IOperation receiver, string verb)
    {
        if (receiver.Type is not INamedTypeSymbol type)
        {
            return null;
        }

        return type.Name == verb + "Command" ? type.TypeArguments[0] as INamedTypeSymbol : type.Name == verb + "Step" ? EntityOfStep(receiver, verb) : null;
    }

    /// <summary>
    /// Whether a method is <c>Table&lt;T&gt;.Update(key)</c>, <c>Table&lt;T&gt;.Delete(key)</c>, or a step of a transaction that takes a table and a key. Those that take the values of a key are on a table
    /// without a schema, which has no model.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>Whether it is a write by a key the generator built.</returns>
    internal static bool IsWriteByKey(IMethodSymbol method) =>
        method.Name is UpdateVerb or DeleteVerb
        && WritesByKey.Contains(method.ContainingType.Name + "." + method.Name)
        && method.Parameters.Any(static parameter => parameter.Name == KeyParameter);

    /// <summary>The call that takes this value as its receiver: one it is the instance of, or an extension method it is the first argument of. A value that is an argument is no instance.</summary>
    /// <param name="operation">The value, such as a call on a chain.</param>
    /// <returns>The call, or null where the value goes anywhere else.</returns>
    internal static IInvocationOperation? NextCall(IOperation operation) => operation.Parent switch
    {
        IInvocationOperation call => call,
        IArgumentOperation argument => argument.Parent is IInvocationOperation call && ReceiverOf(call) == operation ? call : null,
        _ => null,
    };

    /// <summary>Whether the member hands a command of this type on, by a return, an argument or a store into anything but a local, so that a clause can still follow there.</summary>
    /// <param name="invocation">A call that gives the command.</param>
    /// <returns>Whether code outside the member can add to it.</returns>
    internal static bool LeavesMember(IInvocationOperation invocation)
    {
        IOperation root = invocation;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        var batch = invocation.Type;
        return root.Descendants().Any(operation => operation switch
        {
            IReturnOperation returned => Is(returned.ReturnedValue, batch),
            IArgumentOperation argument => Is(argument.Value, batch) && !IsReceiverOfTheLibrary(argument),
            ISimpleAssignmentOperation assignment => Is(assignment.Value, batch) && assignment.Target is not ILocalReferenceOperation,
            _ => false,
        });
    }

    /// <summary>A call after this one in its chain, on a line of its own with the indent of this one where the chain has one call per line.</summary>
    /// <param name="call">The call, a member access.</param>
    /// <param name="next">The call to add, as written after the dot.</param>
    /// <returns>The text to insert after the call.</returns>
    internal static string NextCall(InvocationExpressionSyntax call, string next)
    {
        var dot = ((MemberAccessExpressionSyntax)call.Expression).OperatorToken;
        var lineEnd = dot.GetPreviousToken().TrailingTrivia.LastOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        return lineEnd == default ? "." + next : lineEnd.ToFullString() + dot.LeadingTrivia.ToFullString() + "." + next;
    }

    private static void Analyze(OperationAnalysisContext context, ModelCatalog models)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType))
        {
            return;
        }

        if (IsBatchWrite(invocation))
        {
            AnalyzeBatchWrite(context, invocation, models);
        }
        else if (IsWriteByKey(invocation.TargetMethod))
        {
            AnalyzeWriteByKey(context, invocation, models);
        }
        else if (WriteMethods.Contains(invocation.TargetMethod.Name))
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
            || ChainCalls(invocation, IgnoreVersion)
            || LeavesMember(invocation)
            || EarlierVersionedBatchWrite(invocation, models))
        {
            return;
        }

        var name = ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)invocation.Syntax).Expression).Name;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.VersionedBatchWrite, name.GetLocation(), entity.Name));
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

        var name = ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)invocation.Syntax).Expression).Name;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.UnsettledVersionByKey, name.GetLocation(), named.Name, method.Name == UpdateVerb ? "update" : "delete"));
    }

    // A command that leaves the member can get the clause where it goes.
    private static bool CommandSettlesVersion(IInvocationOperation invocation) =>
        ChainCalls(invocation, IfVersion) || ChainCalls(invocation, IgnoreVersion) || LeavesMember(invocation);

    // The calls on the step itself, up to the next verb or the terminal. A step that goes anywhere else, into a local, an argument or a conversion, or into a method of the consumer, counts as settled.
    private static bool StepSettlesVersion(IInvocationOperation verb)
    {
        IOperation current = verb;
        while (NextCall(current) is { } next)
        {
            if (!DynaSharpTypes.IsDeclaredByDynaSharp(next.TargetMethod.ContainingType))
            {
                return true;
            }

            // A verb takes a table; IfExists and IfNotExists of a step are extension methods beside the verbs and take none.
            if (!SymbolEqualityComparer.Default.Equals(next.Type, verb.Type) || (next.TargetMethod.ContainingType.Name == StepVerbs && DynaSharpTypes.TableTypeOf(next.TargetMethod) is not null))
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
        var current = ReceiverOf(invocation);
        while (current is IInvocationOperation call && SymbolEqualityComparer.Default.Equals(call.Type, invocation.Type))
        {
            if (IsBatchWrite(call) && VersionedEntityOfBatchWrite(call, models) is not null)
            {
                return true;
            }

            current = ReceiverOf(call);
        }

        return false;
    }

    // batch.DeleteAll(entities) is an extension method of the library, whose first argument is the batch it extends and goes nowhere else.
    private static bool IsReceiverOfTheLibrary(IArgumentOperation argument) =>
        argument.Parent is IInvocationOperation { TargetMethod.IsExtensionMethod: true } call && DynaSharpTypes.IsDeclaredByDynaSharp(call.TargetMethod.ContainingType) && argument.Parameter!.Ordinal == 0;

    private static bool Is(IOperation? value, ITypeSymbol? type) => value is not null && SymbolEqualityComparer.Default.Equals(value.Type, type);

    private static void AnalyzeIfNotExists(OperationAnalysisContext context, IInvocationOperation invocation, ModelCatalog models)
    {
        if (EntityOf(invocation, PutVerb) is { } entity
            && models.ModelOf(entity)?.Entity?.VersionProperty is { } version
            && !ChainCalls(invocation, IgnoreVersion)
            && VersionAboveZero(ItemOfPut(invocation), version) is { } value)
        {
            var syntax = (InvocationExpressionSyntax)invocation.Syntax;
            var name = syntax.Expression.GetLastToken();
            List<SuggestedEdit> edits =
            [
                new("Set " + version + " = 0", value.Syntax.Span, "0"),
                new("Add IgnoreVersion()", new TextSpan(syntax.Span.End, 0), NextCall(syntax, "IgnoreVersion()")),
            ];
            context.ReportDiagnostic(SuggestedEdit.Create(Descriptors.IfNotExistsOnVersionedPut, name.GetLocation(), edits, entity.Name));
        }
    }

    private static IOperation? ItemOfPut(IInvocationOperation invocation)
    {
        var call = ReceiverOf(invocation);
        while (call is IInvocationOperation { TargetMethod.Name: not PutVerb } clause)
        {
            call = ReceiverOf(clause);
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
        if (invocation.TargetMethod.Parameters[0].Name != HandleParameter
            || HandleUnder(invocation.Arguments[0].Value) is not { ContainingType.Name: HandlesClass } handle
            || EntityOf(invocation, UpdateVerb) is not { } entity
            || !Inherits(entity, handle.ContainingType.ContainingType)
            || models.ModelOf(entity)?.Entity is not { } model)
        {
            return;
        }

        var location = invocation.Arguments[0].Value.Syntax.GetLocation();
        if (FeedsTableKey(model, handle.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.WriteOfTableKeyInput, location, handle.Name, entity.Name));
        }
        else if (IndexKeysDerivedFrom(model, handle.Name).FirstOrDefault() is { Key: not null } derived)
        {
            var text = derived.Key.Template?.Text ?? "{" + derived.Key.PlainProperty + "}";
            var reason = derived.Input == handle.Name
                ? $"is computed by its getter for the key template {text} of the index {derived.Index}"
                : $"feeds '{derived.Input}', which the key template {text} of the index {derived.Index} is computed from";
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.WriteOfComputedInputSource, location, handle.Name, reason));
        }
    }

    private static IOperation? ReceiverOf(IInvocationOperation invocation) =>
        invocation.Instance ?? (invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments[0].Value : null);

    // A method of the library with one of these names is an instance or an extension method, so there is a receiver.
    private static INamedTypeSymbol? EntityOf(IInvocationOperation invocation, string verb) => EntityOfCommand(ReceiverOf(invocation)!, verb);

    // The verb takes the table: Update<TChain, T, TKey>(chain, table, key), and Update<TChain>(chain, Table<Item> table, ...) for a table without a schema.
    private static INamedTypeSymbol? EntityOfStep(IOperation step, string verb)
    {
        var type = step.Type;
        while (step is IInvocationOperation invocation && SymbolEqualityComparer.Default.Equals(invocation.Type, type))
        {
            if (invocation.TargetMethod is { ContainingType.Name: StepVerbs } method && method.Name == verb)
            {
                return DynaSharpTypes.TableTypeOf(method) as INamedTypeSymbol;
            }

            step = ReceiverOf(invocation)!;
        }

        return null;
    }
}
