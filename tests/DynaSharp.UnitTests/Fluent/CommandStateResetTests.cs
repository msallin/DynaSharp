using System.Buffers;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DynaSharp.Fluent;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// A state serves one command after the other, so whatever a command leaves in it reaches the next request. These tests walk the fields instead of naming them:
// a field added to the state or to a part of it fails here until Release clears it, or until Survivors says why it stays.
public sealed class CommandStateResetTests
{
    private const BindingFlags EveryInstanceField = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const string PageWithNextKey = """{"Items":[],"Count":0,"ScannedCount":0,"LastEvaluatedKey":{"pk":{"S":"a"}}}""";

    // The objects a state owns for its whole life, which are walked field by field. A buffer and a JSON writer are compared by what they hold, everything else by value.
    private static readonly HashSet<Type> Parts = [typeof(CommandState), typeof(ExpressionWriter), typeof(ActionList), typeof(KeyInputChanges)];

    // Fields that keep their value on purpose. Everything else must be back at what a new state holds.
    private static readonly Dictionary<string, string> Survivors = new(StringComparer.Ordinal)
    {
        ["CommandState._core"] = "The client the state goes back to, which never changes.",
        ["CommandState._version"] = "It only ever grows, so that a handle from before the release no longer matches.",
        ["CommandState._keyHolder"] = "Capacity: a holder that the next update of that key fills again, and Release takes the key out of it.",
        ["CommandState.<Pool>k__BackingField"] = "The pool of the client, which never changes.",
        ["ExpressionWriter._nameSlices"] = "Capacity: offsets, of which only those below the count are read.",
        ["ExpressionWriter._valueSlices"] = "Capacity: offsets, of which only those below the count are read.",
        ["ActionList._offsets"] = "Capacity: offsets, of which only those below the count are read.",
        ["ActionList._tableIndexes"] = "Capacity: indexes, of which only those below the count are read.",
    };

    // Four is as long as the shortest array that a count goes with, so that counts and arrays agree.
    private static readonly Dictionary<Type, object> Sentinels = new()
    {
        [typeof(bool)] = true,
        [typeof(int)] = 4,
        [typeof(long)] = 4L,
        [typeof(string)] = "left over",
        [typeof(ArraySegment<byte>)] = new ArraySegment<byte>([1]),
        [typeof(Action<DynamoDbWriter, object>)] = new Action<DynamoDbWriter, object>(static (_, _) => { }),
        [typeof(HeldKey)] = new TypedHeldKey<MeterDay.Keys.PrimaryKey>(),
    };

    [Test]
    public async Task Fill_EveryField_DiffersFromANewState()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var fresh = NewState(client);
        var state = WithParts(client.Core.RentCommand());

        // Act
        Fill(state);

        // Assert (a field the walk cannot tell from a new one would pass the test below for the wrong reason)
        await Assert.That(Unchanged(fresh, state)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Release_EveryFieldFilled_LeavesNothingForTheNextCommand()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var fresh = NewState(client);
        var state = WithParts(client.Core.RentCommand());
        Fill(state);

        // Act
        state.Release(state.Version);
        var reused = client.Core.RentCommand();

        // Assert
        await Assert.That((ReferenceEquals(reused, state), LeftOver(fresh, reused))).IsEqualTo((true, string.Empty));
    }

    [Test]
    public async Task Release_HeldKey_TakesTheKeyOutOfTheHolderThatStays()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var state = client.Core.RentCommand();
        state.HoldKey(MeterDay.Keys.Of("m1", DateTimeOffset.UnixEpoch, 1));
        var holder = state.HeldKey!;

        // Act
        state.Release(state.Version);

        // Assert
        await Assert.That((state.HeldKey, holder.ValueOf("Meter"))).IsEqualTo((null, null));
    }

    [Test]
    [Arguments("Put")]
    [Arguments("PutOfAnEntityWithAnOpenItem")]
    [Arguments("Update")]
    [Arguments("Query")]
    [Arguments("BatchWrite")]
    [Arguments("TransactWrite")]
    public async Task Release_RealCommand_LeavesNothingForTheNextCommand(string command)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(PageWithNextKey));
        using var client = TestClient.Create(handler);
        var fresh = NewState(client);

        // Act
        await RunAsync(command, client, client.Table("Shop", "pk"));
        var reused = WithParts(client.Core.RentCommand());

        // Assert
        await Assert.That(LeftOver(fresh, reused)).IsEqualTo(string.Empty);
    }

    private static async Task RunAsync(string command, DynamoDbClient client, Table<Item> shop)
    {
        switch (command)
        {
            case "Put":
                await shop.Put(new Item { ["pk"] = "a" }).If(Attr("n").Eq(1)).ReturnOld().ReturnCapacity().ReturnMetrics().SendAsync();
                break;
            case "PutOfAnEntityWithAnOpenItem":
                _ = shop.Put().With("pk", "a").ToRequestJson();
                break;
            case "Update":
                await shop.Update("a").Set("n", 1).If(Attr("b").Eq(2)).ReturnNew().ReturnOldOnFailure().MaxAttempts(2).SendAsync();
                break;
            case "Query":
                await shop.Query("a").Limit(1).Consistent().Descending().Project("n").PageAsync();
                break;
            case "BatchWrite":
                await shop.BatchWrite().Put(new Item { ["pk"] = "a" }).Delete("b").SendAsync();
                break;
            default:
                await client.TransactWrite().Put(shop, new Item { ["pk"] = "a" }).Delete(shop, "b").SendAsync();
                break;
        }
    }

    private static CommandState NewState(DynamoDbClient client) => WithParts(new CommandState(client.Core, ArrayPool<byte>.Shared));

    // A state makes these parts when it first needs them.
    private static CommandState WithParts(CommandState state)
    {
        _ = state.Actions;
        _ = state.KeyInputs;
        return state;
    }

    private static string LeftOver(CommandState fresh, CommandState reused) => string.Join(", ", Walk(fresh, reused).Where(static field => !field.IsSame).Select(static field => field.Path));

    private static string Unchanged(CommandState fresh, CommandState filled) => string.Join(", ", Walk(fresh, filled).Where(static field => field.IsSame).Select(static field => field.Path));

    private static List<Comparison> Walk(CommandState fresh, CommandState other)
    {
        var fields = new List<Comparison>();
        WalkFields(fresh, other, nameof(CommandState), fields);
        return fields;
    }

    private static void WalkFields(object fresh, object other, string path, List<Comparison> fields)
    {
        foreach (var field in FieldsOf(fresh))
        {
            WalkValue(field.GetValue(fresh), field.GetValue(other), $"{path}.{field.Name}", fields);
        }
    }

    private static void WalkValue(object? fresh, object? other, string path, List<Comparison> fields)
    {
        switch (fresh, other)
        {
            case (PooledByteBuffer freshBuffer, PooledByteBuffer otherBuffer):
                fields.Add(new Comparison(path, freshBuffer.WrittenSpan.SequenceEqual(otherBuffer.WrittenSpan)));
                break;
            case (Utf8JsonWriter freshWriter, Utf8JsonWriter otherWriter):
                fields.Add(new Comparison(path, Position(freshWriter) == Position(otherWriter)));
                break;
            case (IList freshList, IList otherList):
                for (var i = 0; i < Math.Max(freshList.Count, otherList.Count); i++)
                {
                    WalkValue(i < freshList.Count ? freshList[i] : null, i < otherList.Count ? otherList[i] : null, $"{path}[{i}]", fields);
                }

                break;
            case (not null, not null) when Parts.Contains(fresh.GetType()):
                WalkFields(fresh, other, path, fields);
                break;
            default:
                fields.Add(new Comparison(path, Equals(fresh, other)));
                break;
        }
    }

    private static (long Pending, long Committed, int Depth) Position(Utf8JsonWriter writer) => (writer.BytesPending, writer.BytesCommitted, writer.CurrentDepth);

    private static void Fill(object part)
    {
        foreach (var field in FieldsOf(part))
        {
            field.SetValue(part, Filled(field.GetValue(part), field.FieldType));
        }
    }

    private static object? Filled(object? value, Type type)
    {
        switch (value)
        {
            case PooledByteBuffer buffer:
                buffer.Append((byte)'x');
                return buffer;
            case Utf8JsonWriter writer:
                writer.WriteStartObject();
                return writer;
            case Array array:
                for (var i = 0; i < array.Length; i++)
                {
                    array.SetValue(Filled(array.GetValue(i), type.GetElementType()!), i);
                }

                return array;
            case IList list:
                list.Add(RuntimeHelpers.GetUninitializedObject(type.GetGenericArguments()[0]));
                return list;
            case not null when Parts.Contains(value.GetType()):
                Fill(value);
                return value;
            default:
                return Sentinel(Nullable.GetUnderlyingType(type) ?? type);
        }
    }

    // An object nobody looks at will do: Release only ever drops a reference.
    private static object Sentinel(Type type)
    {
        if (Sentinels.TryGetValue(type, out var sentinel))
        {
            return sentinel;
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, 1);
        }

        return type.IsClass && !type.IsAbstract
            ? RuntimeHelpers.GetUninitializedObject(type)
            : throw new InvalidOperationException($"This test cannot fill a field of type {type}. Add a value to Sentinels, or the field to Survivors with the reason it stays.");
    }

    private static IEnumerable<FieldInfo> FieldsOf(object part) =>
        part.GetType().GetFields(EveryInstanceField).Where(static field => !Survivors.ContainsKey($"{field.DeclaringType!.Name}.{field.Name}"));

    private readonly record struct Comparison(string Path, bool IsSame);
}
