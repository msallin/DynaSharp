using System.Globalization;
using System.Numerics;
using System.Text;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests;

public sealed class AttributeValueTests
{
    // Pairs built independently from equal inputs; for sets and maps in a different order.
    public static IEnumerable<Func<(AttributeValue Left, AttributeValue Right)>> EqualPairs()
    {
        yield return () => (default, default);
        yield return () => (AttributeValue.Null, AttributeValue.Null);
        yield return () => ("abc", AttributeValue.FromString(new string(['a', 'b', 'c'])));
        yield return () => (AttributeValue.FromNumber("1.50"), AttributeValue.FromNumber(1.50m));
        yield return () => (AttributeValue.FromBinary([1, 2]), AttributeValue.FromBinary([1, 2]));
        yield return () => (AttributeValue.FromBool(true), AttributeValue.FromBool(true));
        yield return () => (AttributeValue.FromBool(false), AttributeValue.FromBool(false));
        yield return () => (AttributeValue.FromList([]), AttributeValue.FromList([]));
        yield return () => (AttributeValue.FromList(["a", AttributeValue.FromList(["b"])]), AttributeValue.FromList(["a", AttributeValue.FromList(["b"])]));
        yield return () => (AttributeValue.FromMap([new("a", "1"), new("b", "2")]), AttributeValue.FromMap([new("b", "2"), new("a", "1")]));
        yield return () => (AttributeValue.FromStringSet(["a", "b", "c"]), AttributeValue.FromStringSet(["c", "a", "b"]));
        yield return () => (AttributeValue.FromNumberSet(["1", "2"]), AttributeValue.FromNumberSet(["2", "1"]));
        yield return () => (AttributeValue.FromBinarySet([[1], [2, 3]]), AttributeValue.FromBinarySet([[2, 3], [1]]));
    }

    public static IEnumerable<Func<(AttributeValue Left, AttributeValue Right)>> UnequalPairs()
    {
        yield return () => (default, AttributeValue.Null);
        yield return () => ("1", AttributeValue.FromNumber("1"));
        yield return () => ("a", "b");
        yield return () => (AttributeValue.FromNumber("1"), AttributeValue.FromNumber("1.0"));
        yield return () => (AttributeValue.FromBinary([1, 2]), AttributeValue.FromBinary([1, 3]));
        yield return () => (AttributeValue.FromBinary([1]), AttributeValue.FromBinary([1, 1]));
        yield return () => (AttributeValue.FromBool(true), AttributeValue.FromBool(false));
        yield return () => (AttributeValue.FromList(["a", "b"]), AttributeValue.FromList(["b", "a"]));
        yield return () => (AttributeValue.FromList(["a"]), AttributeValue.FromList(["a", "a"]));
        yield return () => (AttributeValue.FromMap([new("a", "1")]), AttributeValue.FromMap([new("a", "2")]));
        yield return () => (AttributeValue.FromMap([new("a", "1")]), AttributeValue.FromMap([new("b", "1")]));
        yield return () => (AttributeValue.FromMap([new("a", "1")]), AttributeValue.FromMap([new("a", "1"), new("b", "1")]));
        yield return () => (AttributeValue.FromStringSet(["a", "b"]), AttributeValue.FromStringSet(["a", "c"]));
        yield return () => (AttributeValue.FromStringSet(["a"]), AttributeValue.FromStringSet(["a", "b"]));
        yield return () => (AttributeValue.FromNumberSet(["1"]), AttributeValue.FromNumberSet(["1.0"]));
        yield return () => (AttributeValue.FromStringSet(["1"]), AttributeValue.FromNumberSet(["1"]));
        yield return () => (AttributeValue.FromBinarySet([[1], [2]]), AttributeValue.FromBinarySet([[1], [3]]));
        yield return () => (AttributeValue.FromBinarySet([[1]]), AttributeValue.FromBinarySet([[1], [2]]));
    }

    public static IEnumerable<Func<(AttributeValue Value, string Json)>> JsonSamples()
    {
        yield return () => ("Hello", """{"S":"Hello"}""");
        yield return () => (AttributeValue.FromNumber(42), """{"N":"42"}""");
        yield return () => (AttributeValue.FromBinary("Sunny"u8), """{"B":"U3Vubnk="}""");
        yield return () => (AttributeValue.FromBool(false), """{"BOOL":false}""");
        yield return () => (AttributeValue.Null, """{"NULL":true}""");
        yield return () => (AttributeValue.FromList(["a", AttributeValue.FromNumber(1)]), """{"L":[{"S":"a"},{"N":"1"}]}""");
        yield return () => (AttributeValue.FromMap([new("Name", "Joe")]), """{"M":{"Name":{"S":"Joe"}}}""");
        yield return () => (AttributeValue.FromStringSet(["a", "é<"]), """{"SS":["a","é<"]}""");
        yield return () => (AttributeValue.FromNumberSet(["1", "-2.5"]), """{"NS":["1","-2.5"]}""");
        yield return () => (AttributeValue.FromBinarySet([[1]]), """{"BS":["AQ=="]}""");
    }

    // Each accessor applied to a value of another type.
    public static IEnumerable<Func<(Func<object> Access, string Message)>> MismatchedAccessors()
    {
        yield return () => (() => AttributeValue.FromNumber(1).AsString(), "type Number, not String");
        yield return () => (() => AttributeValue.FromString("1").AsNumber(), "type String, not Number");
        yield return () => (() => AttributeValue.FromString("1").AsNumber<int>(), "type String, not Number");
        yield return () => (() => AttributeValue.FromString("1").AsBinary(), "type String, not Binary");
        yield return () => (() => AttributeValue.Null.AsBool(), "type Null, not Boolean");
        yield return () => (() => AttributeValue.FromMap([]).AsList(), "type Map, not List");
        yield return () => (() => AttributeValue.FromList([]).AsMap(), "type List, not Map");
        yield return () => (() => AttributeValue.FromNumberSet(["1"]).AsStringSet(), "type NumberSet, not StringSet");
        yield return () => (() => AttributeValue.FromStringSet(["1"]).AsNumberSet(), "type StringSet, not NumberSet");
        yield return () => (() => default(AttributeValue).AsBinarySet(), "type None, not BinarySet");
    }

    public static IEnumerable<Func<ValuePair>> EqualCases() => AsCases(EqualPairs());

    public static IEnumerable<Func<ValuePair>> UnequalCases() => AsCases(UnequalPairs());

    public static IEnumerable<Func<ValueSample>> JsonCases() => JsonSamples().Select(static sample => new Func<ValueSample>(() =>
    {
        var (value, json) = sample();
        return new ValueSample(json, value);
    }));

    [Test]
    public async Task Null_Always_HasTypeNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = AttributeValue.Null;

        // Assert
        await Assert.That(value.Type).IsEqualTo(AttributeType.Null);
    }

    [Test]
    public async Task IsNull_NullValue_ReturnsTrue()
    {
        // Arrange
        var value = AttributeValue.Null;

        // Act
        var isNull = value.IsNull;

        // Assert
        await Assert.That(isNull).IsTrue();
    }

    [Test]
    public async Task IsNull_DefaultValue_ReturnsFalse()
    {
        // Arrange
        var value = default(AttributeValue);

        // Act
        var isNull = value.IsNull;

        // Assert
        await Assert.That(isNull).IsFalse();
    }

    [Test]
    public async Task Type_DefaultValue_ReturnsNone()
    {
        // Arrange
        var value = default(AttributeValue);

        // Act
        var type = value.Type;

        // Assert
        await Assert.That(type).IsEqualTo(AttributeType.None);
    }

    [Test]
    public async Task ImplicitConversion_String_CreatesStringValue()
    {
        // Arrange
        const string text = "Hello";

        // Act
        AttributeValue value = text;

        // Assert
        await Assert.That(value).IsEqualTo(AttributeValue.FromString("Hello"));
    }

    [Test]
    [Arguments(int.MinValue, "-2147483648")]
    [Arguments(-1, "-1")]
    [Arguments(0, "0")]
    [Arguments(1, "1")]
    [Arguments(42, "42")]
    [Arguments(int.MaxValue, "2147483647")]
    public async Task ImplicitConversion_Int_CreatesNumberValue(int number, string expected)
    {
        // Arrange (nothing to arrange)

        // Act
        AttributeValue value = number;

        // Assert
        await Assert.That((value.Type, value.AsNumber())).IsEqualTo((AttributeType.Number, expected));
    }

    [Test]
    [Arguments(long.MinValue, "-9223372036854775808")]
    [Arguments(-1L, "-1")]
    [Arguments(0L, "0")]
    [Arguments(1L, "1")]
    [Arguments(1_700_000_000_000L, "1700000000000")]
    [Arguments(long.MaxValue, "9223372036854775807")]
    public async Task ImplicitConversion_Long_CreatesNumberValue(long number, string expected)
    {
        // Arrange (nothing to arrange)

        // Act
        AttributeValue value = number;

        // Assert
        await Assert.That((value.Type, value.AsNumber())).IsEqualTo((AttributeType.Number, expected));
    }

    [Test]
    public async Task ImplicitConversion_Decimal_KeepsTheScaleInTheText()
    {
        // Arrange
        const decimal price = 19.90m;

        // Act
        AttributeValue value = price;

        // Assert
        await Assert.That((value.Type, value.AsNumber())).IsEqualTo((AttributeType.Number, "19.90"));
    }

    [Test]
    public async Task ImplicitConversion_DecimalExtremes_KeepEveryDigit()
    {
        // Arrange (a decimal has 28 or 29 significant digits, which a double would round)
        const decimal largest = decimal.MaxValue;
        const decimal smallest = decimal.MinValue;

        // Act
        AttributeValue max = largest;
        AttributeValue min = smallest;

        // Assert
        await Assert.That((max.AsNumber(), min.AsNumber())).IsEqualTo(("79228162514264337593543950335", "-79228162514264337593543950335"));
    }

    [Test]
    public async Task ImplicitConversion_Double_CreatesTheShortestRoundTripText()
    {
        // Arrange
        const double ratio = 0.1;

        // Act
        AttributeValue value = ratio;

        // Assert
        await Assert.That((value.Type, value.AsNumber())).IsEqualTo((AttributeType.Number, "0.1"));
    }

    [Test]
    public async Task ImplicitConversion_DoubleWithExponent_CreatesTextThatDynamoDbAccepts()
    {
        // Arrange
        const double large = 1e21;

        // Act
        AttributeValue value = large;

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("1E+21");
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    public async Task ImplicitConversion_DoubleNotFinite_ThrowsArgumentOutOfRangeException(double number)
    {
        // Arrange (DynamoDB cannot store NaN or infinity)

        // Act
        var act = () =>
        {
            AttributeValue value = number;
            return value;
        };

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ImplicitConversion_Bool_CreatesBooleanValue(bool flag)
    {
        // Arrange (nothing to arrange)

        // Act
        AttributeValue value = flag;

        // Assert
        await Assert.That((value.Type, value.AsBool())).IsEqualTo((AttributeType.Boolean, flag));
    }

    [Test]
    public async Task ImplicitConversion_Bytes_CreatesBinaryValue()
    {
        // Arrange
        byte[] bytes = [0, 1, 254, 255];

        // Act
        AttributeValue value = bytes;

        // Assert
        await Assert.That(value.AsBinary().ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task ImplicitConversion_EmptyBytes_CreatesAnEmptyBinaryValue()
    {
        // Arrange
        byte[] bytes = [];

        // Act
        AttributeValue value = bytes;

        // Assert
        await Assert.That((value.Type, value.AsBinary().Length)).IsEqualTo((AttributeType.Binary, 0));
    }

    [Test]
    public async Task ImplicitConversion_Bytes_CopiesThem()
    {
        // Arrange
        byte[] bytes = [1, 2, 3];
        AttributeValue value = bytes;

        // Act
        bytes[0] = 99;

        // Assert
        await Assert.That(value.AsBinary().ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    public async Task ImplicitConversion_NullBytes_ThrowsArgumentNullException()
    {
        // Arrange
        byte[]? bytes = null;

        // Act
        var act = () =>
        {
            AttributeValue value = bytes!;
            return value;
        };

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task ImplicitConversion_ItemInitializer_BuildsAnItemFromPlainValues()
    {
        // Arrange (the use case: an item written without a factory call per attribute)

        // Act
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = "CUSTOMER#alice",
            ["orders"] = 7,
            ["total"] = 19.90m,
            ["active"] = true,
        };

        // Assert
        await Assert.That((item["pk"].AsString(), item["orders"].AsNumber<int>(), item["total"].AsNumber<decimal>(), item["active"].AsBool())).IsEqualTo(("CUSTOMER#alice", 7, 19.90m, true));
    }

    [Test]
    public async Task FromString_Value_AsStringReturnsIt()
    {
        // Arrange
        const string text = "Hello";

        // Act
        var value = AttributeValue.FromString(text);

        // Assert
        await Assert.That(value.AsString()).IsEqualTo("Hello");
    }

    [Test]
    public async Task FromString_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string? text = null;

        // Act
        var act = () => AttributeValue.FromString(text!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task FromNumber_Text_AsNumberReturnsItUnchanged()
    {
        // Arrange
        const string text = "-1.50E-10";

        // Act
        var value = AttributeValue.FromNumber(text);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("-1.50E-10");
    }

    [Test]
    public async Task FromNumber_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        string? text = null;

        // Act
        var act = () => AttributeValue.FromNumber(text!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task FromNumber_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var text = string.Empty;

        // Act
        var act = () => AttributeValue.FromNumber(text);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("value");
    }

    [Test]
    public async Task FromNumber_Decimal_KeepsTrailingZeros()
    {
        // Arrange
        const decimal number = 1.50m;

        // Act
        var value = AttributeValue.FromNumber(number);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("1.50");
    }

    [Test]
    public async Task FromNumber_Double_UsesShortestRoundTripText()
    {
        // Arrange
        const double number = 0.1;

        // Act
        var value = AttributeValue.FromNumber(number);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("0.1");
    }

    [Test]
    public async Task FromNumber_BigInteger_KeepsAllDigits()
    {
        // Arrange
        var number = BigInteger.Pow(10, 37) - 1;

        // Act
        var value = AttributeValue.FromNumber(number);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo(new string('9', 37));
    }

    [Test]
    public async Task FromNumber_CurrentCultureUsesDecimalComma_UsesDecimalPoint()
    {
        // Arrange
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-CH");
        AttributeValue value;

        // Act
        try
        {
            value = AttributeValue.FromNumber(1234.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("1234.5");
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    public async Task FromNumber_NotFinite_ThrowsArgumentOutOfRangeException(double number)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => AttributeValue.FromNumber(number);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Test]
    [Arguments("42", 42)]
    [Arguments("-19", -19)]
    [Arguments("1E+2", 100)]
    [Arguments("2.0", 2)]
    public async Task AsNumber_IntegerText_ParsesWithInvariantCulture(string text, int expected)
    {
        // Arrange
        var value = AttributeValue.FromNumber(text);

        // Act
        var number = value.AsNumber<int>();

        // Assert
        await Assert.That(number).IsEqualTo(expected);
    }

    [Test]
    public async Task AsNumber_DecimalText_ParsesAllDigits()
    {
        // Arrange
        var value = AttributeValue.FromNumber("12345678901234567890.123456789");

        // Act
        var number = value.AsNumber<decimal>();

        // Assert
        await Assert.That(number).IsEqualTo(12345678901234567890.123456789m);
    }

    [Test]
    public async Task AsNumber_FractionForInteger_ThrowsOverflowException()
    {
        // Arrange
        var value = AttributeValue.FromNumber("1.5");

        // Act
        var act = () => value.AsNumber<int>();

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    [Arguments("1E-29")]
    [Arguments("0.12345678901234567890123456789012345678")]
    public async Task AsNumber_DecimalThatWouldLoseDigits_ThrowsOverflowException(string text)
    {
        // Arrange
        var value = AttributeValue.FromNumber(text);

        // Act
        var act = () => value.AsNumber<decimal>();

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task AsNumber_HalfBeyondTheType_ThrowsOverflowException()
    {
        // Arrange
        var value = AttributeValue.FromNumber("70000");

        // Act
        var act = () => value.AsNumber<Half>();

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task ToString_LoneSurrogate_ShowsTheReplacementCharacter()
    {
        // Arrange
        AttributeValue value = "a\uD800";

        // Act
        var text = value.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("""{"S":"a\uFFFD"}""");
    }

    [Test]
    public async Task AsNumber_NotANumber_ThrowsFormatException()
    {
        // Arrange
        var value = AttributeValue.FromNumber("forty-two");

        // Act
        var act = () => value.AsNumber<int>();

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task FromBinary_SourceChangedAfterwards_KeepsOriginalBytes()
    {
        // Arrange
        byte[] bytes = [1, 2, 3];
        var value = AttributeValue.FromBinary(bytes);

        // Act
        bytes[0] = 9;

        // Assert
        await Assert.That(Convert.ToHexString(value.AsBinary().Span)).IsEqualTo("010203");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task FromBool_Value_AsBoolReturnsIt(bool flag)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var value = AttributeValue.FromBool(flag);

        // Assert
        await Assert.That(value.AsBool()).IsEqualTo(flag);
    }

    [Test]
    public async Task FromBool_CreateAndRead_DoesNotAllocate()
    {
        // Arrange
        static bool CreateAndRead(bool flag) => AttributeValue.FromBool(flag).AsBool() && AttributeValue.FromBool(!flag).AsBool();
        CreateAndRead(true);
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        CreateAndRead(true);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task FromList_SourceChangedAfterwards_KeepsOriginalElements()
    {
        // Arrange
        var source = new List<AttributeValue> { "a", "b" };
        var value = AttributeValue.FromList(source);

        // Act
        source[0] = "changed";

        // Assert
        await Assert.That(value.AsList()[0]).IsEqualTo(AttributeValue.FromString("a"));
    }

    [Test]
    public async Task FromList_Elements_AsListKeepsOrder()
    {
        // Arrange
        AttributeValue[] elements = ["b", AttributeValue.Null, "a"];

        // Act
        var value = AttributeValue.FromList(elements);

        // Assert
        await Assert.That(string.Join(',', value.AsList())).IsEqualTo("""{"S":"b"},{"NULL":true},{"S":"a"}""");
    }

    [Test]
    public async Task FromList_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<AttributeValue>? elements = null;

        // Act
        var act = () => AttributeValue.FromList(elements!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromList_DefaultElement_ThrowsArgumentException()
    {
        // Arrange
        AttributeValue[] elements = ["a", default];

        // Act
        var act = () => AttributeValue.FromList(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("A default AttributeValue has no type and cannot be part of a list or map.", StringComparison.Ordinal);
    }

    [Test]
    public async Task FromMap_SourceChangedAfterwards_KeepsOriginalEntries()
    {
        // Arrange
        var source = new Dictionary<string, AttributeValue> { ["a"] = "1" };
        var value = AttributeValue.FromMap(source);

        // Act
        source["b"] = "2";

        // Assert
        await Assert.That(value.AsMap().Count).IsEqualTo(1);
    }

    [Test]
    public async Task FromMap_Entries_AsMapLooksUpByName()
    {
        // Arrange
        KeyValuePair<string, AttributeValue>[] entries = [new("Name", "Joe"), new("Age", AttributeValue.FromNumber(35))];

        // Act
        var value = AttributeValue.FromMap(entries);

        // Assert
        await Assert.That(value.AsMap()["Age"]).IsEqualTo(AttributeValue.FromNumber("35"));
    }

    [Test]
    public async Task FromMap_Entries_HoldsThemInAnItemThatRefusesWrites()
    {
        // Arrange (a value is immutable, also for whoever casts AsMap)
        var map = (Item)AttributeValue.FromMap([new("a", 1)]).AsMap();

        // Act
        Action act = () => map["a"] = 2;

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task FromMap_AnItem_CopiesIt()
    {
        // Arrange
        var source = new Item { ["a"] = 1 };

        // Act
        var value = AttributeValue.FromMap(source);
        source["a"] = 2;

        // Assert
        await Assert.That(value.AsMap()["a"].AsNumber()).IsEqualTo("1");
    }

    [Test]
    public async Task FromMap_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<KeyValuePair<string, AttributeValue>>? entries = null;

        // Act
        var act = () => AttributeValue.FromMap(entries!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromMap_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        KeyValuePair<string, AttributeValue>[] entries = [new(null!, "1")];

        // Act
        var act = () => AttributeValue.FromMap(entries);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task FromMap_DuplicateKey_ThrowsArgumentException()
    {
        // Arrange
        KeyValuePair<string, AttributeValue>[] entries = [new("a", "1"), new("a", "2")];

        // Act
        var act = () => AttributeValue.FromMap(entries);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("The attribute name \"a\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    public async Task FromMap_DefaultValue_ThrowsArgumentException()
    {
        // Arrange
        KeyValuePair<string, AttributeValue>[] entries = [new("a", default)];

        // Act
        var act = () => AttributeValue.FromMap(entries);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("values");
    }

    [Test]
    public async Task FromStringSet_SourceChangedAfterwards_KeepsOriginalElements()
    {
        // Arrange
        string[] source = ["a", "b"];
        var value = AttributeValue.FromStringSet(source);

        // Act
        source[0] = "changed";

        // Assert
        await Assert.That(string.Join(',', value.AsStringSet())).IsEqualTo("a,b");
    }

    [Test]
    public async Task FromStringSet_EmptyString_IsAllowed()
    {
        // Arrange
        string[] elements = [string.Empty];

        // Act
        var value = AttributeValue.FromStringSet(elements);

        // Assert
        await Assert.That(value.AsStringSet().Count).IsEqualTo(1);
    }

    [Test]
    public async Task FromStringSet_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<string>? elements = null;

        // Act
        var act = () => AttributeValue.FromStringSet(elements!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromStringSet_NullElement_ThrowsArgumentNullException()
    {
        // Arrange
        string[] elements = ["a", null!];

        // Act
        var act = () => AttributeValue.FromStringSet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromStringSet_NoElements_ThrowsArgumentException()
    {
        // Arrange
        string[] elements = [];

        // Act
        var act = () => AttributeValue.FromStringSet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("A DynamoDB set must contain at least one element.", StringComparison.Ordinal);
    }

    [Test]
    public async Task FromStringSet_Duplicates_ThrowsArgumentException()
    {
        // Arrange
        string[] elements = ["a", "b", "a"];

        // Act
        var act = () => AttributeValue.FromStringSet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("A DynamoDB set cannot contain duplicates.", StringComparison.Ordinal);
    }

    [Test]
    public async Task FromNumberSet_TextuallyDifferentEqualNumbers_KeepsBoth()
    {
        // Arrange
        string[] elements = ["1", "1.0"];

        // Act
        var value = AttributeValue.FromNumberSet(elements);

        // Assert
        await Assert.That(string.Join(',', value.AsNumberSet())).IsEqualTo("1,1.0");
    }

    [Test]
    public async Task FromNumberSet_EmptyElement_ThrowsArgumentException()
    {
        // Arrange
        string[] elements = ["1", string.Empty];

        // Act
        var act = () => AttributeValue.FromNumberSet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("A number cannot be empty.", StringComparison.Ordinal);
    }

    [Test]
    public async Task FromNumberSet_NullElement_ThrowsArgumentNullException()
    {
        // Arrange
        string[] elements = [null!];

        // Act
        var act = () => AttributeValue.FromNumberSet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromNumberSet_TextualDuplicates_ThrowsArgumentException()
    {
        // Arrange
        string[] elements = ["7", "7"];

        // Act
        var act = () => AttributeValue.FromNumberSet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("values");
    }

    [Test]
    public async Task FromBinarySet_SourceBytesChangedAfterwards_KeepsOriginalBytes()
    {
        // Arrange
        byte[] element = [1, 2];
        var value = AttributeValue.FromBinarySet([element]);

        // Act
        element[0] = 9;

        // Assert
        await Assert.That(Convert.ToHexString(value.AsBinarySet()[0].Span)).IsEqualTo("0102");
    }

    [Test]
    public async Task FromBinarySet_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<byte[]>? elements = null;

        // Act
        var act = () => AttributeValue.FromBinarySet(elements!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromBinarySet_NullElement_ThrowsArgumentNullException()
    {
        // Arrange
        byte[][] elements = [[1], null!];

        // Act
        var act = () => AttributeValue.FromBinarySet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("values");
    }

    [Test]
    public async Task FromBinarySet_NoElements_ThrowsArgumentException()
    {
        // Arrange
        byte[][] elements = [];

        // Act
        var act = () => AttributeValue.FromBinarySet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("values");
    }

    [Test]
    public async Task FromBinarySet_SameContentInDifferentArrays_ThrowsArgumentException()
    {
        // Arrange
        byte[][] elements = [[1, 2], [3], [1, 2]];

        // Act
        var act = () => AttributeValue.FromBinarySet(elements);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("A DynamoDB set cannot contain duplicates.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(MismatchedAccessors))]
    public async Task As_ValueOfAnotherType_ThrowsInvalidOperationExceptionNamingBothTypes(Func<object> access, string message)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(access);

        // Act
        var act = () => access();

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining(message, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(EqualCases))]
    public async Task Equals_EqualValues_ReturnsTrue(ValuePair pair)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(pair);

        // Act
        var equal = pair.Left.Equals(pair.Right);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(UnequalCases))]
    public async Task Equals_DifferentValues_ReturnsFalse(ValuePair pair)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(pair);

        // Act
        var equal = pair.Left.Equals(pair.Right) || pair.Right.Equals(pair.Left);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(EqualCases))]
    public async Task GetHashCode_EqualValues_ReturnsSameHash(ValuePair pair)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(pair);

        // Act
        var same = pair.Left.GetHashCode() == pair.Right.GetHashCode();

        // Assert
        await Assert.That(same).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(UnequalCases))]
    public async Task GetHashCode_DifferentValues_ReturnsDifferentHashes(ValuePair pair)
    {
        // Arrange (collisions are possible in principle, but not for these fixed inputs)
        ArgumentNullException.ThrowIfNull(pair);

        // Act
        var same = pair.Left.GetHashCode() == pair.Right.GetHashCode();

        // Assert
        await Assert.That(same).IsFalse();
    }

    [Test]
    public async Task Equals_BoxedEqualValue_ReturnsTrue()
    {
        // Arrange
        AttributeValue value = "a";
        object boxed = AttributeValue.FromString("a");

        // Act
        var equal = value.Equals(boxed);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task Equals_OtherObject_ReturnsFalse()
    {
        // Arrange
        AttributeValue value = "a";
        object other = "a";

        // Act
        var equal = value.Equals(other);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task EqualityOperator_EqualValues_ReturnsTrue()
    {
        // Arrange
        var left = AttributeValue.FromStringSet(["a", "b"]);
        var right = AttributeValue.FromStringSet(["b", "a"]);

        // Act
        var equal = left == right;

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task InequalityOperator_DifferentValues_ReturnsTrue()
    {
        // Arrange
        AttributeValue left = "a";
        AttributeValue right = "b";

        // Act
        var different = left != right;

        // Assert
        await Assert.That(different).IsTrue();
    }

    [Test]
    public async Task InequalityOperator_EqualValues_ReturnsFalse()
    {
        // Arrange
        AttributeValue left = "a";
        AttributeValue right = "a";

        // Act
        var different = left != right;

        // Assert
        await Assert.That(different).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(JsonCases))]
    public async Task ToString_Value_ReturnsDynamoDbJson(ValueSample sample)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(sample);

        // Act
        var text = sample.Value.ToString();

        // Assert
        await Assert.That(text).IsEqualTo(sample.Json);
    }

    [Test]
    public async Task ToString_ValueLargerThanInitialBuffer_ReturnsCompleteJson()
    {
        // Arrange
        var value = AttributeValue.FromString(new string('x', 1000));

        // Act
        var text = value.ToString();

        // Assert
        await Assert.That(text).IsEqualTo($$"""{"S":"{{new string('x', 1000)}}"}""");
    }

    [Test]
    public async Task ToString_DefaultValue_ReturnsEmptyString()
    {
        // Arrange
        var value = default(AttributeValue);

        // Act
        var text = value.ToString();

        // Assert
        await Assert.That(text).IsEmpty();
    }

    [Test]
    public async Task AsBinarySet_Elements_ReturnsBytesOfEachElement()
    {
        // Arrange
        var value = AttributeValue.FromBinarySet(["Sunny"u8.ToArray(), "Rainy"u8.ToArray()]);

        // Act
        var elements = value.AsBinarySet();

        // Assert
        await Assert.That(Encoding.UTF8.GetString(elements[1].Span)).IsEqualTo("Rainy");
    }

    private static IEnumerable<Func<ValuePair>> AsCases(IEnumerable<Func<(AttributeValue Left, AttributeValue Right)>> pairs) => pairs.Select(static pair => new Func<ValuePair>(() =>
    {
        var (left, right) = pair();
        return new ValuePair(left, right);
    }));
}
