using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The results follow the Developer Guide, "Update expressions": SET reads every value from the item before the update, a list index past the end appends, REMOVE of several elements
// of a list takes the indexes the list had, ADD adds to a number or to a set and creates what is absent, DELETE takes elements out of a set and an empty set goes; and the API
// Reference of UpdateItem for UPDATED_OLD and UPDATED_NEW ("only the updated attributes"), which DynamoDB Local returns whole, as top-level attributes, for an update of a nested path too
// (Vectors/updates.json). Every item has the key pk, which the tests leave out of the JSON below.
public sealed class UpdateApplierTests
{
    private const string Key = """{"pk":{"S":"k"}}""";
    private const string InvalidPath = "The document path provided in the update expression is invalid for update";
    private const string MissingAttribute = "The provided expression refers to an attribute that does not exist in the item";
    private const string IncorrectType = "An operand in the update expression has an incorrect data type";

    private const string Values = """
        {":one":{"N":"1"},":zero":{"N":"0"},":ten":{"N":"10"},":tenth":{"N":"0.1"},":x":{"S":"x"},
         ":big":{"N":"99999999999999999999999999999999999999"},":wide":{"N":"12345678901234567890123456789012345678"},
         ":lx":{"L":[{"S":"x"}]},":sxy":{"SS":["x","y"]},":sa":{"SS":["a"]},":sab":{"SS":["a","b"]},":nums":{"NS":["1.0","2"]},":n1":{"NS":["1"]},":bs":{"BS":["Ag=="]}}
        """;

    [Test]
    [Arguments("SET a = :one", "{}", """{"a":{"N":"1"}}""", "{}", """{"a":{"N":"1"}}""")]
    [Arguments("SET a = :x", """{"a":{"N":"5"},"c":{"S":"keep"}}""", """{"a":{"S":"x"},"c":{"S":"keep"}}""", """{"a":{"N":"5"}}""", """{"a":{"S":"x"}}""")]
    [Arguments("SET a.b = :one", """{"a":{"M":{}}}""", """{"a":{"M":{"b":{"N":"1"}}}}""", """{"a":{"M":{}}}""", """{"a":{"M":{"b":{"N":"1"}}}}""")]
    [Arguments("SET a.b = :one", """{"a":{"M":{"b":{"N":"0"},"c":{"N":"2"}}}}""", """{"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}}}""", """{"a":{"M":{"b":{"N":"0"},"c":{"N":"2"}}}}""", """{"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}}}""")]
    [Arguments("SET a.b.c = :one", """{"a":{"M":{"b":{"M":{}}}}}""", """{"a":{"M":{"b":{"M":{"c":{"N":"1"}}}}}}""", """{"a":{"M":{"b":{"M":{}}}}}""", """{"a":{"M":{"b":{"M":{"c":{"N":"1"}}}}}}""")]
    [Arguments("SET l[1] = :x", """{"l":{"L":[{"S":"a"},{"S":"b"},{"S":"c"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"x"},{"S":"c"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"b"},{"S":"c"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"x"},{"S":"c"}]}}""")]
    [Arguments("SET l[10] = :x", """{"l":{"L":[{"S":"a"},{"S":"b"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"b"},{"S":"x"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"b"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"b"},{"S":"x"}]}}""")]
    [Arguments("SET l[0].b = :x", """{"l":{"L":[{"M":{}}]}}""", """{"l":{"L":[{"M":{"b":{"S":"x"}}}]}}""", """{"l":{"L":[{"M":{}}]}}""", """{"l":{"L":[{"M":{"b":{"S":"x"}}}]}}""")]
    [Arguments("REMOVE a SET b = a", """{"a":{"S":"moved"}}""", """{"b":{"S":"moved"}}""", """{"a":{"S":"moved"}}""", """{"b":{"S":"moved"}}""")]
    [Arguments("SET n = n + :one", """{"n":{"N":"10"}}""", """{"n":{"N":"11"}}""", """{"n":{"N":"10"}}""", """{"n":{"N":"11"}}""")]
    [Arguments("SET n = n - :ten", """{"n":{"N":"1"}}""", """{"n":{"N":"-9"}}""", """{"n":{"N":"1"}}""", """{"n":{"N":"-9"}}""")]
    [Arguments("SET n = :big + :one", "{}", """{"n":{"N":"100000000000000000000000000000000000000"}}""", "{}", """{"n":{"N":"100000000000000000000000000000000000000"}}""")]
    [Arguments("SET n = if_not_exists(n, :zero) + :one", "{}", """{"n":{"N":"1"}}""", "{}", """{"n":{"N":"1"}}""")]
    [Arguments("SET n = if_not_exists(n, :zero) + :one", """{"n":{"N":"10"}}""", """{"n":{"N":"11"}}""", """{"n":{"N":"10"}}""", """{"n":{"N":"11"}}""")]
    [Arguments("SET a = if_not_exists(a, :one)", """{"a":{"N":"5"}}""", """{"a":{"N":"5"}}""", """{"a":{"N":"5"}}""", """{"a":{"N":"5"}}""")]
    [Arguments("SET l = list_append(l, :lx)", """{"l":{"L":[{"S":"a"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"x"}]}}""", """{"l":{"L":[{"S":"a"}]}}""", """{"l":{"L":[{"S":"a"},{"S":"x"}]}}""")]
    [Arguments("SET l = list_append(:lx, l)", """{"l":{"L":[{"S":"a"}]}}""", """{"l":{"L":[{"S":"x"},{"S":"a"}]}}""", """{"l":{"L":[{"S":"a"}]}}""", """{"l":{"L":[{"S":"x"},{"S":"a"}]}}""")]
    [Arguments("ADD n :one", "{}", """{"n":{"N":"1"}}""", "{}", """{"n":{"N":"1"}}""")]
    [Arguments("ADD n :tenth", """{"n":{"N":"10"}}""", """{"n":{"N":"10.1"}}""", """{"n":{"N":"10"}}""", """{"n":{"N":"10.1"}}""")]
    [Arguments("ADD ss :sxy", """{"ss":{"SS":["a","x"]}}""", """{"ss":{"SS":["a","x","y"]}}""", """{"ss":{"SS":["a","x"]}}""", """{"ss":{"SS":["a","x","y"]}}""")]
    [Arguments("ADD ss :sxy", "{}", """{"ss":{"SS":["x","y"]}}""", "{}", """{"ss":{"SS":["x","y"]}}""")]
    [Arguments("ADD ns :nums", """{"ns":{"NS":["1"]}}""", """{"ns":{"NS":["1","2"]}}""", """{"ns":{"NS":["1"]}}""", """{"ns":{"NS":["1","2"]}}""")]
    [Arguments("ADD bs :bs", """{"bs":{"BS":["AQ=="]}}""", """{"bs":{"BS":["AQ==","Ag=="]}}""", """{"bs":{"BS":["AQ=="]}}""", """{"bs":{"BS":["AQ==","Ag=="]}}""")]
    [Arguments("ADD m.tally :one", """{"m":{"M":{}}}""", """{"m":{"M":{"tally":{"N":"1"}}}}""", """{"m":{"M":{}}}""", """{"m":{"M":{"tally":{"N":"1"}}}}""")]
    [Arguments("ADD l[3] :one", """{"l":{"L":[{"N":"5"}]}}""", """{"l":{"L":[{"N":"5"},{"N":"1"}]}}""", """{"l":{"L":[{"N":"5"}]}}""", """{"l":{"L":[{"N":"5"},{"N":"1"}]}}""")]
    [Arguments("DELETE ss :sa", """{"ss":{"SS":["a","b"]}}""", """{"ss":{"SS":["b"]}}""", """{"ss":{"SS":["a","b"]}}""", """{"ss":{"SS":["b"]}}""")]
    [Arguments("DELETE ss :sab", """{"ss":{"SS":["a","b"]}}""", "{}", """{"ss":{"SS":["a","b"]}}""", "{}")]
    [Arguments("DELETE ss :sa", "{}", "{}", "{}", "{}")]
    [Arguments("DELETE ns :n1", """{"ns":{"NS":["1.0","3"]}}""", """{"ns":{"NS":["3"]}}""", """{"ns":{"NS":["1","3"]}}""", """{"ns":{"NS":["3"]}}""")]
    [Arguments("DELETE l[5] :sa", """{"l":{"L":[{"S":"x"}]}}""", """{"l":{"L":[{"S":"x"}]}}""", """{"l":{"L":[{"S":"x"}]}}""", "{}")]
    [Arguments("REMOVE a", """{"a":{"S":"x"},"b":{"S":"y"}}""", """{"b":{"S":"y"}}""", """{"a":{"S":"x"}}""", "{}")]
    [Arguments("REMOVE absent", "{}", "{}", "{}", "{}")]
    [Arguments("REMOVE a.b", """{"a":{"M":{}}}""", """{"a":{"M":{}}}""", """{"a":{"M":{}}}""", "{}")]
    [Arguments("REMOVE m.k", """{"m":{"M":{"k":{"S":"v"},"j":{"S":"w"}}}}""", """{"m":{"M":{"j":{"S":"w"}}}}""", """{"m":{"M":{"k":{"S":"v"},"j":{"S":"w"}}}}""", "{}")]
    [Arguments("REMOVE l[1], l[0]", """{"l":{"L":[{"S":"x"},{"S":"y"},{"S":"z"}]}}""", """{"l":{"L":[{"S":"z"}]}}""", """{"l":{"L":[{"S":"x"},{"S":"y"},{"S":"z"}]}}""", "{}")]
    [Arguments("REMOVE l[0], l[2]", """{"l":{"L":[{"S":"x"},{"S":"y"},{"S":"z"}]}}""", """{"l":{"L":[{"S":"y"}]}}""", """{"l":{"L":[{"S":"x"},{"S":"y"},{"S":"z"}]}}""", "{}")]
    [Arguments("REMOVE l[5]", """{"l":{"L":[{"S":"x"}]}}""", """{"l":{"L":[{"S":"x"}]}}""", """{"l":{"L":[{"S":"x"}]}}""", "{}")]
    [Arguments("REMOVE l[0].b, l[1]", """{"l":{"L":[{"M":{"b":{"N":"1"},"c":{"N":"2"}}},{"S":"y"}]}}""", """{"l":{"L":[{"M":{"c":{"N":"2"}}}]}}""", """{"l":{"L":[{"M":{"b":{"N":"1"},"c":{"N":"2"}}},{"S":"y"}]}}""", "{}")]
    [Arguments("REMOVE a, b", """{"a":{"S":"x"},"b":{"S":"y"}}""", "{}", """{"a":{"S":"x"},"b":{"S":"y"}}""", "{}")]
    [Arguments(
        "SET a = :one REMOVE b ADD c :one DELETE d :sa",
        """{"b":{"S":"x"},"c":{"N":"1"},"d":{"SS":["a","b"]}}""",
        """{"a":{"N":"1"},"c":{"N":"2"},"d":{"SS":["b"]}}""",
        """{"b":{"S":"x"},"c":{"N":"1"},"d":{"SS":["a","b"]}}""",
        """{"a":{"N":"1"},"c":{"N":"2"},"d":{"SS":["b"]}}""")]
    public async Task Apply_Update_GivesTheNewItemAndTheUpdatedAttributes(string update, string current, string expectedItem, string expectedOld, string expectedNew)
    {
        // Arrange
        var parsed = FakeExpressions.ParseUpdate(update, Scope(Values));

        // Act
        var (image, updatedOld, updatedNew) = parsed.Apply(WithKey(current), Item(Key));

        // Assert
        await Assert.That((image, updatedOld, updatedNew)).IsEqualTo((WithKey(expectedItem), Item(expectedOld), Item(expectedNew)));
    }

    [Test]
    public async Task Apply_NoItem_StartsFromTheKey()
    {
        // Arrange (an upsert: UpdateItem on a key that has no item writes the key and the update)
        var parsed = FakeExpressions.ParseUpdate("SET a = :one, b = pk", Scope(Values));

        // Act
        var (image, updatedOld, updatedNew) = parsed.Apply(null, Item(Key));

        // Assert
        await Assert.That((image, updatedOld, updatedNew))
            .IsEqualTo((Item("""{"pk":{"S":"k"},"a":{"N":"1"},"b":{"S":"k"}}"""), Item("{}"), Item("""{"a":{"N":"1"},"b":{"S":"k"}}""")));
    }

    [Test]
    public async Task Apply_Update_LeavesTheCurrentItemAsItWas()
    {
        // Arrange
        var parsed = FakeExpressions.ParseUpdate("SET a.b = :one REMOVE c", Scope(Values));
        var current = WithKey("""{"a":{"M":{"b":{"N":"0"}}},"c":{"S":"x"}}""");

        // Act
        _ = parsed.Apply(current, Item(Key));

        // Assert
        await Assert.That(current).IsEqualTo(WithKey("""{"a":{"M":{"b":{"N":"0"}}},"c":{"S":"x"}}"""));
    }

    [Test]
    [Arguments("SET a.b = :one", "{}", InvalidPath)]
    [Arguments("SET a.b.c = :one", """{"a":{"M":{}}}""", InvalidPath)]
    [Arguments("SET a.b = :one", """{"a":{"S":"x"}}""", InvalidPath)]
    [Arguments("SET a[0] = :one", """{"a":{"M":{}}}""", InvalidPath)]
    [Arguments("SET l[5].b = :x", """{"l":{"L":[{"S":"a"}]}}""", InvalidPath)]
    [Arguments("REMOVE a.b", "{}", InvalidPath)]
    [Arguments("ADD a.b :one", """{"a":{"L":[]}}""", InvalidPath)]
    [Arguments("SET a = b", "{}", MissingAttribute)]
    [Arguments("SET n = absent + :one", "{}", MissingAttribute)]
    [Arguments("SET l = list_append(absent, :lx)", "{}", MissingAttribute)]
    [Arguments("SET n = s + :one", """{"s":{"S":"x"}}""", IncorrectType)]
    [Arguments("SET n = :one - s", """{"s":{"S":"x"}}""", IncorrectType)]
    [Arguments("SET l = list_append(s, :lx)", """{"s":{"S":"x"}}""", IncorrectType)]
    [Arguments("SET l = list_append(:lx, s)", """{"s":{"S":"x"}}""", IncorrectType)]
    [Arguments("ADD s :one", """{"s":{"S":"x"}}""", IncorrectType)]
    [Arguments("ADD ns :sa", """{"ns":{"NS":["1"]}}""", IncorrectType)]
    [Arguments("DELETE ns :sa", """{"ns":{"NS":["1"]}}""", IncorrectType)]
    [Arguments("SET n = :wide + :tenth", "{}", "Attempting to store more than 38 significant digits in a Number")]
    [Arguments("SET pk = :x", "{}", "One or more parameter values were invalid: Cannot update attribute pk. This attribute is part of the key")]
    [Arguments("REMOVE pk.a", "{}", "One or more parameter values were invalid: Cannot update attribute pk. This attribute is part of the key")]
    public async Task Apply_UpdateDynamoDbRejectsOnTheItem_ThrowsValidation(string update, string current, string message)
    {
        // Arrange
        var parsed = FakeExpressions.ParseUpdate(update, Scope(Values));
        var item = WithKey(current);

        // Act
        void Act() => parsed.Apply(item, Item(Key));

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task Apply_RemovalsInsideEachOther_RemovesTheInnerFirst()
    {
        // Arrange (the parser rejects such paths; the applier still orders them, the inner one first)
        UpdateAction[] actions = [new UpdateAction.Remove(Path("a")), new UpdateAction.Remove(Path("a", "b"))];

        // Act
        var (image, _, _) = UpdateApplier.Apply(actions, WithKey("""{"a":{"M":{"b":{"N":"1"}}}}"""), Item(Key));

        // Assert
        await Assert.That(image).IsEqualTo(Item(Key));
    }

    private static DocumentPath Path(params string[] names) => new([.. names.Select(PathSegment.Of)]);

    private static Item WithKey(string json)
    {
        var item = Item(Key);
        foreach (var (name, value) in Item(json))
        {
            item.Add(name, value);
        }

        return item;
    }
}
