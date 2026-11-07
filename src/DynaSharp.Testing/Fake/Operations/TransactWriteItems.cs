using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>TransactWriteItems</c>: up to 100 condition checks, puts, deletes and updates over several tables, all or nothing. Every condition is checked against the items as they are
/// before any write; if one fails, the transaction is cancelled with one reason per action in its order, <c>ConditionalCheckFailed</c> where a condition failed, with the item when
/// the action asked for it with <c>ALL_OLD</c>, <c>ValidationError</c> for a key that does not match the schema of its table, a put of an index key of another type than the index
/// defines (whatever its condition) and an update whose result has one (once its condition holds), and <c>None</c> for the others. A write the table rejects once every action passed,
/// such as an update that makes an item over 400 KB, cancels it with <c>ValidationError</c> and takes back the writes before it. One item twice is a <c>ValidationException</c>, and so is
/// a put of an item over 400 KB, and an empty string or binary as a value of a key: of the key of an action, or of an index key that a put or an update writes, and an index key that is too large.
/// </summary>
/// <remarks>
/// <para>
/// <c>ClientRequestToken</c> makes the call idempotent for 10 minutes on the clock of the fake: the same token with the same request again changes nothing and answers success, the same
/// token with another request is <c>IdempotentParameterMismatchException</c>. A replay reports, as the API reference says, the read units of reading the items, not the write units of the first call:
/// twice a strongly consistent read of each item, as a transactional read costs.
/// </para>
/// <para>
/// A transaction writes every item twice, to prepare and to commit, so each action costs twice the units of its write, a condition check twice a write of the item it reads.
/// <c>INDEXES</c> repeats the units as <c>WriteCapacityUnits</c>, and on a replay as <c>ReadCapacityUnits</c>, as DynamoDB answers it.
/// </para>
/// <para>
/// An update is tried on its item before the transaction is decided, so that the keys of the indexes it writes count for the reasons, as both emulators answer it. Any other error of an update,
/// such as a path into a missing map, comes to light only when the actions are applied; if the condition of another action fails, its reason is <c>None</c>. DynamoDB Local answers a
/// <c>ValidationException</c> for it then and Dynoxide a <c>ValidationError</c>, so which DynamoDB does is open.
/// </para>
/// </remarks>
internal static class TransactWriteItems
{
    /// <summary>The operation.</summary>
    public const string Name = "TransactWriteItems";

    private const int MaxTokenLength = 36;

    private enum ActionKind
    {
        ConditionCheck,
        Put,
        Delete,
        Update,
    }

    private static CancellationReason None { get; } = new() { Code = "None" };

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, Name, "TransactItems", "ReturnConsumedCapacity", "ReturnItemCollectionMetrics", "ClientRequestToken");
        var elements = RequestJson.ReadArray(RequestJson.Required(request, "TransactItems"), "TransactItems");
        Batches.ThrowIfLength(elements.Count, "transactItems", TransactGetItems.MaxActions);
        var capacity = SingleWrite.Enum(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities);
        var metrics = SingleWrite.Enum(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues);
        var token = ReadToken(request);
        var now = db.Clock.GetUtcNow();
        var replay = token is not null && db.Tokens.IsReplay(token, request, now);

        var actions = new List<Action>(elements.Count);
        var seen = new HashSet<(string, ItemKey)>();
        foreach (var element in elements)
        {
            var action = Read(db, element);
            if (action.Rejected is null)
            {
                Batches.ThrowIfSeen(seen, action.Table, action.Key, TransactGetItems.SameItemTwice);
            }

            actions.Add(action);
        }

        if (replay)
        {
            if (capacity != ReturnConsumedCapacity.None)
            {
                WriteCapacity.WriteAll(
                    response,
                    capacity,
                    actions.Select(static action => new CapacityShare(action.Table.Name, 2 * ReadCapacity.Units(SizeOf(action.Table.Get(action.Key)), consistent: true), [])),
                    WriteCapacity.Split.Read);
            }

            WriteReturns.WriteAll(response, metrics, actions.Where(static action => action.Kind != ActionKind.ConditionCheck).Select(static action => (action.Table, new WriteOutcome(action.Key, null, null))));
            return;
        }

        ThrowIfAnyConditionFails(actions);
        var outcomes = Apply(actions);
        if (capacity != ReturnConsumedCapacity.None)
        {
            WriteCapacity.WriteAll(response, capacity, actions.Zip(outcomes, static (action, outcome) => Share(action, outcome)), WriteCapacity.Split.Write);
        }

        WriteReturns.WriteAll(
            response,
            metrics,
            actions.Zip(outcomes).Where(static write => write.First.Kind != ActionKind.ConditionCheck).Select(static write => (write.First.Table, write.Second)));
        if (token is not null)
        {
            db.Tokens.Remember(token, request, now);
        }
    }

    private static string? ReadToken(JsonElement request)
    {
        if (RequestJson.Optional(request, "ClientRequestToken") is not { } value)
        {
            return null;
        }

        var token = RequestJson.ReadString(value, "ClientRequestToken");
        if (token.Length is < 1 or > MaxTokenLength)
        {
            throw FakeError.Validation(
                $"1 validation error detected: Value '{token}' at 'clientRequestToken' failed to satisfy constraint: Member must have length {(token.Length == 0 ? "greater than or equal to 1" : "less than or equal to " + MaxTokenLength)}");
        }

        return token;
    }

    // {"Put":{"TableName":"T","Item":{...},"ConditionExpression":"...","ExpressionAttributeNames":{...},"ExpressionAttributeValues":{...},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}}
    private static Action Read(FakeDatabase db, JsonElement element)
    {
        var (name, body) = Batches.OneOf(
            element,
            "TransactItems of " + Name,
            "TransactItems can only contain one of Check, Put, Update or Delete",
            "ConditionCheck",
            "Put",
            "Delete",
            "Update");
        var kind = name switch
        {
            "Put" => ActionKind.Put,
            "Delete" => ActionKind.Delete,
            "Update" => ActionKind.Update,
            _ => ActionKind.ConditionCheck,
        };
        var where = name + " of " + Name;
        string[] members = kind switch
        {
            ActionKind.Put => ["TableName", "Item", "ConditionExpression", "ExpressionAttributeNames", "ExpressionAttributeValues", "ReturnValuesOnConditionCheckFailure"],
            ActionKind.Update => ["TableName", "Key", "UpdateExpression", "ConditionExpression", "ExpressionAttributeNames", "ExpressionAttributeValues", "ReturnValuesOnConditionCheckFailure"],
            _ => ["TableName", "Key", "ConditionExpression", "ExpressionAttributeNames", "ExpressionAttributeValues", "ReturnValuesOnConditionCheckFailure"],
        };
        RequestJson.Members(body, where, members);
        var table = db.GetTable(TableOperations.ReadTableName(body));
        var item = kind == ActionKind.Put ? RequestJson.ReadItem(RequestJson.Required(body, "Item"), "Item") : null;
        var keyMap = item is null ? RequestJson.ReadItem(RequestJson.Required(body, "Key"), "Key") : null;
        ItemKey key = default;
        FakeError? rejected = null;
        try
        {
            key = item is not null ? table.KeySchema.KeyOf(table.Check(item, ofRequest: true)) : table.KeySchema.ReadKey(keyMap!);
        }
        catch (FakeError error) when (!error.IsInputError)
        {
            // DynamoDB Local cancels the transaction with a ValidationError reason for a key that does not match the schema, see Vectors/writes.json, and both emulators
            // do so for an index key of another type than the index defines.
            rejected = error;
        }

        if (kind == ActionKind.ConditionCheck)
        {
            _ = RequestJson.Required(body, "ConditionExpression");
        }
        else if (kind == ActionKind.Update)
        {
            _ = RequestJson.Required(body, "UpdateExpression");
        }

        var (condition, update) = ExpressionMembers.ReadWrite(body, kind == ActionKind.Update);

        // An update of a key attribute is wrong whatever the item holds, so it fails the request before the transaction, not as a reason of it.
        if (update?.Actions.FirstOrDefault(action => table.KeySchema.Names.Contains(action.Path.Root)) is { } keyUpdate)
        {
            throw FakeError.Validation($"One or more parameter values were invalid: Cannot update attribute {keyUpdate.Path.Root}. This attribute is part of the key");
        }

        var onFailure = SingleWrite.Enum(body, "ReturnValuesOnConditionCheckFailure", WireNames.ReturnValuesOnConditionCheckFailures);
        return new Action(kind, table, key, item, condition, update, onFailure, rejected);
    }

    private static void ThrowIfAnyConditionFails(List<Action> actions)
    {
        var reasons = new CancellationReason[actions.Count];
        var cancelled = false;
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.Rejected is { } error)
            {
                reasons[i] = ValidationError(error);
                cancelled = true;
                continue;
            }

            var current = action.Table.Get(action.Key);
            if (action.Condition is not null && !action.Condition.Evaluate(current))
            {
                reasons[i] = new CancellationReason
                {
                    Code = "ConditionalCheckFailed",
                    Message = "The conditional request failed",
                    Item = action.OnFailure == ReturnValuesOnConditionCheckFailure.AllOld ? current : null,
                };
                cancelled = true;
                continue;
            }

            if (action.Kind == ActionKind.Update && IndexKeyRejected(action, current) is { } indexError)
            {
                reasons[i] = ValidationError(indexError);
                cancelled = true;
                continue;
            }

            reasons[i] = None;
        }

        if (cancelled)
        {
            throw FakeError.TransactionCanceled(reasons);
        }
    }

    // The update tried on its item, for the keys of the indexes its result holds: one of another type is the reason of the action, an empty one fails the request. Every other error
    // of the update waits for the apply, see the remarks of the class.
    private static FakeError? IndexKeyRejected(Action action, Item? current)
    {
        Item image;
        try
        {
            image = action.Update!.Apply(current, action.Table.KeySchema.ToItem(action.Key)).NewImage;
        }
        catch (FakeError)
        {
            return null;
        }

        try
        {
            foreach (var index in action.Table.GlobalIndexes.Concat(action.Table.LocalIndexes))
            {
                index.KeySchema.ThrowIfInvalidIndexKey(image, index.Name);
            }

            return null;
        }
        catch (FakeError error) when (!error.IsInputError)
        {
            return error;
        }
    }

    private static List<WriteOutcome> Apply(List<Action> actions)
    {
        var journal = new WriteJournal();
        var outcomes = new List<WriteOutcome>(actions.Count);
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            WriteOutcome outcome;
            try
            {
                outcome = action.Kind switch
                {
                    ActionKind.ConditionCheck => new WriteOutcome(action.Key, action.Table.Get(action.Key), null),
                    ActionKind.Put => new WriteOutcome(action.Key, action.Table.Put(action.Item!), null),
                    ActionKind.Delete => new WriteOutcome(action.Key, action.Table.Remove(action.Key), null),
                    _ => UpdateItem.Apply(action.Table, action.Key, action.Table.Get(action.Key), action.Update),
                };
            }
            catch (FakeError error) when (error.Code == "ValidationException")
            {
                // An error of the input, such as an empty index key, failed the request before: Read checks the puts and IndexKeyRejected the updates.
                journal.Undo();
                var reasons = Enumerable.Repeat(None, actions.Count).ToArray();
                reasons[i] = ValidationError(error);
                throw FakeError.TransactionCanceled(reasons);
            }

            if (action.Kind != ActionKind.ConditionCheck)
            {
                journal.Record(action.Table, action.Key, outcome.OldImage);
                outcome = outcome with { NewImage = action.Table.Get(action.Key) };
            }

            outcomes.Add(outcome);
        }

        return outcomes;
    }

    private static CapacityShare Share(Action action, WriteOutcome outcome)
    {
        if (action.Kind != ActionKind.ConditionCheck)
        {
            return CapacityShare.OfWrite(action.Table, outcome, factor: 2);
        }

        var size = SizeOf(outcome.OldImage);
        return new CapacityShare(action.Table.Name, 2 * WriteCapacity.Units(size, size), []);
    }

    private static long SizeOf(Item? item) => item is null ? 0 : ItemSize.Of(item);

    private static CancellationReason ValidationError(FakeError error) => new() { Code = "ValidationError", Message = error.Message };

    private sealed record Action(
        ActionKind Kind,
        FakeTable Table,
        ItemKey Key,
        Item? Item,
        ParsedCondition? Condition,
        ParsedUpdate? Update,
        ReturnValuesOnConditionCheckFailure OnFailure,
        FakeError? Rejected);
}
