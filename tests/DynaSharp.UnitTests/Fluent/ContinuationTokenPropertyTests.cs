using System.Buffers.Text;
using System.Text;
using DynaSharp.UnitTests.Properties;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// A token carries any key there and back, and its text comes from clients, so whatever text a client sends, <see cref="ContinuationToken.TryParse"/> answers and
/// <see cref="ContinuationToken.Parse"/> fails with nothing but a <see cref="FormatException"/>.
/// </summary>
public sealed class ContinuationTokenPropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Tokens)])]
    public async Task ToKey_TokenFromAnyKey_GivesTheKeyBack(Key key)
    {
        // Arrange
        var token = ContinuationToken.FromKey(key.Attributes);

        // Act
        var back = ContinuationToken.Parse(token.ToString()).ToKey();

        // Assert
        await Assert.That(back).IsEqualTo(key.Attributes);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Tokens)])]
    public async Task Parse_AnyTextAClientSends_ReadsATokenOrThrowsFormatException(TokenText text)
    {
        // Arrange (base64url of a version byte and JSON that is close to a key, broken in one of many ways)
        // Act
        var outcome = Outcome(() => ContinuationToken.Parse(text.Text));

        // Assert
        await Assert.That(outcome).IsIn("token", nameof(FormatException));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Tokens)])]
    public async Task TryParse_AnyTextAClientSends_AnswersWithoutThrowing(TokenText text)
    {
        // Arrange
        // Act
        var outcome = Outcome(() => ContinuationToken.TryParse(text.Text, out var token) ? token : throw new FormatException());

        // Assert
        await Assert.That(outcome).IsIn("token", nameof(FormatException));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Which exception escapes is what the property asks.")]
    private static string Outcome(Func<ContinuationToken> parse)
    {
        try
        {
            _ = parse();
            return "token";
        }
        catch (Exception exception)
        {
            return exception.GetType().Name;
        }
    }

    /// <summary>The text a client sends as a token.</summary>
    /// <param name="Text">The text.</param>
    public readonly record struct TokenText(string Text);

    /// <summary>The attributes of a key, of any type and any text.</summary>
    /// <param name="Attributes">The attributes.</param>
    public sealed record Key(Item Attributes)
    {
        /// <inheritdoc/>
        public override string ToString() => Attributes.ToString();
    }

    internal static class Tokens
    {
        // Attribute values that are almost right: what a client that edits a token, or guesses its format, could send.
        private static readonly string[] Values =
        [
            """{"S":"a"}""", """{"N":"1"}""", """{"B":"AQI="}""", """{"BOOL":true}""", """{"NULL":true}""", """{"SS":["a","b"]}""", """{"L":[{"S":"a"}]}""", """{"M":{"a":{"N":"1"}}}""",
            """{"B":"!!"}""", """{"B":"AQI"}""", """{"BS":["!!"]}""", """{"N":"x"}""", """{"N":""}""", """{"N":1}""", """{"S":1}""", """{"S":null}""", """{"BOOL":"x"}""",
            """{"NULL":false}""", """{"SS":[]}""", """{"SS":["a","a"]}""", """{"NS":["1","1.0"]}""", """{"NS":["x"]}""", """{"Q":"x"}""", """{}""", """{"S":"a","N":"1"}""",
            """{"L":{}}""", """{"M":[]}""", """{"M":{"":{"S":"a"}}}""", """{"S":"\ud800"}""", """{"S":"\u0000"}""", "null", "1", "\"a\"", "[]",
            "{\"S\":\"\xff\"}", """{"N":"1E+999999999999"}""", """{"N":"-0"}""", """{"L":[""" + string.Concat(Enumerable.Repeat("""{"L":[""", 70)) + string.Concat(Enumerable.Repeat("]}", 70)) + "]}",
        ];

        private static readonly string[] Names = ["pk", "sk", string.Empty, "a\"b", "\\u0000", "pk"];

        public static Arbitrary<Key> Keys() => (
            from attributes in Gen.NonEmptyListOf(Gen.Zip(Generate.NonEmptyText, Generate.AttributeValue)).Resize(3)
            select new Key(ToItem(attributes))).ToArbitrary();

        public static Arbitrary<TokenText> Texts() => Gen.Frequency(
            (6, from version in Gen.Frequency((6, Gen.Constant((byte)1)), (1, Gen.Elements((byte)0, (byte)2, (byte)123)))
                from json in Json()
                from cut in Gen.Frequency((5, Gen.Constant(0)), (1, Gen.Choose(1, 4)))
                select new TokenText(Encode(version, json, cut))),
            (1, Generate.Text.Select(static text => new TokenText(text)))).ToArbitrary();

        // A later value of a name replaces an earlier one.
        private static Item ToItem(IEnumerable<(string Name, AttributeValue Value)> attributes)
        {
            var item = new Item();
            foreach (var (name, value) in attributes)
            {
                item[name] = value;
            }

            return item;
        }

        private static Gen<string> Json() => Gen.Frequency(
            (5, from entries in Gen.NonEmptyListOf(Gen.Zip(Gen.Elements(Names), Gen.Elements(Values))).Resize(3)
                select "{" + string.Join(',', entries.Select(static entry => $"\"{entry.Item1}\":{entry.Item2}")) + "}"),
            (1, from json in Gen.Elements(Values) from trailer in Gen.Elements(string.Empty, " ", "}", ",\"TableName\":\"x\"}", "{}") select json + trailer),
            (1, Gen.Elements(string.Empty, "{", "{\"pk\":", "{\"pk\":{\"S\":\"a\"}", "{\"pk\":{\"S\":\"a\"}}{}", "[{\"pk\":{\"S\":\"a\"}}]")));

        private static string Encode(byte version, string json, int cut)
        {
            byte[] bytes = [version, .. Encoding.Latin1.GetBytes(json)];
            return Base64Url.EncodeToString(bytes.AsSpan(0, Math.Max(0, bytes.Length - cut)));
        }
    }
}
