using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using DynaSharp.Operations;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests;

public sealed class ItemTests
{
    public static IEnumerable<int> Sizes() => [0, 1, 8, 9, 32, 33, 400];

    public static IEnumerable<Func<Func<Item, string, object?>>> Getters() =>
    [
        () => static (item, name) => item.GetString(name),
        () => static (item, name) => item.GetNumber<int>(name),
        () => static (item, name) => item.GetBool(name),
        () => static (item, name) => item.GetBinary(name),
        () => MapGetter,
        () => static (item, name) => item.GetList(name),
        () => static (item, name) => item.GetStringSet(name),
        () => static (item, name) => item.GetNumberSet(name),
        () => static (item, name) => item.GetBinarySet(name),
    ];

    [Test]
    public async Task Constructor_IndexerInitializer_HoldsTheAttributes()
    {
        // Act
        var item = new Item { ["pk"] = "a", ["n"] = 1, ["ok"] = true };

        // Assert
        await Assert.That((item.Count, item["pk"].AsString(), item["n"].AsNumber(), item["ok"].AsBool())).IsEqualTo((3, "a", "1", true));
    }

    [Test]
    public async Task Constructor_AddInitializer_HoldsTheAttributes()
    {
        // Act
        var item = new Item { { "pk", "a" }, { "n", 1 } };

        // Assert
        await Assert.That((item.Count, item["pk"].AsString(), item["n"].AsNumber())).IsEqualTo((2, "a", "1"));
    }

    [Test]
    public async Task Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new Item(-1);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(100)]
    public async Task Constructor_Capacity_HoldsMoreAttributesThanThat(int capacity)
    {
        // Arrange
        var item = new Item(capacity);

        // Act
        for (var i = 0; i <= capacity; i++)
        {
            item[Name(i)] = i;
        }

        // Assert
        await Assert.That(item.Count).IsEqualTo(capacity + 1);
    }

    [Test]
    public async Task Constructor_Attributes_CopiesThemInOrder()
    {
        // Arrange
        var source = new Dictionary<string, AttributeValue> { ["b"] = 1, ["a"] = 2 };

        // Act
        var item = new Item(source);
        source["b"] = 3;

        // Assert
        await Assert.That(string.Join(',', item.Select(static attribute => attribute.Key + attribute.Value.AsNumber()))).IsEqualTo("b1,a2");
    }

    [Test]
    public async Task Constructor_NullAttributes_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<KeyValuePair<string, AttributeValue>> attributes = null!;

        // Act
        var act = () => new Item(attributes);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_AttributesWithADuplicate_ThrowsArgumentException()
    {
        // Arrange
        KeyValuePair<string, AttributeValue>[] attributes = [new("a", 1), new("a", 2)];

        // Act
        var act = () => new Item(attributes);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Add_NameThatExists_ThrowsArgumentExceptionNamingTheAttribute()
    {
        // Arrange
        var item = new Item { ["pk"] = "secret" };

        // Act
        Action act = () => item.Add("pk", "other");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("The attribute name \"pk\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Add_NullName_ThrowsArgumentNullException()
    {
        // Arrange
        var item = new Item();

        // Act
        Action act = () => item.Add(null!, "a");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Add_DefaultValue_ThrowsArgumentException()
    {
        // Arrange (a default value has no type and could not be sent; better to fail where it is put in)
        var item = new Item();

        // Act
        Action act = () => item.Add("a", default);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task IndexerSet_DefaultValue_ThrowsArgumentException()
    {
        // Arrange
        var item = new Item();

        // Act
        Action act = () => item["a"] = default;

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task IndexerSet_NameThatExists_ReplacesTheValueInPlace()
    {
        // Arrange
        var item = new Item { ["a"] = 1, ["b"] = 2 };

        // Act
        item["a"] = 3;

        // Assert
        await Assert.That(string.Join(',', item.Select(static attribute => attribute.Key + attribute.Value.AsNumber()))).IsEqualTo("a3,b2");
    }

    [Test]
    public async Task IndexerGet_MissingAttribute_ThrowsKeyNotFoundExceptionNamingIt()
    {
        // Arrange
        var item = new Item { ["pk"] = "a" };

        // Act
        var act = () => item["nope"];

        // Assert
        await Assert.That(act).ThrowsExactly<KeyNotFoundException>()
            .WithMessage("The item has no attribute \"nope\".", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(9)]
    public async Task IndexerGet_NullName_ThrowsArgumentNullExceptionAtAnySize(int size)
    {
        // Arrange (linear search and index must agree)
        var item = Read(size);

        // Act
        var act = () => item[null!];

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task ReadItem_Size_FindsEveryAttributeAndNoOther(int size)
    {
        // Arrange
        var item = Read(size);

        // Act
        var found = Enumerable.Range(0, size).Count(i => item[Name(i)].AsNumber() == Text(i));
        var missing = item.ContainsKey("missing") || item.TryGetValue("missing", out _);

        // Assert
        await Assert.That((item.Count, found, missing)).IsEqualTo((size, size, false));
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task HandBuiltItem_Size_FindsEveryAttributeAndNoOther(int size)
    {
        // Arrange
        var item = new Item();

        // Act
        for (var i = 0; i < size; i++)
        {
            item.Add(Name(i), i);
        }

        var found = Enumerable.Range(0, size).Count(i => item[Name(i)].AsNumber() == Text(i));

        // Assert
        await Assert.That((item.Count, found, item.ContainsKey("missing"))).IsEqualTo((size, size, false));
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task ReadItem_ShuffledSecondItem_ReadsEveryValueUnderItsName(int size)
    {
        // Arrange (both emulators return one attribute set in varying order)
        var page = $"[{Json(Enumerable.Range(0, size))},{Json(Enumerable.Range(0, size).Reverse())}]";

        // Act
        var items = TestJson.Read(page, static (ref reader) => OperationJson.ReadItems(ref reader));
        var (first, second) = ((Item)items[0], (Item)items[1]);

        // Assert
        await Assert.That((second.Equals(first), ReferenceEquals(first.Shape, second.Shape))).IsEqualTo((true, true));
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task ToString_Size_IsTheJsonItWasReadFrom(int size)
    {
        // Arrange
        var json = Json(Enumerable.Range(0, size));

        // Act
        var text = TestJson.ReadItem(json).ToString();

        // Assert
        await Assert.That(text).IsEqualTo(json);
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task IndexerSet_NewAttributeOnReadItemOfSize_LeavesTheItemsThatShareItsShapeAlone(int size)
    {
        // Arrange
        var pools = TestPools.Create();
        var json = Json(Enumerable.Range(0, size));
        var changed = pools.Read(json);
        var other = pools.Read(json);

        // Act
        changed["new"] = "value";

        // Assert
        await Assert.That((changed.Count, changed["new"].AsString(), other.Count, other.ContainsKey("new"), pools.Read(json).Count)).IsEqualTo((size + 1, "value", size, false, size));
    }

    [Test]
    [MethodDataSource(nameof(Sizes))]
    public async Task IndexerSet_NewAttributesOnReadItemOfSize_KeepsEveryAttribute(int size)
    {
        // Arrange (past the limits of the shared index and the private index)
        var item = Read(size);

        // Act
        for (var i = size; i < size + 40; i++)
        {
            item[Name(i)] = i;
        }

        // Assert
        await Assert.That(Enumerable.Range(0, size + 40).All(i => item[Name(i)].AsNumber() == Text(i))).IsTrue();
    }

    [Test]
    public async Task Remove_AnAttribute_TakesItOutAndKeepsTheOrderOfTheRest()
    {
        // Arrange
        var item = new Item { ["a"] = 1, ["b"] = 2, ["c"] = 3 };

        // Act
        var removed = item.Remove("b");

        // Assert
        await Assert.That((removed, item.Count, string.Join(",", item.Keys), item.ContainsKey("b"), item.GetNumber<int>("c"))).IsEqualTo((true, 2, "a,c", false, (int?)3));
    }

    [Test]
    public async Task Remove_AnAttributeItDoesNotHave_ReturnsFalseAndChangesNothing()
    {
        // Arrange
        var item = new Item { ["a"] = 1 };

        // Act
        var removed = item.Remove("missing");

        // Assert
        await Assert.That((removed, item.Count)).IsEqualTo((false, 1));
    }

    [Test]
    public async Task Remove_NullName_Throws()
    {
        // Arrange
        var item = new Item { ["a"] = 1 };

        // Act
        var act = () => item.Remove(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Remove_ThenAddTheName_PutsItLast()
    {
        // Arrange
        var item = new Item { ["a"] = 1, ["b"] = 2 };

        // Act
        item.Remove("a");
        item["a"] = 3;

        // Assert
        await Assert.That((string.Join(",", item.Keys), item.GetNumber<int>("a"), item.Count)).IsEqualTo(("b,a", (int?)3, 2));
    }

    [Test]
    public async Task Remove_LastAttribute_LeavesAnEmptyItem()
    {
        // Arrange
        var item = new Item { ["a"] = 1 };
        Item empty = [];

        // Act
        item.Remove("a");

        // Assert
        await Assert.That((item.Count, item.Keys.Any(), item.Equals(empty))).IsEqualTo((0, false, true));
    }

    [Test]
    public async Task Remove_FromAReadItem_LeavesTheItemsThatShareItsNamesAlone()
    {
        // Arrange (items of one response share their names)
        var first = TestJson.ReadItem("""{"a":{"N":"1"},"b":{"N":"2"}}""");
        var second = TestJson.ReadItem("""{"a":{"N":"3"},"b":{"N":"4"}}""");

        // Act
        first.Remove("a");
        first["c"] = 5;

        // Assert
        await Assert.That((string.Join(",", first.Keys), string.Join(",", second.Keys), second.GetNumber<int>("a"))).IsEqualTo(("b,c", "a,b", (int?)3));
    }

    [Test]
    public async Task Remove_ManyAttributes_FindsWhatIsLeftThroughItsIndex()
    {
        // Arrange (above 32 attributes an item looks names up in an index)
        var item = new Item();
        for (var i = 0; i < 40; i++)
        {
            item["a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = i;
        }

        var built = item["a39"];

        // Act
        item.Remove("a0");

        // Assert
        await Assert.That((item.Count, item["a39"], item.ContainsKey("a0"))).IsEqualTo((39, built, false));
    }

    [Test]
    public async Task Remove_FromAMapOfAnAttributeValue_Throws()
    {
        // Arrange
        var item = new Item { ["m"] = AttributeValue.FromMap([KeyValuePair.Create("a", (AttributeValue)1)]) };
        var map = item.GetMap("m")!;

        // Act
        var act = () => map.Remove("a");

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("Copy it into a new Item", StringComparison.Ordinal);
    }

    [Test]
    public async Task IndexerSet_ExistingAttributeOfReadItem_LeavesTheItemThatSharesItsShapeAlone()
    {
        // Arrange
        var pools = TestPools.Create();
        var changed = pools.Read(Json([0, 1]));
        var other = pools.Read(Json([0, 1]));

        // Act
        changed[Name(0)] = "changed";

        // Assert
        await Assert.That((changed[Name(0)].AsString(), other[Name(0)].AsNumber(), ReferenceEquals(changed.Shape, other.Shape))).IsEqualTo(("changed", "0", true));
    }

    [Test]
    public async Task Add_ToReadItem_DoesNotGoIntoTheShapeOfLaterItems()
    {
        // Arrange
        var pools = TestPools.Create();
        var item = pools.Read(Json([0, 1]));
        var shape = item.Shape;

        // Act
        item.Add("new", 1);

        // Assert
        await Assert.That((shape.Names.Length, item.Shape.IsShared, shape.IsShared)).IsEqualTo((2, false, true));
    }

    [Test]
    public async Task Equals_SameContentInAnotherOrder_IsTrue()
    {
        // Arrange
        var left = new Item { ["a"] = 1, ["b"] = "x" };
        var right = new Item { ["b"] = "x", ["a"] = 1 };

        // Act
        var equal = left.Equals(right);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task Equals_ReadItemAndHandBuiltItem_IsTrue()
    {
        // Arrange
        var read = TestJson.ReadItem("""{"a":{"N":"1"},"m":{"M":{"x":{"S":"y"}}}}""");
        var built = new Item { ["m"] = AttributeValue.FromMap([new("x", "y")]), ["a"] = 1 };

        // Act
        var equal = (read.Equals(built), built.Equals(read));

        // Assert
        await Assert.That(equal).IsEqualTo((true, true));
    }

    [Test]
    public async Task Equals_ItemsOfOneShape_ComparesTheValues()
    {
        // Arrange
        var pools = TestPools.Create();
        var one = pools.Read("""{"a":{"N":"1"}}""");
        var same = pools.Read("""{"a":{"N":"1"}}""");
        var other = pools.Read("""{"a":{"N":"2"}}""");

        // Act
        var result = (one.Equals(same), one.Equals(other), ReferenceEquals(one.Shape, other.Shape));

        // Assert
        await Assert.That(result).IsEqualTo((true, false, true));
    }

    [Test]
    public async Task Equals_DifferentValue_IsFalse()
    {
        // Arrange
        var left = new Item { ["a"] = 1 };
        var right = new Item { ["a"] = 2 };

        // Act
        var equal = left.Equals(right);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task Equals_DifferentName_IsFalse()
    {
        // Arrange
        var left = new Item { ["a"] = 1 };
        var right = new Item { ["b"] = 1 };

        // Act
        var equal = left.Equals(right);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task Equals_DifferentCount_IsFalse()
    {
        // Arrange
        var left = new Item { ["a"] = 1 };
        var right = new Item { ["a"] = 1, ["b"] = 2 };

        // Act
        var equal = left.Equals(right);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task Equals_Null_IsFalse()
    {
        // Arrange
        var item = new Item();

        // Act
        var equal = item.Equals(NoItem());

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task Equals_ObjectOfAnotherType_IsFalse()
    {
        // Arrange (no equality with a dictionary: the rule would not be symmetric)
        object item = new Item { ["a"] = 1 };
        object dictionary = new Dictionary<string, AttributeValue> { ["a"] = 1 };

        // Act
        var equal = item.Equals(dictionary);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    public async Task Equals_ObjectThatIsAnEqualItem_IsTrue()
    {
        // Arrange
        object left = new Item { ["a"] = 1 };
        object right = new Item { ["a"] = 1 };

        // Act
        var equal = left.Equals(right);

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task GetHashCode_SameContentInAnotherOrder_IsEqual()
    {
        // Arrange
        var left = new Item { ["a"] = 1, ["b"] = "x", ["c"] = AttributeValue.FromMap([new("x", 1), new("y", 2)]) };
        var right = new Item { ["c"] = AttributeValue.FromMap([new("y", 2), new("x", 1)]), ["b"] = "x", ["a"] = 1 };

        // Act
        var hashes = (left.GetHashCode(), right.GetHashCode());

        // Assert
        await Assert.That(hashes.Item1).IsEqualTo(hashes.Item2);
    }

    [Test]
    public async Task GetHashCode_ChangedContent_Changes()
    {
        // Arrange
        var item = new Item { ["a"] = 1 };
        var before = item.GetHashCode();

        // Act
        item["a"] = 2;

        // Assert
        await Assert.That(item.GetHashCode()).IsNotEqualTo(before);
    }

    [Test]
    public async Task ToString_EveryType_IsDynamoDbJson()
    {
        // Arrange
        var item = new Item
        {
            ["s"] = "text",
            ["n"] = 42,
            ["b"] = AttributeValue.FromBinary([1, 2]),
            ["t"] = true,
            ["z"] = AttributeValue.Null,
            ["l"] = AttributeValue.FromList(["x"]),
            ["m"] = AttributeValue.FromMap([new("k", 1)]),
            ["ss"] = AttributeValue.FromStringSet(["y"]),
            ["ns"] = AttributeValue.FromNumberSet(["1"]),
            ["bs"] = AttributeValue.FromBinarySet([[3]]),
        };

        // Act
        var json = item.ToString();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"s":{"S":"text"},"n":{"N":"42"},"b":{"B":"AQI="},"t":{"BOOL":true},"z":{"NULL":true},"l":{"L":[{"S":"x"}]},"m":{"M":{"k":{"N":"1"}}},"ss":{"SS":["y"]},"ns":{"NS":["1"]},"bs":{"BS":["Aw=="]}}""");
    }

    [Test]
    public async Task ToString_NoAttributes_IsAnEmptyObject()
    {
        // Arrange
        var item = new Item();

        // Act
        var json = item.ToString();

        // Assert
        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    public async Task ToString_NameThatNeedsEscaping_ReadsBackAsTheSameName()
    {
        // Arrange
        var item = new Item { ["quo\"te\\ü"] = 1 };

        // Act
        var read = TestJson.ReadItem(item.ToString());

        // Assert
        await Assert.That(read.Single().Key).IsEqualTo("quo\"te\\ü");
    }

    [Test]
    public async Task DebuggerTypeProxy_ReadItem_ShowsOneRowPerAttribute()
    {
        // Arrange
        var item = TestJson.ReadItem("""{"pk":{"S":"a"},"n":{"N":"1"}}""");

        // Act
        var rows = new ItemDebugView(item).Attributes;

        // Assert
        await Assert.That(string.Join(',', rows.Select(static row => $"{row.Name}:{row.Type}:{row.Value}"))).IsEqualTo("""pk:String:{"S":"a"},n:Number:{"N":"1"}""");
    }

    [Test]
    public async Task DebuggerTypeProxy_ItemType_NamesTheDebugView()
    {
        // Act
        var proxy = typeof(Item).GetCustomAttribute<DebuggerTypeProxyAttribute>();

        // Assert
        await Assert.That(proxy!.ProxyTypeName).Contains(nameof(ItemDebugView));
    }

    [Test]
    public async Task GetString_String_ReturnsIt()
    {
        // Arrange
        var item = new Item { ["a"] = "text" };

        // Act
        var value = item.GetString("a");

        // Assert
        await Assert.That(value).IsEqualTo("text");
    }

    [Test]
    public async Task GetNumber_Number_ParsesIt()
    {
        // Arrange
        var item = new Item { ["a"] = AttributeValue.FromNumber("19.90") };

        // Act
        var value = item.GetNumber<decimal>("a");

        // Assert
        await Assert.That(value).IsEqualTo(19.90m);
    }

    [Test]
    public async Task GetNumber_BeyondThePrecisionOfBuiltInTypes_ParsesItWithBigInteger()
    {
        // Arrange (38 digits)
        const string digits = "12345678901234567890123456789012345678";
        var item = new Item { ["a"] = AttributeValue.FromNumber(digits) };

        // Act
        var value = item.GetNumber<BigInteger>("a");

        // Assert
        await Assert.That(value).IsEqualTo(BigInteger.Parse(digits, CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task GetNumber_FractionForAnInteger_ThrowsNamingTheAttributeAndNotTheNumber()
    {
        // Arrange
        var item = new Item { ["price"] = AttributeValue.FromNumber("19.90") };

        // Act
        var error = CatchFromGetNumber<int>(item, "price");

        // Assert
        await Assert.That((error is FormatException or OverflowException, error.Message.Contains("\"price\"", StringComparison.Ordinal), error.ToString().Contains("19.9", StringComparison.Ordinal))).IsEqualTo((true, true, false));
    }

    [Test]
    public async Task GetNumber_TextThatIsNoNumber_ThrowsFormatExceptionNamingTheAttributeAndNotTheText()
    {
        // Arrange (the wire does not validate the text of a number)
        var item = new Item { ["price"] = AttributeValue.FromNumber("not-a-number") };

        // Act
        var error = CatchFromGetNumber<int>(item, "price");

        // Assert
        await Assert.That((error.GetType(), error.Message.Contains("\"price\"", StringComparison.Ordinal), error.ToString().Contains("not-a-number", StringComparison.Ordinal))).IsEqualTo((typeof(FormatException), true, false));
    }

    [Test]
    public async Task GetNumber_OutOfRange_ThrowsOverflowExceptionNamingTheAttributeAndNotTheNumber()
    {
        // Arrange
        var item = new Item { ["count"] = AttributeValue.FromNumber("300") };

        // Act
        var error = CatchFromGetNumber<byte>(item, "count");

        // Assert
        await Assert.That((error.GetType(), error.Message.Contains("\"count\"", StringComparison.Ordinal), error.ToString().Contains("300", StringComparison.Ordinal))).IsEqualTo((typeof(OverflowException), true, false));
    }

    [Test]
    public async Task GetBool_Boolean_ReturnsIt()
    {
        // Arrange
        var item = new Item { ["a"] = true };

        // Act
        var value = item.GetBool("a");

        // Assert
        await Assert.That(value == true).IsTrue();
    }

    [Test]
    public async Task GetBinary_Binary_ReturnsIt()
    {
        // Arrange
        var item = new Item { ["a"] = AttributeValue.FromBinary([1, 2]) };

        // Act
        var value = item.GetBinary("a");

        // Assert
        await Assert.That(value!.Value.ToArray()).IsEquivalentTo(new byte[] { 1, 2 });
    }

    [Test]
    public async Task GetMap_NestedMap_AllowsChainedAccess()
    {
        // Arrange
        var item = TestJson.ReadItem("""{"address":{"M":{"city":{"S":"Zürich"}}}}""");

        // Act
        var city = item.GetMap("address")?.GetString("city");

        // Assert
        await Assert.That(city).IsEqualTo("Zürich");
    }

    [Test]
    public async Task GetList_List_ReturnsTheElementsInOrder()
    {
        // Arrange
        var item = new Item { ["a"] = AttributeValue.FromList(["x", 1]) };

        // Act
        var value = item.GetList("a");

        // Assert
        await Assert.That((value![0].AsString(), value[1].AsNumber())).IsEqualTo(("x", "1"));
    }

    [Test]
    public async Task GetStringSet_StringSet_ReturnsTheElements()
    {
        // Arrange
        var item = new Item { ["a"] = AttributeValue.FromStringSet(["x"]) };

        // Act
        var value = item.GetStringSet("a");

        // Assert
        await Assert.That(value!.Single()).IsEqualTo("x");
    }

    [Test]
    public async Task GetNumberSet_NumberSet_ReturnsTheElementsAsText()
    {
        // Arrange
        var item = new Item { ["a"] = AttributeValue.FromNumberSet(["1.50"]) };

        // Act
        var value = item.GetNumberSet("a");

        // Assert
        await Assert.That(value!.Single()).IsEqualTo("1.50");
    }

    [Test]
    public async Task GetBinarySet_BinarySet_ReturnsTheElements()
    {
        // Arrange
        var item = new Item { ["a"] = AttributeValue.FromBinarySet([[7]]) };

        // Act
        var value = item.GetBinarySet("a");

        // Assert
        await Assert.That(value!.Single().Span[0]).IsEqualTo((byte)7);
    }

    [Test]
    [MethodDataSource(nameof(Getters))]
    public async Task Getter_MissingAttribute_ReturnsNull(Func<Item, string, object?> get)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(get);
        var item = new Item { ["other"] = 1 };

        // Act
        var value = get(item, "missing");

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(Getters))]
    public async Task Getter_DynamoDbNull_ReturnsNull(Func<Item, string, object?> get)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(get);
        var item = new Item { ["a"] = AttributeValue.Null };

        // Act
        var value = get(item, "a");

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(Getters))]
    public async Task Getter_NullName_ThrowsArgumentNullException(Func<Item, string, object?> get)
    {
        // Arrange
        var item = new Item();

        // Act
        var act = () => get(item, null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [MethodDataSource(nameof(Getters))]
    public async Task Getter_AttributeOfAnotherType_ThrowsNamingTheAttributeAndNotTheValue(Func<Item, string, object?> get)
    {
        // Arrange (a map with a secret is of the wrong type for all getters but one; a string for that one)
        var item = new Item
        {
            ["a"] = AttributeValue.FromMap([new("k", "secret-value")]),
            ["b"] = "secret-value",
        };
        ArgumentNullException.ThrowIfNull(get);
        var name = IsMapGetter(get) ? "b" : "a";

        // Act
        var act = () => get(item, name);

        // Assert
        var error = await Assert.That(act).ThrowsExactly<InvalidOperationException>();
        await Assert.That((error!.Message.Contains($"\"{name}\"", StringComparison.Ordinal), error.Message.Contains("secret", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    [Test]
    public async Task GetMap_WritingIntoTheResult_ThrowsInvalidOperationException()
    {
        // Arrange (the map is part of an immutable value, which other items may share)
        var item = TestJson.ReadItem("""{"m":{"M":{"a":{"N":"1"}}}}""");
        var map = item.GetMap("m")!;

        // Act
        Action act = () => map["a"] = 2;

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GetMap_AddingToTheResultOfAHandBuiltMap_ThrowsInvalidOperationException()
    {
        // Arrange
        var item = new Item { ["m"] = AttributeValue.FromMap([new("a", 1)]) };
        var map = item.GetMap("m")!;

        // Act
        Action act = () => map.Add("b", 2);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Constructor_CopyOfAMap_IsWritableAndLeavesTheMapAlone()
    {
        // Arrange
        var item = TestJson.ReadItem("""{"m":{"M":{"a":{"N":"1"}}}}""");
        var map = item.GetMap("m")!;

        // Act
        var copy = new Item(map) { ["a"] = 2, ["b"] = 3 };
        item["m"] = AttributeValue.FromMap(copy);

        // Assert
        await Assert.That((map["a"].AsNumber(), map.Count, item.GetMap("m")!["b"].AsNumber())).IsEqualTo(("1", 1, "3"));
    }

    [Test]
    public async Task Enumerator_OnTheConcreteType_ReturnsTheAttributesThenStops()
    {
        // Arrange
        var item = new Item { ["a"] = 1, ["b"] = 2 };

        // Act
        var names = new List<string>();
        foreach (var attribute in item)
        {
            names.Add(attribute.Key);
        }

        // Assert
        await Assert.That(string.Join(',', names)).IsEqualTo("a,b");
    }

    [Test]
    public async Task Enumerator_Reset_StartsOver()
    {
        // Arrange
        var item = new Item { ["a"] = 1 };
        var enumerator = item.GetEnumerator();
        enumerator.MoveNext();

        // Act
        enumerator.Reset();
        var moved = enumerator.MoveNext();

        // Assert
        await Assert.That((moved, enumerator.Current.Key)).IsEqualTo((true, "a"));
    }

    [Test]
    public async Task GetEnumerator_ThroughTheNonGenericInterface_ReturnsTheAttributes()
    {
        // Arrange
        IEnumerable item = new Item { ["a"] = 1 };

        // Act
        var attributes = new List<object>();
        foreach (var attribute in item)
        {
            attributes.Add(attribute);
        }

        // Assert
        await Assert.That(((KeyValuePair<string, AttributeValue>)attributes.Single()).Key).IsEqualTo("a");
    }

    [Test]
    public async Task KeysAndValues_ReadItem_ArePairedInTheSameOrder()
    {
        // Arrange
        var item = TestJson.ReadItem(Json([0, 1, 2]));

        // Act
        var pairs = item.Keys.Zip(item.Values, static (name, value) => name + value.AsNumber());

        // Assert
        await Assert.That(string.Join(',', pairs)).IsEqualTo($"{Name(0)}0,{Name(1)}1,{Name(2)}2");
    }

    [Test]
    public async Task Values_HandBuiltItem_EnumeratesThemAllInInsertionOrder()
    {
        // Arrange
        var item = new Item { ["b"] = 1, ["a"] = 2 };

        // Act
        var values = item.Values.Select(static value => value.AsNumber());

        // Assert
        await Assert.That(string.Join(',', values)).IsEqualTo("1,2");
    }

    [Test]
    public async Task ImplicitConversion_Char_IsAString()
    {
        // Arrange
        const char letter = 'a';

        // Act
        AttributeValue value = letter;

        // Assert
        await Assert.That(value.AsString()).IsEqualTo("a");
    }

    private static Item? MapGetter(Item item, string name) => item.GetMap(name);

    private static Item? NoItem() => null;

    private static bool IsMapGetter(Func<Item, string, object?> getter) => getter.Method.Name == nameof(MapGetter);

    private static Exception CatchFromGetNumber<T>(Item item, string name)
        where T : struct, INumberBase<T>
    {
        try
        {
            item.GetNumber<T>(name);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            return error;
        }

        throw new InvalidOperationException("GetNumber was expected to throw.");
    }

    private static string Name(int index) => string.Create(CultureInfo.InvariantCulture, $"attribute{index}");

    private static string Text(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static Item Read(int size) => TestJson.ReadItem(Json(Enumerable.Range(0, size)));

    private static string Json(IEnumerable<int> attributes) =>
        "{" + string.Join(',', attributes.Select(i => $"\"{Name(i)}\":{{\"N\":\"{Text(i)}\"}}")) + "}";
}
