using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Reads the expressions of a write or of a get in a batch or a transaction, with the <c>ExpressionAttributeNames</c> and <c>ExpressionAttributeValues</c> beside them, through the
/// engine in <see cref="Expressions"/>. Every expression of one object shares one <see cref="PlaceholderScope"/>, which checks once at the end that each placeholder was used.
/// </summary>
internal static class ExpressionMembers
{
    /// <summary>Reads <c>ConditionExpression</c>, and <c>UpdateExpression</c> where the object takes one.</summary>
    /// <param name="value">The request, or an action of a transaction.</param>
    /// <param name="takesUpdate"><see langword="true"/> for <c>UpdateItem</c> and the <c>Update</c> of a transaction.</param>
    /// <returns>The condition and the update, each <see langword="null"/> when the object has none.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects, or maps without an expression.</exception>
    public static (ParsedCondition? Condition, ParsedUpdate? Update) ReadWrite(JsonElement value, bool takesUpdate)
    {
        var condition = Text(value, "ConditionExpression");
        var update = takesUpdate ? Text(value, "UpdateExpression") : null;
        if (condition is null && update is null)
        {
            ItemRequests.ThrowIfMapsWithoutExpression(value);
            return (null, null);
        }

        var scope = Scope(value);
        var parsedUpdate = update is null ? null : Expressions.ParseUpdate(update, scope);
        var parsedCondition = condition is null ? null : Expressions.ParseCondition(condition, scope);
        scope.ThrowIfUnused();
        return (parsedCondition, parsedUpdate);
    }

    /// <summary>Reads <c>ProjectionExpression</c> of a get in a batch or a transaction.</summary>
    /// <param name="value">The object of the get.</param>
    /// <returns>The projection, or <see langword="null"/> when the object has none.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects, or names without an expression.</exception>
    public static ParsedProjection? ReadProjection(JsonElement value)
    {
        if (Text(value, "ProjectionExpression") is not { } projection)
        {
            ItemRequests.ThrowIfMapsWithoutExpression(value);
            return null;
        }

        var scope = Scope(value);
        var parsed = Expressions.ParseProjection(projection, scope);
        scope.ThrowIfUnused();
        return parsed;
    }

    private static string? Text(JsonElement value, string member) =>
        RequestJson.Optional(value, member) is { } text ? RequestJson.ReadString(text, member) : null;

    private static PlaceholderScope Scope(JsonElement value) => new(
        RequestJson.Optional(value, "ExpressionAttributeNames") is { } names ? RequestJson.ReadStringMap(names, "ExpressionAttributeNames") : null,
        RequestJson.Optional(value, "ExpressionAttributeValues") is { } values ? RequestJson.ReadItem(values, "ExpressionAttributeValues") : null);
}
