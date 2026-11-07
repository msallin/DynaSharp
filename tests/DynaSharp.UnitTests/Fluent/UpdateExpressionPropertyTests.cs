using System.Buffers;
using System.Text.Json;
using DynaSharp.Fluent;
using DynaSharp.Serialization;
using DynaSharp.Testing.Fake;
using DynaSharp.UnitTests.Properties;
using DynaSharp.UnitTests.Serialization;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// Random updates as a request writes them, over attribute names that need escaping or look like placeholders and words of the grammar, applied by the engine of the fake to
/// a random item, against a model of what each action does. The actions the writer adds on its own, <see cref="ExpressionWriter.SetMissingAttribute"/> for a version or an
/// index key, come last and give way to an action of the update on the same attribute, because DynamoDB rejects two actions on one path.
/// </summary>
public sealed class UpdateExpressionPropertyTests
{
    // Plain names, names that look like a placeholder or a keyword of an update, and names that only a placeholder can carry.
    private static readonly string[] Names = ["status", "total", "#n0", "#n1", ":v0", "SET", "REMOVE", "if_not_exists", "a b", "x\"y", "ü"];

    /// <summary>What an action does.</summary>
    public enum ActionKind
    {
        /// <summary><c>SET a = :v</c>.</summary>
        Set,

        /// <summary><c>SET a = if_not_exists(a, :v)</c>.</summary>
        SetIfNotExists,

        /// <summary><c>ADD a :v</c>, on a number.</summary>
        Add,

        /// <summary><c>REMOVE a</c>.</summary>
        Remove,
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.FewerCases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Updates)])]
    public async Task WriteMembers_AnyUpdate_ChangesTheItemAsEachActionSays(Update update)
    {
        // Arrange
        var expected = update.Model();

        // Act
        var request = Write(update);
        var scope = new PlaceholderScope(request.Names, request.Values);
        var parsed = DynaSharp.Testing.Fake.Expressions.ParseUpdate(request.Expression, scope);
        scope.ThrowIfUnused();
        var image = parsed.Apply(update.Start(), new Item { ["pk"] = "k" }).NewImage;

        // Assert
        await Assert.That(image).IsEqualTo(expected);
    }

    private static (string Expression, Dictionary<string, string> Names, Dictionary<string, AttributeValue>? Values) Write(Update update)
    {
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        foreach (var action in update.Actions)
        {
            switch (action.Kind)
            {
                case ActionKind.Set:
                    writer.Set<int, NumberConverter<int>>(Names[action.Name], action.Value);
                    break;
                case ActionKind.SetIfNotExists:
                    writer.SetIfNotExists<int, NumberConverter<int>>(Names[action.Name], action.Value);
                    break;
                case ActionKind.Add:
                    writer.Add<int, NumberConverter<int>>(Names[action.Name], action.Value);
                    break;
                default:
                    writer.Remove(Names[action.Name]);
                    break;
            }
        }

        foreach (var missing in update.Missing)
        {
            writer.SetMissingAttribute<int, NumberConverter<int>>(Names[missing.Name], missing.Value);
        }

        using var body = new PooledByteBuffer();
        body.Append("{\"TableName\":\"t\""u8);
        writer.WriteMembers(body);
        body.Append("}"u8);
        writer.Release();
        using var document = JsonDocument.Parse(body.WrittenSegment.AsMemory());
        var root = document.RootElement;
        return (
            root.GetProperty("UpdateExpression").GetString()!,
            root.GetProperty("ExpressionAttributeNames").EnumerateObject().ToDictionary(static name => name.Name, static name => name.Value.GetString()!, StringComparer.Ordinal),
            root.TryGetProperty("ExpressionAttributeValues", out var values)
                ? values.EnumerateObject().ToDictionary(static value => value.Name, static value => TestJson.ReadValue(value.Value.GetRawText()), StringComparer.Ordinal)
                : null);
    }

    /// <summary>One action on one attribute.</summary>
    /// <param name="Kind">What it does.</param>
    /// <param name="Name">The index of the name.</param>
    /// <param name="Value">The number it sets or adds.</param>
    public readonly record struct UpdateAction(ActionKind Kind, int Name, int Value)
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Kind} [{Names[Name]}] {Value}";
    }

    /// <summary>An update: actions on distinct attributes, then the attributes the writer sets if missing, and the item it applies to.</summary>
    /// <param name="Actions">The actions, at most one per attribute.</param>
    /// <param name="Missing">The attributes set if missing; the value is unused for anything else.</param>
    /// <param name="Attributes">The number of each name in the item, or a negative one for a name it lacks.</param>
    public sealed record Update(UpdateAction[] Actions, UpdateAction[] Missing, int[] Attributes)
    {
        /// <summary>Builds the item the update applies to.</summary>
        /// <returns>The item.</returns>
        public Item Start()
        {
            var item = new Item { ["pk"] = "k" };
            for (var i = 0; i < Names.Length; i++)
            {
                if (Attributes[i] >= 0)
                {
                    item[Names[i]] = AttributeValue.FromNumber(Attributes[i]);
                }
            }

            return item;
        }

        /// <summary>Applies the update as each action is defined, one after another.</summary>
        /// <returns>The item after the update.</returns>
        public Item Model()
        {
            var item = Start();
            var touched = new HashSet<int>();
            foreach (var action in Actions)
            {
                var name = Names[action.Name];
                var found = item.TryGetValue(name, out var current);
                touched.Add(action.Name);
                switch (action.Kind)
                {
                    case ActionKind.Set:
                        item[name] = AttributeValue.FromNumber(action.Value);
                        break;
                    case ActionKind.SetIfNotExists:
                        item[name] = found ? current : AttributeValue.FromNumber(action.Value);
                        break;
                    case ActionKind.Add:
                        item[name] = AttributeValue.FromNumber((found ? current.AsNumber<int>() : 0) + action.Value);
                        break;
                    default:
                        item.Remove(name);
                        break;
                }
            }

            foreach (var missing in Missing)
            {
                if (touched.Add(missing.Name) && !item.ContainsKey(Names[missing.Name]))
                {
                    item[Names[missing.Name]] = AttributeValue.FromNumber(missing.Value);
                }
            }

            return item;
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"{string.Join(", ", Actions.AsEnumerable())}; if missing {string.Join(", ", Missing.Select(static missing => Names[missing.Name]))}; on [{string.Join(", ", Attributes)}]";
    }

    internal static class Updates
    {
        public static Arbitrary<Update> Any() => (
            from order in Gen.Shuffle(Enumerable.Range(0, Names.Length))
            from count in Gen.Choose(0, 6)
            from kinds in Gen.ArrayOf(Gen.Elements(ActionKind.Set, ActionKind.SetIfNotExists, ActionKind.Add, ActionKind.Remove), count)
            from values in Gen.ArrayOf(Gen.Choose(0, 9), Names.Length)
            from missing in Gen.ListOf(Gen.Choose(0, Names.Length - 1), 3)
            from attributes in Gen.ArrayOf(Gen.Choose(-1, 9), Names.Length)
            let actions = order.Take(count).Select((name, i) => new UpdateAction(kinds[i], name, values[i])).ToArray()
            where actions.Length > 0 || missing.Count > 0
            select new Update(actions, [.. missing.Select(name => new UpdateAction(ActionKind.Set, name, values[name]))], attributes)).ToArbitrary();
    }
}
