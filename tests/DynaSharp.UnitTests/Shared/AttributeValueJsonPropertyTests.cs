using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Properties;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests;

/// <summary>
/// <see cref="AttributeValue.FromJson(ReadOnlySpan{byte})"/> over any JSON: it takes exactly what DynamoDB stores, and gives it back through <see cref="AttributeValue.ToJson"/>
/// as the same JSON without the nulls of objects. It refuses, with an <see cref="ArgumentException"/>, a value deeper than 32 levels, an empty or a repeated name, and a
/// number of more than 38 digits or outside the magnitudes DynamoDB has.
/// </summary>
public sealed class AttributeValueJsonPropertyTests
{
    /// <summary>The kind of a JSON value.</summary>
    public enum JsonKind
    {
        /// <summary><c>null</c>.</summary>
        Null,

        /// <summary><c>true</c> or <c>false</c>.</summary>
        Boolean,

        /// <summary>A number, as text.</summary>
        Number,

        /// <summary>A string.</summary>
        Text,

        /// <summary>An array.</summary>
        Array,

        /// <summary>An object, whose names may repeat.</summary>
        Members,
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries), typeof(Documents)])]
    public async Task FromJson_AnyJson_StoresWhatDynamoDbStoresAndRefusesTheRest(JsonInput json)
    {
        // Arrange
        var expected = json.Root.Storable(1) ? json.Root.WithoutNullsInObjects()?.ToJsonString() ?? "null" : nameof(ArgumentException);

        // Act
        var outcome = FromJsonAndBack(json.Root.ToJsonString());

        // Assert
        await Assert.That(outcome).IsEqualTo(expected);
    }

    private static string FromJsonAndBack(string json)
    {
        try
        {
            var value = AttributeValue.FromJson(Encoding.UTF8.GetBytes(json));
            return JsonNode.Parse(value.ToJson().GetRawText())?.ToJsonString() ?? "null";
        }
        catch (ArgumentException)
        {
            return nameof(ArgumentException);
        }
    }

    /// <summary>A JSON value as a tree, so that an object can hold a name twice, which a <see cref="JsonObject"/> cannot.</summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="Text">The text of a number or a string, <c>true</c> or <c>false</c>.</param>
    /// <param name="Members">The elements of an array, or the members of an object.</param>
    public sealed record JsonValue(JsonKind Kind, string Text, (string Name, JsonValue Value)[] Members)
    {
        /// <summary>Determines whether DynamoDB stores the value at this level, counting a string as one level and an object of a string as two.</summary>
        /// <param name="level">The level of this value.</param>
        /// <returns>Whether it is stored.</returns>
        public bool Storable(int level) => Kind switch
        {
            _ when level > JsonBridge.MaxLevels => false,
            JsonKind.Number => FitsByArithmetic(Text),
            JsonKind.Array => Members.All(member => member.Value.Storable(level + 1)),
            JsonKind.Members => Members.All(static member => member.Name.Length > 0)
                && Members.Select(static member => member.Name).Distinct(StringComparer.Ordinal).Count() == Members.Length
                && Members.All(member => member.Value.Kind == JsonKind.Null || member.Value.Storable(level + 1)),
            _ => true,
        };

        /// <summary>Writes the value as JSON text.</summary>
        /// <returns>The text.</returns>
        public string ToJsonString()
        {
            var text = new StringBuilder();
            Write(text);
            return text.ToString();
        }

        /// <summary>Gives the JSON as DynamoDB holds it: without the nulls of objects, the rest as it is.</summary>
        /// <returns>The JSON.</returns>
        public JsonNode? WithoutNullsInObjects() => Kind switch
        {
            JsonKind.Null => null,
            JsonKind.Boolean => System.Text.Json.Nodes.JsonValue.Create(Text == "true"),
            JsonKind.Number => JsonNode.Parse(Text),
            JsonKind.Text => System.Text.Json.Nodes.JsonValue.Create(Text),
            JsonKind.Array => new JsonArray([.. Members.Select(static member => member.Value.WithoutNullsInObjects())]),
            _ => new JsonObject(Members.Where(static member => member.Value.Kind != JsonKind.Null).Select(static member => KeyValuePair.Create(member.Name, member.Value.WithoutNullsInObjects()))),
        };

        /// <inheritdoc/>
        public override string ToString() => ToJsonString();

        // The rule of DynamoDB itself, independent of the code under test: at most 38 significant digits, and the first of them at a power of ten from -130 to 125.
        private static bool FitsByArithmetic(string number)
        {
            var mantissa = number.TrimStart('-').Split('e', 'E')[0];
            var exponent = number.Contains('e', StringComparison.OrdinalIgnoreCase) ? long.Parse(number[(number.IndexOfAny(['e', 'E']) + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
            var point = mantissa.IndexOf('.', StringComparison.Ordinal);
            var units = point < 0 ? mantissa.Length : point;
            var digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);
            var first = digits.AsSpan().IndexOfAnyExcept('0');
            if (first < 0)
            {
                return true;
            }

            var significant = digits.TrimStart('0').TrimEnd('0').Length;
            var power = units - 1 - first + exponent;
            return significant <= 38 && power is >= -130 and <= 125;
        }

        private void Write(StringBuilder text)
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    text.Append("null");
                    break;
                case JsonKind.Boolean or JsonKind.Number:
                    text.Append(Text);
                    break;
                case JsonKind.Text:
                    text.Append(JsonSerializer.Serialize(Text));
                    break;
                case JsonKind.Array:
                    text.Append('[');
                    for (var i = 0; i < Members.Length; i++)
                    {
                        text.Append(i == 0 ? string.Empty : ",");
                        Members[i].Value.Write(text);
                    }

                    text.Append(']');
                    break;
                default:
                    text.Append('{');
                    for (var i = 0; i < Members.Length; i++)
                    {
                        text.Append(i == 0 ? string.Empty : ",").Append(JsonSerializer.Serialize(Members[i].Name)).Append(':');
                        Members[i].Value.Write(text);
                    }

                    text.Append('}');
                    break;
            }
        }
    }

    /// <summary>A JSON document.</summary>
    /// <param name="Root">Its value.</param>
    public sealed record JsonInput(JsonValue Root);

    internal static class Documents
    {
        private static readonly (string Name, JsonValue Value)[] None = [];

        public static Arbitrary<JsonInput> Any() => Gen.Frequency(
            (3, Gen.Sized(static size => Value(Math.Min(size, 30)))),
            (1, from depth in Gen.Choose(28, 34) from leaf in Value(1) from objects in Gen.ArrayOf(Gen.Elements(false, true), depth) select Nest(leaf, objects))).Select(static root => new JsonInput(root)).ToArbitrary();

        // A chain of arrays and objects around a value, to reach the limit of levels from either side.
        private static JsonValue Nest(JsonValue leaf, bool[] objects) =>
            objects.Aggregate(leaf, static (inner, isObject) => isObject ? new JsonValue(JsonKind.Members, string.Empty, [("n", inner)]) : new JsonValue(JsonKind.Array, string.Empty, [(string.Empty, inner)]));

        private static Gen<JsonValue> Value(int size)
        {
            var number = Gen.Frequency(
                (3, Generate.NumberText),
                (1, from negative in Gen.Elements(string.Empty, "-") from digits in Generate.Digits(36, 40) from exponent in Gen.Elements(string.Empty, "e-131", "E+90", "e126", "E-95") select negative + "1" + digits + exponent),
                (1, Gen.Elements("0", "-0", "0.0", "0e999", "1e125", "9.9e125", "1e126", "1e-130", "1e-131", "10e-131", "0.01e-128")));
            var scalar = Gen.OneOf(
                Gen.Constant(new JsonValue(JsonKind.Null, "null", None)),
                Gen.Elements("true", "false").Select(static text => new JsonValue(JsonKind.Boolean, text, None)),
                number.Select(static text => new JsonValue(JsonKind.Number, text, None)),
                Generate.Text.Select(static text => new JsonValue(JsonKind.Text, text, None)));
            if (size <= 1)
            {
                return scalar;
            }

            var inner = Value(size / 3);
            var name = Gen.Frequency((8, Gen.Elements("a", "b", "c", "ü", "a b")), (1, Gen.Constant(string.Empty)));
            return Gen.Frequency(
                (3, scalar),
                (1, Gen.ListOf(inner, 4).Select(static elements => new JsonValue(JsonKind.Array, string.Empty, [.. elements.Select(static element => (string.Empty, element))]))),
                (1, Gen.ListOf(Gen.Zip(name, inner), 4).Select(static members => new JsonValue(JsonKind.Members, string.Empty, [.. members]))));
        }
    }
}
