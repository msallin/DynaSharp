using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Operations;

namespace DynaSharp.Testing;

/// <summary>
/// One action of a recorded <c>TransactWriteItems</c> or <c>TransactGetItems</c>, from <see cref="RecordedRequest.TransactItems"/>, so that a test says what a transaction did instead of walking
/// <see cref="RecordedRequest.BodyJson"/>: <c>steps[0].ReadItem&lt;EmailClaim&gt;()</c>, <c>steps[2].ConditionText</c>.
/// </summary>
/// <remarks>
/// The texts follow the notation of <see cref="RecordedRequest"/> and use the placeholders of the step, because each action of a transaction has maps of its own. An action that sets several members,
/// which only the request API can send and DynamoDB rejects, shows the first one.
/// </remarks>
public sealed class RecordedTransactStep
{
    private static readonly EnumWireNames<TransactStepKind> Kinds = new(
        nameof(TransactStepKind),
        (TransactStepKind.Put, "Put"),
        (TransactStepKind.Update, "Update"),
        (TransactStepKind.Delete, "Delete"),
        (TransactStepKind.ConditionCheck, "ConditionCheck"),
        (TransactStepKind.Get, "Get"));

    private readonly JsonElement _action;

    private RecordedTransactStep(TransactStepKind kind, JsonElement action)
    {
        Kind = kind;
        _action = action;
    }

    /// <summary>Gets what the step does.</summary>
    public TransactStepKind Kind { get; }

    /// <summary>Gets the table of the step, or <see langword="null"/> if it names none.</summary>
    public string? TableName => _action.TryGetProperty("TableName"u8, out var name) ? name.GetString() : null;

    /// <summary>Gets the <c>ConditionExpression</c> with its placeholders resolved, in the notation of <see cref="RecordedRequest"/>; <see langword="null"/> if the step has none.</summary>
    public string? ConditionText => RecordedJson.ResolveExpression(_action, "ConditionExpression");

    /// <summary>Gets the <c>UpdateExpression</c> of an update with its placeholders resolved, in the notation of <see cref="RecordedRequest"/>; <see langword="null"/> for the other kinds.</summary>
    public string? UpdateText => RecordedJson.ResolveExpression(_action, "UpdateExpression");

    /// <summary>Gets the <c>ProjectionExpression</c> of a get with its placeholders resolved, in the notation of <see cref="RecordedRequest"/>; <see langword="null"/> if the step has none.</summary>
    public string? ProjectionText => RecordedJson.ResolveExpression(_action, "ProjectionExpression");

    private string Owner => $"{Kind} step";

    /// <summary>Reads the <c>Item</c> of a put.</summary>
    /// <returns>A new item with the attributes as they went over the wire.</returns>
    /// <exception cref="InvalidOperationException">The step has no <c>Item</c>.</exception>
    public Item ReadItem() => ReadItem<Item>();

    /// <summary>Reads the <c>Item</c> of a put the way a response that carries it is read.</summary>
    /// <typeparam name="T">The type to read: a generated entity or <see cref="Item"/>.</typeparam>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The step has no <c>Item</c>.</exception>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted, or the item is of another type.</exception>
    public T ReadItem<T>()
        where T : IDynamoDbDocument<T> => RecordedJson.ReadDocument<T>(_action, "Item", Owner);

    /// <summary>Reads the <c>Key</c> of every kind but a put. <see cref="StubResponse.KeyOf(IKey)"/> gives what to compare it with.</summary>
    /// <returns>A new item with the key attributes.</returns>
    /// <exception cref="InvalidOperationException">The step has no <c>Key</c>.</exception>
    public Item ReadKey() => ReadKey<Item>();

    /// <summary>Reads the <c>Key</c> through a type, with the limits of <see cref="RecordedRequest.ReadKey{T}"/>.</summary>
    /// <typeparam name="T">The type to read, which can be made from the key attributes alone.</typeparam>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The step has no <c>Key</c>.</exception>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted.</exception>
    public T ReadKey<T>()
        where T : IDynamoDbDocument<T> => RecordedJson.ReadDocument<T>(_action, "Key", Owner);

    internal static RecordedTransactStep From(JsonElement action)
    {
        using var members = action.EnumerateObject();
        if (!members.MoveNext())
        {
            return new RecordedTransactStep(TransactStepKind.Unknown, action);
        }

        Kinds.TryGetValue(members.Current.Name, out var kind);
        return new RecordedTransactStep(kind, members.Current.Value);
    }
}
