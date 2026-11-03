using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The options <c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c> share: the condition with its maps, the update of <c>UpdateItem</c>, what to return, the consumed capacity and
/// the item collection metrics. <see cref="Read"/> checks them all before the write changes anything, <see cref="ThrowIfConditionFails"/> runs on the item as it is, and
/// <see cref="Answer"/> writes the response of the write that went through.
/// </summary>
/// <param name="Condition">The condition, or <see langword="null"/>.</param>
/// <param name="Update">The update of <c>UpdateItem</c>, or <see langword="null"/>.</param>
/// <param name="ReturnValues">The <c>ReturnValues</c>.</param>
/// <param name="OnConditionFailure">The <c>ReturnValuesOnConditionCheckFailure</c>.</param>
/// <param name="ConsumedCapacity">The <c>ReturnConsumedCapacity</c>.</param>
/// <param name="ItemCollectionMetrics">The <c>ReturnItemCollectionMetrics</c>.</param>
internal sealed record SingleWrite(
    ParsedCondition? Condition,
    ParsedUpdate? Update,
    ReturnValue ReturnValues,
    ReturnValuesOnConditionCheckFailure OnConditionFailure,
    ReturnConsumedCapacity ConsumedCapacity,
    ReturnItemCollectionMetrics ItemCollectionMetrics)
{
    /// <summary>Reads the options of a write before it changes anything.</summary>
    /// <param name="request">The request.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>The options.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a value DynamoDB rejects, such as <c>ReturnValues: ALL_NEW</c> on a <c>PutItem</c>, or maps without an expression.</exception>
    public static SingleWrite Read(JsonElement request, string operation)
    {
        var returnValues = Enum(request, "ReturnValues", WireNames.ReturnValues);
        WriteReturns.Check(operation, returnValues);
        var onFailure = Enum(request, "ReturnValuesOnConditionCheckFailure", WireNames.ReturnValuesOnConditionCheckFailures);
        var capacity = Enum(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);
        var metrics = Enum(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues);
        var (condition, update) = ExpressionMembers.ReadWrite(request, operation == UpdateItem.Name);
        return new SingleWrite(condition, update, returnValues, onFailure, capacity, metrics);
    }

    /// <summary>Reads an enum member whose zero member is <c>NONE</c>, which is also what a missing member means.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="member">The member.</param>
    /// <param name="names">The spellings.</param>
    /// <returns>The value, the zero member if the request has none.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a name DynamoDB does not have.</exception>
    public static TEnum Enum<TEnum>(JsonElement request, string member, EnumWireNames<TEnum> names)
        where TEnum : struct, System.Enum =>
        RequestJson.Optional(request, member) is { } value ? RequestJson.ReadEnum(value, member, names, noneIsDefault: true) : default;

    /// <summary>Checks the condition on the item as it is.</summary>
    /// <param name="current">The item, or <see langword="null"/> if there is none.</param>
    /// <exception cref="FakeError">A <c>ConditionalCheckFailedException</c>, with the item when the request asked for it with <c>ALL_OLD</c>.</exception>
    public void ThrowIfConditionFails(Item? current)
    {
        if (Condition is not null && !Condition.Evaluate(current))
        {
            throw FakeError.ConditionalCheckFailed(OnConditionFailure == ReturnValuesOnConditionCheckFailure.AllOld ? current : null);
        }
    }

    /// <summary>Writes the response of the write: its returns, its capacity and its item collection metrics, as the request asked for them.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="table">The table.</param>
    /// <param name="outcome">What the write did.</param>
    public void Answer(Utf8JsonWriter response, FakeTable table, WriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(table);
        WriteReturns.Write(response, table, ReturnValues, ItemCollectionMetrics, outcome);
        if (ConsumedCapacity != ReturnConsumedCapacity.None)
        {
            var share = CapacityShare.OfWrite(table, outcome, factor: 1);
            WriteCapacity.Write(response, ConsumedCapacity, table.Name, share.TableUnits, share.Indexes);
        }
    }
}
