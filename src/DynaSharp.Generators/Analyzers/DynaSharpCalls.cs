using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DynaSharp.Generators;

/// <summary>What the analyzers read of a call into the library and of what it takes: its receiver, the calls of its chain, its arguments and the handles among them.</summary>
internal static class DynaSharpCalls
{
    /// <summary>The parameter that takes the handle in a write of <see cref="WriteMethods"/>.</summary>
    public const string HandleParameter = "attribute";

    /// <summary>The members of <c>IUpdatable&lt;TSelf&gt;</c> that take a handle, by name.</summary>
    public static readonly HashSet<string> WriteMethods = ["Set", "SetIfNotNull", "Add", "Remove", "SetIfNotExists", "Delete"];

    /// <summary>The writes by key that leave the version to the chain, by type and name: the commands of <c>Table&lt;T&gt;</c> and the steps of a transaction that take a key.</summary>
    public static readonly HashSet<string> WritesByKey = ["Table.Update", "Table.Delete", "TransactWriteExtensions.Update", "TransactWriteExtensions.Delete"];

    private const string StepVerbs = "TransactWriteExtensions";

    private const string KeyParameter = "key";

    private const string NestedPath = "At";

    /// <summary>Whether a call goes to a method of the library.</summary>
    /// <param name="invocation">The call.</param>
    /// <returns>Whether the type that declares the method is in the namespace of the library.</returns>
    public static bool IsOfTheLibrary(IInvocationOperation invocation) => DynaSharpTypes.IsDeclaredByDynaSharp(invocation.TargetMethod.ContainingType);

    /// <summary>The value a call is made on. A static method of the consumer under the namespace of the library counts as the library, and it has no receiver.</summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The instance, the first argument of an extension method, or null.</returns>
    public static IOperation? ReceiverOf(IInvocationOperation invocation) => invocation.Instance ?? (invocation.TargetMethod.IsExtensionMethod ? invocation.Arguments[0].Value : null);

    /// <summary>The call that takes this value as its receiver: one it is the instance of, or an extension method it is the first argument of. A value that is an argument is no instance.</summary>
    /// <param name="operation">The value, such as a call on a chain.</param>
    /// <returns>The call, or null where the value goes anywhere else.</returns>
    public static IInvocationOperation? NextCall(IOperation operation) => operation.Parent switch
    {
        IInvocationOperation call => call,
        IArgumentOperation argument => argument.Parent is IInvocationOperation call && ReceiverOf(call) == operation ? call : null,
        _ => null,
    };

    /// <summary>The argument of a parameter. Arguments come in the order of the source, which a named argument can change.</summary>
    /// <param name="call">The call.</param>
    /// <param name="ordinal">The position of the parameter.</param>
    /// <returns>The value of the argument.</returns>
    public static IOperation ArgumentOf(IInvocationOperation call, int ordinal) => call.Arguments.First(argument => argument.Parameter!.Ordinal == ordinal).Value;

    /// <summary>Whether a clause stands in the chain of a command. It holds wherever it stands, also after this call, so this looks at all of the member.</summary>
    /// <param name="invocation">A call on the chain.</param>
    /// <param name="clause">The name of the clause.</param>
    /// <returns>Whether the member calls it on a command of this type.</returns>
    public static bool ChainCalls(IInvocationOperation invocation, string clause) =>
        RootOf(invocation).Descendants().OfType<IInvocationOperation>()
            .Any(other => other.TargetMethod.Name == clause && SymbolEqualityComparer.Default.Equals(other.Type, invocation.Type));

    /// <summary>Whether the member hands a command of this type on, by a return, an argument or a store into anything but a local, so that a clause can still follow there.</summary>
    /// <param name="invocation">A call that gives the command.</param>
    /// <returns>Whether code outside the member can add to it.</returns>
    public static bool LeavesMember(IInvocationOperation invocation)
    {
        var command = invocation.Type;
        return RootOf(invocation).Descendants().Any(operation => operation switch
        {
            IReturnOperation returned => Is(returned.ReturnedValue, command),
            IArgumentOperation argument => Is(argument.Value, command) && !IsReceiverOfTheLibrary(argument),
            ISimpleAssignmentOperation assignment => Is(assignment.Value, command) && assignment.Target is not ILocalReferenceOperation,
            _ => false,
        });
    }

    /// <summary>Whether the next call is on the same update: another action or option of the command, but not the verb of the next step of a transaction, which has the same type.</summary>
    /// <param name="invocation">A call that returns an update.</param>
    /// <returns>Whether the update goes on after it.</returns>
    public static bool GoesOn(IInvocationOperation invocation) => invocation.Parent switch
    {
        IInvocationOperation next when next.Instance == invocation => SymbolEqualityComparer.Default.Equals(next.Type, invocation.Type),
        IArgumentOperation { Parent: IInvocationOperation next } argument when next.TargetMethod.IsExtensionMethod && next.Arguments[0] == argument
            => SymbolEqualityComparer.Default.Equals(next.Type, invocation.Type) && !IsStepVerb(next),
        _ => false,
    };

    /// <summary>Whether a call is a verb that starts a step of a transaction.</summary>
    /// <param name="call">The call.</param>
    /// <returns>Whether it is declared by <c>TransactWriteExtensions</c>.</returns>
    public static bool IsStepVerb(IInvocationOperation call) => call.TargetMethod.ContainingType.Name == StepVerbs;

    /// <summary>
    /// Whether a method is <c>Table&lt;T&gt;.Update(key)</c>, <c>Table&lt;T&gt;.Delete(key)</c>, or a step of a transaction that takes a table and a key. Those that take the values of a key are on a table
    /// without a schema, which has no model.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>Whether it is a write by a key the generator built.</returns>
    public static bool IsWriteByKey(IMethodSymbol method) =>
        WritesByKey.Contains(method.ContainingType.Name + "." + method.Name)
        && method.Parameters.Any(static parameter => parameter.Name == KeyParameter);

    /// <summary>The entity of a command or a step of a verb: the type argument of <c>PutCommand&lt;T&gt;</c>, or the table of the verb a <c>PutStep</c> came from.</summary>
    /// <param name="receiver">The command or the step.</param>
    /// <param name="verb">The verb, such as <c>Put</c>.</param>
    /// <returns>The entity, or null for another type, a type parameter and a step that cannot be followed to its verb.</returns>
    public static INamedTypeSymbol? EntityOfCommand(IOperation receiver, string verb)
    {
        if (receiver.Type is not INamedTypeSymbol type)
        {
            return null;
        }

        return type.Name == verb + "Command" ? type.TypeArguments[0] as INamedTypeSymbol : type.Name == verb + "Step" ? EntityOfStep(receiver, verb) : null;
    }

    /// <summary>The type a handle belongs to: <c>Order</c> for <c>Order.Attrs.Total</c>.</summary>
    /// <param name="property">A property.</param>
    /// <returns>The type, or null for a property that is no handle.</returns>
    public static INamedTypeSymbol? OwnerOfHandle(IPropertySymbol property) => property.ContainingType is { Name: Names.HandlesClass, ContainingType: { } owner } ? owner : null;

    /// <summary>The model of the property a handle stands for: <c>Total</c> for <c>Order.Attrs.Total</c>.</summary>
    /// <param name="value">The value that may be a handle.</param>
    /// <param name="models">The models of the compilation.</param>
    /// <returns>The property, or null for a value that is no handle or a handle of a type without a model.</returns>
    public static PropertyModel? PropertyOfHandle(IOperation value, ModelCatalog models) =>
        value is IPropertyReferenceOperation { Property: var handle } && OwnerOfHandle(handle) is { } owner && models.ModelOf(owner) is { } model ? model.Property(handle.Name) : null;

    /// <summary>Whether a value counts as null for a handle: a <see langword="null"/>, and an empty string where <c>[OmitEmpty]</c> counts it as null.</summary>
    /// <param name="value">The value of the action.</param>
    /// <param name="attribute">The handle of the action.</param>
    /// <param name="models">The models of the compilation.</param>
    /// <returns>Whether a <c>SetIfNotNull</c> of it writes nothing, and a <c>Set</c> of it removes the attribute.</returns>
    public static bool AddsNothing(IOperation value, IOperation attribute, ModelCatalog models) => value switch
    {
        { ConstantValue: { HasValue: true, Value: null } } => true,
        { ConstantValue: { HasValue: true, Value: "" } } or IFieldReferenceOperation { Field: { Name: nameof(string.Empty), ContainingType.SpecialType: SpecialType.System_String } } =>
            PropertyOfHandle(attribute, models)?.OmitEmptyConverter is not null,
        _ => false,
    };

    /// <summary>The handle a path starts at: <c>Order.Attrs.Message.At(Msg.Attrs.Arrival).At(...)</c> starts at <c>Order.Attrs.Message</c>, because <c>At</c> is an extension method, so its document comes first.</summary>
    /// <param name="value">The argument that takes the handle.</param>
    /// <returns>The property of the handle, or null for one in a variable or from a method.</returns>
    public static IPropertySymbol? HandleUnder(IOperation value)
    {
        while (value is IInvocationOperation { TargetMethod.Name: NestedPath } nested)
        {
            value = nested.Arguments[0].Value;
        }

        return (value as IPropertyReferenceOperation)?.Property;
    }

    /// <summary>Whether a type is the owner of a handle or derives from it, so that the handle is one of its attributes.</summary>
    /// <param name="entity">The type of the command.</param>
    /// <param name="owner">The type that declares the handle.</param>
    /// <returns>Whether it is that type or one of its base types.</returns>
    public static bool Inherits(INamedTypeSymbol entity, INamedTypeSymbol? owner)
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

    /// <summary>The local or the parameter a reference reads.</summary>
    /// <param name="reference">A reference to a local or a parameter.</param>
    /// <returns>Its symbol.</returns>
    public static ISymbol VariableOf(IOperation reference) => reference is ILocalReferenceOperation local ? local.Local : ((IParameterReferenceOperation)reference).Parameter;

    /// <summary>Whether an operation stands inside a <c>nameof</c>, which reads nothing.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>True inside <c>nameof</c>.</returns>
    public static bool IsInNameOf(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation RootOf(IOperation operation)
    {
        while (operation.Parent is { } parent)
        {
            operation = parent;
        }

        return operation;
    }

    // batch.DeleteAll(entities) is an extension method of the library, whose first argument is the batch it extends and goes nowhere else.
    private static bool IsReceiverOfTheLibrary(IArgumentOperation argument) =>
        argument.Parent is IInvocationOperation { TargetMethod.IsExtensionMethod: true } call && IsOfTheLibrary(call) && argument.Parameter!.Ordinal == 0;

    private static bool Is(IOperation? value, ITypeSymbol? type) => value is not null && SymbolEqualityComparer.Default.Equals(value.Type, type);

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
