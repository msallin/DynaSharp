using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>What generated code reads, from items written by hand, and the rules for missing, null and wrong attributes of section 5.7.</summary>
public sealed class EntityReadTests
{
    private const string Minimal = """{"pk":{"S":"bob"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"}}""";

    [Test]
    public async Task ReadAttributes_FullEntity_ReadsEveryAttribute()
    {
        // Arrange
        const string Json = """
            {"_type":{"S":"PURCHASE"},"pk":{"S":"alice"},"sk":{"N":"42"},"Total":{"N":"12.50"},"Status":{"S":"Paid"},
             "Tracking":{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"},"ShipTo":{"M":{"City":{"S":"Bern"},"Street":{"S":"Main St"}}},
             "Lines":{"L":[{"M":{"Quantity":{"N":"2"},"Sku":{"S":"A-1"}}}]},"Tags":{"SS":["red","blue"]},"Stages":{"SS":["Open","Closed"]},
             "Counters":{"M":{"views":{"N":"3"}}},"Rights":{"N":"3"},"Fee":{"S":"150c"},"Tip":{"S":"20c"},"ttl":{"N":"1793448000"},"items":{"N":"1"},"Revision":{"N":"5"}}
            """;

        // Act
        var purchase = EntityJson.Read<Purchase>(Json);

        // Assert
        await Assert.That((purchase.Customer, purchase.Number, purchase.Total, purchase.Status, purchase.Tracking, purchase.ShipTo))
            .IsEqualTo(("alice", 42, 12.5m, Status.Paid, new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), new Place("Main St", "Bern", null)));
        await Assert.That((purchase.Lines.Single(), purchase.Tags.Count, purchase.Stages.Contains(Status.Closed), purchase.Counters["views"]))
            .IsEqualTo((new Line { Sku = "A-1", Quantity = 2 }, 2, true, 3));
        await Assert.That((purchase.Rights, purchase.Fee, purchase.Tip, purchase.ExpiresAt, purchase.ItemCount, purchase.Revision))
            .IsEqualTo((Access.Read | Access.Write, new Cents(150), (Cents?)new Cents(20), (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(1793448000), 1, 5L));
    }

    [Test]
    public async Task ReadAttributes_AttributesInAnyOrder_AreAllFound()
    {
        // Arrange (DynamoDB gives no order)
        const string Json = """{"Status":{"S":"Closed"},"Total":{"N":"1"},"sk":{"N":"2"},"_type":{"S":"PURCHASE"},"pk":{"S":"x"}}""";

        // Act
        var purchase = EntityJson.Read<Purchase>(Json);

        // Assert
        await Assert.That((purchase.Customer, purchase.Number, purchase.Status)).IsEqualTo(("x", 2, Status.Closed));
    }

    [Test]
    public async Task ReadAttributes_UnknownAttributes_AreSkipped()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"Unknown":{"M":{"a":{"L":[{"N":"1"}]}}},"sk":{"N":"2"},"Toal":{"N":"9"},"Total":{"N":"3"},"Status":{"S":"Open"}}""";

        // Act
        var purchase = EntityJson.Read<Purchase>(Json);

        // Assert
        await Assert.That((purchase.Number, purchase.Total)).IsEqualTo((2, 3m));
    }

    [Test]
    public async Task ReadAttributes_MalformedValueInAnUnknownAttribute_NamesNoKnownAttribute()
    {
        // Arrange (the name before it was read fine, and has nothing to do with this)
        const string Json = """{"pk":{"S":"x"},"Unknown":[1],"sk":{"N":"2"},"Total":{"N":"3"},"Status":{"S":"Open"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is System.Text.Json.JsonException)).IsEqualTo(((string?)null, true));
    }

    [Test]
    public async Task ReadAttributes_MalformedValueInAKnownAttributeAfterAnUnknownOne_NamesTheKnownAttribute()
    {
        // Arrange
        const string Json = """{"Unknown":{"S":"a"},"Total":[1],"pk":{"S":"x"},"sk":{"N":"2"},"Status":{"S":"Open"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Total");
    }

    [Test]
    public async Task ReadAttributes_SetMissing_KeepsTheEmptySetOfTheInitializer()
    {
        // Arrange, Act
        var shelf = EntityJson.Read<Shelf>("""{"Id":{"S":"a"},"Rack":{"N":"1"}}""");

        // Assert
        await Assert.That((shelf.Flags.Count, shelf.Numbers.Count, shelf.Names.Length)).IsEqualTo((0, 0, 0));
    }

    [Test]
    public async Task ReadAttributes_SetPresent_ReplacesTheEmptySetOfTheInitializer()
    {
        // Arrange, Act
        var shelf = EntityJson.Read<Shelf>("""{"Id":{"S":"a"},"Rack":{"N":"1"},"Numbers":{"NS":["1"]}}""");

        // Assert
        await Assert.That(shelf.Numbers.Single()).IsEqualTo(1);
    }

    [Test]
    public async Task ReadAttributes_EscapedName_IsKnown()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"sk":{"N":"2"},"Total":{"N":"3"},"Status":{"S":"Open"}}""";

        // Act
        var purchase = EntityJson.Read<Purchase>(Json);

        // Assert
        await Assert.That(purchase.Customer).IsEqualTo("x");
    }

    [Test]
    public async Task ReadAttributes_RepeatedName_LastOneWins()
    {
        // Arrange
        const string Json = """{"pk":{"S":"first"},"pk":{"S":"second"},"sk":{"N":"2"},"Total":{"N":"3"},"Status":{"S":"Open"}}""";

        // Act
        var purchase = EntityJson.Read<Purchase>(Json);

        // Assert
        await Assert.That(purchase.Customer).IsEqualTo("second");
    }

    [Test]
    public async Task ReadAttributes_WriteOnlyAttribute_IsSkipped()
    {
        // Arrange (the stored count is stale, the getter computes it)
        var json = Minimal.Replace("}}", """},"items":{"N":"99"}}""", StringComparison.Ordinal);

        // Act
        var purchase = EntityJson.Read<Purchase>(json);

        // Assert
        await Assert.That(purchase.ItemCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReadAttributes_IgnoredProperty_KeepsItsInitializer()
    {
        // Arrange
        var json = Minimal.Replace("}}", """},"Scratch":{"S":"stored"}}""", StringComparison.Ordinal);

        // Act
        var purchase = EntityJson.Read<Purchase>(json);

        // Assert
        await Assert.That(purchase.Scratch).IsEqualTo("not stored");
    }

    [Test]
    [Arguments("pk")]
    [Arguments("sk")]
    [Arguments("Total")]
    [Arguments("Status")]
    public async Task ReadAttributes_RequiredAttributeMissing_ThrowsNamingIt(string attribute)
    {
        // Arrange
        var json = Without(Minimal, attribute);

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName, failure.Message))
            .IsEqualTo(("Purchase", (string?)attribute, $"The attribute \"{attribute}\" is missing, so the item cannot be read as Purchase."));
    }

    [Test]
    public async Task ReadAttributes_RequiredInputsMissing_RestoresThemFromTheKey()
    {
        // Arrange (what an update leaves: the key and the discriminator)
        const string Json = """{"_type":{"S":"DAILY"},"pk":{"S":"DAILY#/home"},"sk":{"S":"DAY#2026-11-01"},"Count":{"N":"3"}}""";

        // Act
        var daily = EntityJson.Read<DailyCount>(Json);

        // Assert
        await Assert.That(daily).IsEqualTo(new DailyCount { Path = "/home", Day = new DateOnly(2026, 11, 1), Count = 3 });
    }

    [Test]
    public async Task ReadAttributes_KeysAfterTheOtherAttributes_RestoresThemAll()
    {
        // Arrange (JSON has no order of attributes)
        const string Json = """{"Count":{"N":"3"},"sk":{"S":"DAY#2026-11-01"},"_type":{"S":"DAILY"},"pk":{"S":"DAILY#/home"}}""";

        // Act
        var daily = EntityJson.Read<DailyCount>(Json);

        // Assert
        await Assert.That((daily.Path, daily.Day)).IsEqualTo(("/home", new DateOnly(2026, 11, 1)));
    }

    [Test]
    public async Task ReadAttributes_InputThatIsThere_WinsOverTheKey()
    {
        // Arrange (a value the template would reject does not parse, so only the attribute can say it)
        const string Json = """{"pk":{"S":"DAILY#stale"},"Path":{"S":"/fresh"},"sk":{"S":"DAY#2026-11-01"},"Day":{"S":"2026-10-31"}}""";

        // Act
        var daily = EntityJson.Read<DailyCount>(Json);

        // Assert
        await Assert.That((daily.Path, daily.Day)).IsEqualTo(("/fresh", new DateOnly(2026, 10, 31)));
    }

    [Test]
    public async Task ReadAttributes_OneInputOfAKeyMissing_RestoresThatOneAndKeepsTheOther()
    {
        // Arrange
        const string Json = """{"pk":{"S":"GRID#g"},"sk":{"S":"CELL#stale#042"},"Column":{"S":"fresh"}}""";

        // Act
        var cell = EntityJson.Read<GridCell>(Json);

        // Assert
        await Assert.That(cell).IsEqualTo(new GridCell("g", "fresh", 42));
    }

    [Test]
    public async Task ReadAttributes_ConstructorParameters_AreRestored()
    {
        // Arrange
        const string Json = """{"pk":{"S":"GRID#g"},"sk":{"S":"CELL#a#042"}}""";

        // Act
        var cell = EntityJson.Read<GridCell>(Json);

        // Assert
        await Assert.That(cell).IsEqualTo(new GridCell("g", "a", 42));
    }

    [Test]
    public async Task ReadAttributes_EscapedKeyText_IsRestoredUnescaped()
    {
        // Arrange
        const string Json = """{"pk":{"S":"DAILY#\u002Fhome"},"sk":{"S":"DAY#2026-11-01"}}""";

        // Act
        var daily = EntityJson.Read<DailyCount>(Json);

        // Assert
        await Assert.That(daily.Path).IsEqualTo("/home");
    }

    [Test]
    public async Task ReadAttributes_EscapedKeyName_IsFound()
    {
        // Arrange
        const string Json = """{"\u0070k":{"S":"DAILY#/home"},"sk":{"S":"DAY#2026-11-01"}}""";

        // Act
        var daily = EntityJson.Read<DailyCount>(Json);

        // Assert
        await Assert.That(daily.Path).IsEqualTo("/home");
    }

    [Test]
    [Arguments("""{"sk":{"S":"DAY#2026-11-01"}}""", "Path", "pk")]
    [Arguments("""{"pk":{"N":"1"},"sk":{"S":"DAY#2026-11-01"}}""", "Path", "pk")]
    [Arguments("""{"pk":{"NULL":true},"sk":{"S":"DAY#2026-11-01"}}""", "Path", "pk")]
    [Arguments("""{"pk":{"S":"OTHER#/home"},"sk":{"S":"DAY#2026-11-01"}}""", "Path", "pk")]
    [Arguments("""{"pk":{"S":"DAILY#"},"sk":{"S":"DAY#2026-11-01"}}""", "Path", "pk")]
    [Arguments("""{"pk":{"S":"DAILY#/home"},"sk":{"S":"DAY#not-a-date"}}""", "Day", "sk")]
    [Arguments("""{"pk":{"S":"DAILY#/home"}}""", "Day", "sk")]
    public async Task ReadAttributes_KeyThatCannotSayIt_ThrowsNamingTheAttributeAndTheKey(string json, string attribute, string key)
    {
        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<DailyCount>(json));

        // Assert
        await Assert.That((failure.AttributeName, failure.Message)).IsEqualTo(((string?)attribute, $"The attribute \"{attribute}\" is missing, and the key \"{key}\" that could restore it is missing or does not fit its template, so the item cannot be read as DailyCount."));
    }

    [Test]
    public async Task ReadAttributes_KeyThatCannotSayIt_KeepsTheValueOutOfTheMessage()
    {
        // Arrange (a key holds values, and a message ends up in logs)
        const string Json = """{"pk":{"S":"OTHER#secret-value"},"sk":{"S":"DAY#2026-11-01"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<DailyCount>(Json));

        // Assert
        await Assert.That(failure.Message).DoesNotContain("secret-value");
    }

    [Test]
    public async Task ReadAttributes_KeyThatDoesNotFitItsTemplate_ThrowsSayingSoWithoutTheValue()
    {
        // Arrange (a key is the only source of this property, so nothing else can stand in)
        const string Json = """{"pk":{"S":"not-a-plan"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<KeyOnlyPlan>(Json));

        // Assert
        await Assert.That((failure.AttributeName, failure.Message)).IsEqualTo(((string?)"pk", "The key \"pk\" does not fit the key template of KeyOnlyPlan."));
    }

    [Test]
    [Arguments("""{"pk":{"S":"not-a-plan"},"_type":{"S":"OTHER"}}""")]
    [Arguments("""{"_type":{"S":"OTHER"},"pk":{"S":"not-a-plan"}}""")]
    public async Task ReadAttributes_ItemOfAnotherTypeWhoseKeyDoesNotFit_NamesTheTypeInEitherOrder(string json)
    {
        // Arrange, Act (a key of another type is no surprise, and an item has no order of attributes)
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<KeyOnlyPlan>(json));

        // Assert
        await Assert.That((failure.AttributeName, failure.Message)).IsEqualTo(((string?)"_type", "The item is of type \"OTHER\", so it cannot be read as KeyOnlyPlan."));
    }

    [Test]
    public async Task ReadAttributes_RequiredTimestampWithoutAFormat_IsRestoredFromTheKeyToTheTick()
    {
        // Arrange (what an update leaves under ORDER#{CreatedAt}#{OrderId}: a placeholder without a format keeps ticks)
        const string Json = """{"_type":{"S":"ORDER"},"pk":{"S":"USER#0f8fad5b-d9cb-469f-a165-70867728950e"},"sk":{"S":"ORDER#2026-10-31T12:34:56.7890123Z#4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b"},"Status":{"S":"Free"},"Total":{"N":"1"}}""";

        // Act
        var order = EntityJson.Read<KeyedOrder>(Json);

        // Assert
        await Assert.That(order.CreatedAt.UtcTicks).IsEqualTo(new DateTime(2026, 10, 31, 12, 34, 56, DateTimeKind.Utc).Ticks + 7_890_123);
    }

    [Test]
    public async Task ReadAttributes_RequiredInputOfALossyFormat_ThrowsNamingTheAttribute()
    {
        // Arrange (At is in the sort key by the format ms, which is no way back to the ticks)
        const string Json = """{"pk":{"S":"LOG#s"},"sk":{"S":"AT#2026-11-01#2026-11-01T12:00:00.000Z#0003"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<LogLine>(Json));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("At");
    }

    [Test]
    public async Task ReadAttributes_OptionalInputMissing_IsRestoredFromTheKey()
    {
        // Arrange
        const string Json = """{"pk":{"S":"LOG#s"},"sk":{"S":"AT#2026-11-01#2026-11-01T12:00:00.000Z#0003"},"At":{"S":"2026-11-01T12:00:00+00:00"}}""";

        // Act
        var line = EntityJson.Read<LogLine>(Json);

        // Assert
        await Assert.That((line.Sequence, line.Tag)).IsEqualTo((3, "none"));
    }

    [Test]
    public async Task ReadAttributes_OptionalInputWithAKeyThatDoesNotParse_KeepsWhatTheInitializerLeft()
    {
        // Arrange (an older item must read as it did)
        const string Json = """{"pk":{"S":"LOG#s"},"Day":{"S":"2026-11-01"},"At":{"S":"2026-11-01T12:00:00+00:00"},"sk":{"S":"written by something else"}}""";

        // Act
        var line = EntityJson.Read<LogLine>(Json);

        // Assert
        await Assert.That(line.Sequence).IsEqualTo(7);
    }

    [Test]
    public async Task ReadAttributes_InputOfAKeyOfAnIndex_IsRestoredFromThatKey()
    {
        // Arrange
        const string Json = """{"pk":{"S":"LOG#s"},"Day":{"S":"2026-11-01"},"At":{"S":"2026-11-01T12:00:00+00:00"},"sk":{"S":"x"},"byownerpk":{"S":"OWNER#ann"},"byownersk":{"S":"TAG#red"}}""";

        // Act
        var line = EntityJson.Read<LogLine>(Json);

        // Assert
        await Assert.That(line.Tag).IsEqualTo("red");
    }

    [Test]
    public async Task ReadAttributes_NullableInputMissing_StaysNullWhateverTheKeySays()
    {
        // Arrange (null is left out on write, so a key that a raw path left behind is not the value)
        const string Json = """{"pk":{"S":"LOG#s"},"Day":{"S":"2026-11-01"},"At":{"S":"2026-11-01T12:00:00+00:00"},"sk":{"S":"x"},"byownerpk":{"S":"OWNER#ann"},"byownersk":{"S":"TAG#red"}}""";

        // Act
        var line = EntityJson.Read<LogLine>(Json);

        // Assert
        await Assert.That(line.Owner).IsNull();
    }

    [Test]
    public async Task ReadAttributes_InputWithALossyKeyAndALosslessOne_TakesTheLosslessOne()
    {
        // Arrange (When is in the key of the partition by ticks, in the other one by ms)
        const string Json = """{"pk":{"S":"LOG#s"},"Day":{"S":"2026-11-01"},"At":{"S":"2026-11-01T12:00:00+00:00"},"sk":{"S":"x"},"bywhensk":{"S":"2026-11-01T12:00:00.000Z"},"bywhenpk":{"S":"W#2026-11-01T12:00:00.1234567Z"}}""";

        // Act
        var line = EntityJson.Read<LogLine>(Json);

        // Assert
        await Assert.That(line.When).IsEqualTo(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567));
    }

    [Test]
    public async Task ReadAttributes_InputBesideAKeyOnlyProperty_RestoresBoth()
    {
        // Arrange (the read parses the key once for what it never stores, and again for what is missing)
        const string Json = """{"pk":{"S":"KEPT#n#1"}}""";

        // Act
        var kept = EntityJson.Read<HalfKept>(Json);

        // Assert
        await Assert.That(kept).IsEqualTo(new HalfKept { Name = "n", Id = "1" });
    }

    [Test]
    public async Task ReadAttributes_OptionalAttributesMissing_KeepWhatTheInitializerLeft()
    {
        // Arrange, Act
        var purchase = EntityJson.Read<Purchase>(Minimal);

        // Assert
        await Assert.That((purchase.Tracking, purchase.ShipTo, purchase.Lines.Count, purchase.Tags.Count, purchase.Counters.Count, purchase.Revision, purchase.Tip))
            .IsEqualTo(((Guid?)null, (Place?)null, 0, 0, 0, 0L, (Cents?)null));
    }

    [Test]
    public async Task ReadAttributes_ClassWithInitializers_KeepsThemWhenMissing()
    {
        // Arrange, Act
        var account = EntityJson.Read<Account>("""{"Id":{"S":"a"}}""");

        // Assert
        await Assert.That((account.Id, account.Plan, account.Logins, account.Note, account.Aliases.Single(), account.Scores.Length))
            .IsEqualTo(("a", "free", 7, (string?)"kept", "first", 2));
    }

    [Test]
    public async Task ReadAttributes_PrivateAndProtectedSetters_AreAssigned()
    {
        // Arrange, Act
        var account = EntityJson.Read<Account>("""{"Id":{"S":"a"},"Logins":{"N":"3"},"Note":{"S":"n"},"Aliases":{"L":[{"S":"x"},{"S":"y"}]},"Scores":{"L":[{"N":"5"}]}}""");

        // Assert
        await Assert.That((account.Logins, account.Note, account.Aliases.Count, account.Scores[0])).IsEqualTo((3, (string?)"n", 2, 5));
    }

    [Test]
    public async Task ReadAttributes_NullOnNullables_GivesNull()
    {
        // Arrange
        var json = Minimal.Replace("}}", """},"Tracking":{"NULL":true},"ShipTo":{"NULL":true},"Tip":{"NULL":true},"ttl":{"NULL":true}}""", StringComparison.Ordinal);

        // Act
        var purchase = EntityJson.Read<Purchase>(json);

        // Assert
        await Assert.That((purchase.Tracking, purchase.ShipTo, purchase.Tip, purchase.ExpiresAt)).IsEqualTo(((Guid?)null, (Place?)null, (Cents?)null, (DateTimeOffset?)null));
    }

    [Test]
    public async Task ReadAttributes_NullOverAnInitializer_GivesNull()
    {
        // Arrange, Act
        var account = EntityJson.Read<Account>("""{"Id":{"S":"a"},"Note":{"NULL":true}}""");

        // Assert
        await Assert.That(account.Note).IsNull();
    }

    [Test]
    [Arguments("""{"pk":{"NULL":true},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"}}""", "pk")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"NULL":true},"Total":{"N":"3"},"Status":{"S":"Open"}}""", "sk")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"NULL":true}}""", "Status")]
    public async Task ReadAttributes_NullOnNonNullable_ThrowsNamingTheAttribute(string json, string attribute)
    {
        // Arrange, Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName)).IsEqualTo(("Purchase", (string?)attribute));
    }

    [Test]
    [Arguments("""{"pk":{"N":"1"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"}}""", "pk")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"S":"1"},"Total":{"N":"3"},"Status":{"S":"Open"}}""", "sk")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"BOOL":true},"Status":{"S":"Open"}}""", "Total")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"N":"1"}}""", "Status")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"Lines":{"S":"no"}}""", "Lines")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"Tags":{"L":[]}}""", "Tags")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"Rights":{"S":"Read"}}""", "Rights")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"Counters":{"N":"1"}}""", "Counters")]
    [Arguments("""{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"ShipTo":{"S":"no"}}""", "ShipTo")]
    public async Task ReadAttributes_WrongType_ThrowsNamingTheAttribute(string json, string attribute)
    {
        // Arrange, Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That((failure.AttributeName, failure.Message)).IsEqualTo(((string?)attribute, $"The attribute \"{attribute}\" cannot be read as part of Purchase."));
    }

    [Test]
    public async Task ReadAttributes_NumberOutOfRange_IsAMappingFailureThatDoesNotQuoteTheValue()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"sk":{"N":"99999999999"},"Total":{"N":"3"},"Status":{"S":"Open"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That((failure.AttributeName, failure.Message.Contains("99999", StringComparison.Ordinal), failure.InnerException is OverflowException)).IsEqualTo(((string?)"sk", false, true));
    }

    [Test]
    public async Task ReadAttributes_ConverterFailure_NamesTheAttribute()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"Fee":{"S":"abc"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is FormatException)).IsEqualTo(((string?)"Fee", true));
    }

    [Test]
    public async Task ReadAttributes_UnknownEnumName_ThrowsNamingTheAttributeAndNotTheValue()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Secret"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException!.Message)).IsEqualTo(((string?)"Status", "The text is not a member of DynaSharp.UnitTests.Mapping.Status."));
    }

    [Test]
    [Arguments("Open", 0)]
    [Arguments("Paid", 1)]
    [Arguments("Settled", 1)]
    [Arguments("Closed", 2)]
    public async Task ReadAttributes_EnumName_AcceptsEveryName(string name, int expected)
    {
        // Arrange
        var json = Minimal.Replace("Open", name, StringComparison.Ordinal);

        // Act
        var purchase = EntityJson.Read<Purchase>(json);

        // Assert
        await Assert.That((int)purchase.Status).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Opens")]
    [Arguments("open")]
    [Arguments("Clo")]
    public async Task ReadAttributes_EnumNameThatAlmostMatches_Throws(string name)
    {
        // Arrange
        var json = Minimal.Replace("Open", name, StringComparison.Ordinal);

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Status");
    }

    [Test]
    public async Task ReadAttributes_FailureInANestedDocument_ReportsTheInnermost()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"Lines":{"L":[{"M":{"Sku":{"S":"s"},"Quantity":{"S":"no"}}}]}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName)).IsEqualTo(("Line", (string?)"Quantity"));
    }

    [Test]
    public async Task ReadAttributes_MissingInANestedDocument_ReportsTheInnermost()
    {
        // Arrange
        const string Json = """{"pk":{"S":"x"},"sk":{"N":"1"},"Total":{"N":"3"},"Status":{"S":"Open"},"ShipTo":{"M":{"Street":{"S":"s"}}}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName)).IsEqualTo(("Place", (string?)"City"));
    }

    [Test]
    public async Task ReadAttributes_MalformedAttributeValue_IsAMappingFailure()
    {
        // Arrange
        const string Json = """{"pk":{"X":"y"}}""";

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(Json));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("pk");
    }

    [Test]
    public async Task ReadAttributes_BugInAConverter_IsNotWrapped()
    {
        // Arrange (only what a converter throws for a bad value is a mapping failure)
        const string Json = """{"Buggy":{"S":"x"}}""";

        // Act
        var failure = Assert.Throws<NotSupportedException>(() => EntityJson.Read<Faulty>(Json));

        // Assert
        await Assert.That(failure.Message).IsEqualTo("a bug");
    }

    [Test]
    public async Task ReadAttributes_OtherType_ThrowsNamingTheTypeFound()
    {
        // Arrange
        var json = Minimal.Replace("}}", """},"_type":{"S":"INVOICE"}}""", StringComparison.Ordinal);

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName, failure.Message))
            .IsEqualTo(("Purchase", (string?)"_type", "The item is of type \"INVOICE\", so it cannot be read as Purchase."));
    }

    [Test]
    public async Task ReadAttributes_OtherTypeInAnEscapedString_IsNamedUnescaped()
    {
        // Arrange
        var json = Minimal.Replace("}}", """},"_type":{"S":"INVOICE"}}""", StringComparison.Ordinal);

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That(failure.Message).Contains("\"INVOICE\"");
    }

    [Test]
    public async Task ReadAttributes_DiscriminatorEscaped_IsCompared()
    {
        // Arrange
        var json = Minimal.Replace("}}", """},"_type":{"S":"PURCHASE"}}""", StringComparison.Ordinal);

        // Act
        var purchase = EntityJson.Read<Purchase>(json);

        // Assert
        await Assert.That(purchase.Customer).IsEqualTo("bob");
    }

    [Test]
    public async Task ReadAttributes_NoDiscriminator_IsAccepted()
    {
        // Arrange, Act
        var purchase = EntityJson.Read<Purchase>(Minimal);

        // Assert
        await Assert.That(purchase.Customer).IsEqualTo("bob");
    }

    [Test]
    [Arguments("""{"N":"1"}""")]
    [Arguments("""{"NULL":true}""")]
    public async Task ReadAttributes_DiscriminatorOfAnotherAttributeType_ThrowsNamingTheAttribute(string value)
    {
        // Arrange
        var json = Minimal.Replace("}}", $$"""},"_type":{{value}}}""", StringComparison.Ordinal);

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task ReadAttributes_OwnDiscriminatorAttribute_IsChecked()
    {
        // Arrange, Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Account>("""{"Id":{"S":"a"},"kind":{"S":"OTHER"}}"""));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("kind");
    }

    [Test]
    public async Task ReadAttributes_OptOut_IgnoresAnAttributeOfThatName()
    {
        // Arrange, Act
        var foreign = EntityJson.Read<Foreign>("""{"id":{"S":"a"},"_type":{"S":"ANYTHING"}}""");

        // Assert
        await Assert.That(foreign.Id).IsEqualTo("a");
    }

    [Test]
    public async Task ReadAttributes_PositionalAllPresent_UsesEveryValue()
    {
        // Arrange
        const string Json = """{"Id":{"S":"i"},"Name":{"S":"n"},"Age":{"N":"30"},"Limit":{"N":"9.5"},"Motto":{"S":"m"},"Stage":{"S":"Open"},"Nick":{"S":"k"},"Extra":{"S":"e"}}""";

        // Act
        var member = EntityJson.Read<Member>(Json);

        // Assert
        await Assert.That(member).IsEqualTo(new Member("i", "n", 30, 9.5m, "m", Status.Open, "k") { Extra = "e" });
    }

    [Test]
    public async Task ReadAttributes_PositionalMissing_UsesTheDefaultsOfTheConstructor()
    {
        // Arrange, Act
        var member = EntityJson.Read<Member>("""{"Id":{"S":"i"},"Name":{"S":"n"}}""");

        // Assert
        await Assert.That(member).IsEqualTo(new Member("i", "n", 18, 1.5m, "hello \"world\"", Status.Closed, null) { Extra = "initial" });
    }

    [Test]
    public async Task ReadAttributes_ParameterWithoutDefaultMissing_Throws()
    {
        // Arrange, Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Member>("""{"Id":{"S":"i"}}"""));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName)).IsEqualTo(("Member", (string?)"Name"));
    }

    [Test]
    public async Task ReadAttributes_PositionalNullableWithoutDefault_IsNullWhenMissing()
    {
        // Arrange, Act
        var place = EntityJson.Read<Place>("""{"Street":{"S":"s"},"City":{"S":"c"}}""");

        // Assert
        await Assert.That(place).IsEqualTo(new Place("s", "c", null));
    }

    [Test]
    public async Task ReadAttributes_StructWithInitializers_KeepsThemWhenMissing()
    {
        // Arrange, Act
        var retry = EntityJson.Read<Retry>("{}");

        // Assert
        await Assert.That((retry.Count, retry.Mode)).IsEqualTo((3, (string?)"backoff"));
    }

    [Test]
    public async Task ReadAttributes_StructWithInitializers_TakesWhatIsThere()
    {
        // Arrange, Act
        var retry = EntityJson.Read<Retry>("""{"Count":{"N":"5"},"Mode":{"NULL":true}}""");

        // Assert
        await Assert.That((retry.Count, retry.Mode)).IsEqualTo((5, (string?)null));
    }

    [Test]
    public async Task ReadAttributes_StructWithItsOwnConstructor_RunsIt()
    {
        // Arrange, Act
        var window = EntityJson.Read<Window>("""{"Length":{"N":"5"}}""");

        // Assert
        await Assert.That((window.Unit, window.Length)).IsEqualTo(("seconds", 5));
    }

    [Test]
    public async Task ReadAttributes_RecordStruct_UsesTheConstructorAndItsDefault()
    {
        // Arrange, Act
        var span = EntityJson.Read<Span>("""{"Start":{"N":"1"}}""");

        // Assert
        await Assert.That(span).IsEqualTo(new Span(1, 10));
    }

    [Test]
    public async Task ReadAttributes_MissingAttributesWithDefaultsNarrowerThanInt_TakeTheDefaults()
    {
        // Arrange, Act
        var tally = EntityJson.Read<Tally>("""{"Name":{"S":"n"}}""");

        // Assert
        await Assert.That(tally).IsEqualTo(new Tally("n"));
    }

    [Test]
    public async Task ReadAttributes_RecordStructWithAPropertyOutsideItsConstructor_ChainsToTheConstructor()
    {
        // Arrange, Act
        var labeled = EntityJson.Read<Labeled>("""{"Id":{"N":"1"},"Label":{"S":"l"}}""");

        // Assert
        await Assert.That((labeled.Id, labeled.Label, labeled.Default)).IsEqualTo((1, (string?)"l", "kept"));
    }

    [Test]
    public async Task ReadAttributes_RequiredPresent_IsSet()
    {
        // Arrange, Act
        var strict = EntityJson.Read<Strict>("""{"Name":{"S":"n"},"Maybe":{"S":"m"},"Must":{"SS":["a"]}}""");

        // Assert
        await Assert.That((strict.Name, strict.Maybe, strict.Must.Single())).IsEqualTo(("n", (string?)"m", "a"));
    }

    [Test]
    public async Task ReadAttributes_RequiredNullableMissing_KeepsTheCurrentValue()
    {
        // Arrange (null is left out on write, so a missing value must not throw)
        // Act
        var strict = EntityJson.Read<Strict>("""{"Name":{"S":"n"}}""");

        // Assert
        await Assert.That(strict.Maybe).IsEqualTo("kept");
    }

    [Test]
    public async Task ReadAttributes_RequiredSetMissing_IsEmpty()
    {
        // Arrange (DynamoDB cannot store an empty set, so missing means empty and the round trip holds)
        // Act
        var strict = EntityJson.Read<Strict>("""{"Name":{"S":"n"}}""");

        // Assert
        await Assert.That(strict.Must).IsEmpty();
    }

    [Test]
    public async Task ReadAttributes_RequiredMemberBoundToTheConstructor_IsRead()
    {
        // Arrange, Act
        var bound = EntityJson.Read<Bound>("""{"Name":{"S":"n"}}""");

        // Assert
        await Assert.That(bound.Name).IsEqualTo("n");
    }

    [Test]
    public async Task ReadAttributes_Recursion_ReadsEveryLevel()
    {
        // Arrange
        const string Json = """{"Name":{"S":"root"},"Children":{"L":[{"M":{"Name":{"S":"a"},"Parent":{"M":{"Name":{"S":"up"}}}}},{"M":{"Children":{"L":[{"M":{"Name":{"S":"deep"}}}]}}}]}}""";

        // Act
        var tree = EntityJson.Read<Tree>(Json);

        // Assert
        await Assert.That((tree.Name, tree.Children[0].Parent!.Name, tree.Children[1].Name, tree.Children[1].Children[0].Name)).IsEqualTo(("root", "up", string.Empty, "deep"));
    }

    [Test]
    public async Task ReadAttributes_NothingMapped_SkipsEverything()
    {
        // Arrange, Act
        var empty = EntityJson.Read<Empty>("""{"a":{"S":"b"}}""");

        // Assert
        await Assert.That(empty).IsNotNull();
    }

    [Test]
    public async Task ReadAttributes_MalformedItemOfAnEmptyDocument_IsAMappingFailureWithoutAnAttribute()
    {
        // Arrange, Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Empty>("""{"a":[]}"""));

        // Assert
        await Assert.That((failure.EntityType, failure.AttributeName)).IsEqualTo(("Empty", (string?)null));
    }

    [Test]
    public async Task ReadAttributes_BaseAndDerived_ReadTheirOwnDiscriminators()
    {
        // Arrange
        const string Json = """{"_type":{"S":"DERIVED"},"Id":{"S":"d"},"Shared":{"S":"s"},"Extra":{"N":"2"}}""";

        // Act
        var derived = EntityJson.Read<DerivedThing>(Json);
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<BaseThing>(Json));

        // Assert
        await Assert.That((derived.Id, derived.Shared, derived.Extra, failure.EntityType)).IsEqualTo(("d", "s", 2, "BaseThing"));
    }

    [Test]
    public async Task ReadAttributes_EverySetShape_Reads()
    {
        // Arrange
        const string Json = """{"Id":{"S":"s"},"Rack":{"N":"3"},"Flags":{"NS":["3","128"]},"Numbers":{"NS":["4"]},"Blobs":{"BS":["AQ=="]},"Ids":{"SS":["0f8fad5b-d9cb-469f-a165-70867728950e"]},"Names":{"SS":["n"]},"Optional":{"SS":["o"]}}""";

        // Act
        var shelf = EntityJson.Read<Shelf>(Json);

        // Assert
        await Assert.That((shelf.Rack, shelf.Flags.Contains(Access.Read | Access.Write), shelf.Numbers.Single(), shelf.Blobs.Single()[0], shelf.Ids.Count, shelf.Names.Single(), shelf.Optional!.Single()))
            .IsEqualTo((3, true, 4, (byte)1, 1, "n", "o"));
    }

    [Test]
    public async Task ReadAttributes_SetsMissing_AreEmptyAndAnOptionalOneStaysNull()
    {
        // Arrange, Act
        var shelf = EntityJson.Read<Shelf>("""{"Id":{"S":"s"},"Rack":{"N":"3"}}""");

        // Assert
        await Assert.That((shelf.Flags.Count, shelf.Numbers.Count, shelf.Blobs.Count, shelf.Ids.Count, shelf.Names.Length, shelf.Optional)).IsEqualTo((0, 0, 0, 0, 0, (HashSet<string>?)null));
    }

    [Test]
    public async Task ReadAttributes_SetOfEnumsWithAnUnknownName_ThrowsNamingTheSet()
    {
        // Arrange
        var json = Minimal.Replace("}}", """},"Stages":{"SS":["Nope"]}}""", StringComparison.Ordinal);

        // Act
        var failure = Assert.Throws<DynamoDbMappingException>(() => EntityJson.Read<Purchase>(json));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Stages");
    }

    private static string Without(string json, string attribute)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var kept = document.RootElement.EnumerateObject().Where(property => property.Name != attribute).Select(static property => property.ToString());
        return "{" + string.Join(',', kept) + "}";
    }
}
