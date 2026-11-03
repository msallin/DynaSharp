using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> does to the generated converter of an enum: a name the enum does not have reads as if the attribute were not in the item, which is
/// what <see cref="CountsAsMissingTests"/> holds for a converter of one's own. The use case is a rollout, where a newer version of a service stores a member that the older one reading the table does not know yet.
/// The items are written by hand from the model of <c>api.md</c>, and handles sit in variables, because <c>DYNA209</c> reports a <c>Select</c> of them where it sees one.
/// </summary>
public sealed class LenientEnumTests
{
    [Test]
    [Arguments("Wind", "Gale", "Gale")]
    [Arguments("Wind", "TORNADO", "Unknown")]
    [Arguments("Wind", "gale", "Unknown")]
    [Arguments("Wind", "", "Unknown")]
    [Arguments("Bare", "Breeze", "Breeze")]
    [Arguments("Bare", "TORNADO", "Calm")]
    [Arguments("Bare", "", "Calm")]
    public async Task Read_StoredText_ReadsTheNameOrKeepsTheInitialValue(string attribute, string text, string expected)
    {
        // Arrange
        var json = "{\"Id\":{\"S\":\"l1\"},\"" + attribute + "\":{\"S\":\"" + text + "\"}}";

        // Act
        var logbook = EntityJson.Read<Logbook>(json);

        // Assert
        await Assert.That((attribute == "Wind" ? logbook.Wind : logbook.Bare).ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("TORNADO")]
    [Arguments("")]
    public async Task Read_TextThatIsNoNameOnANullableProperty_ReadsNull(string text)
    {
        // Arrange
        var json = "{\"Id\":{\"S\":\"l1\"},\"MaybeWind\":{\"S\":\"" + text + "\"}}";

        // Act
        var logbook = EntityJson.Read<Logbook>(json);

        // Assert
        await Assert.That(logbook.MaybeWind).IsNull();
    }

    [Test]
    public async Task Read_NameOnANullableProperty_ReadsIt()
    {
        // Arrange
        const string Json = """{"Id":{"S":"l1"},"MaybeWind":{"S":"Breeze"}}""";

        // Act
        var logbook = EntityJson.Read<Logbook>(Json);

        // Assert
        await Assert.That(logbook.MaybeWind).IsEqualTo(Wind.Breeze);
    }

    [Test]
    public async Task Read_TextThatIsNoNameOnANullablePropertyWithAnInitializer_KeepsTheInitializer()
    {
        // Arrange (as if the attribute were not in the item, and a missing attribute leaves an initializer alone)
        const string Json = """{"Id":{"S":"l1"},"SeededWind":{"S":"TORNADO"}}""";

        // Act
        var logbook = EntityJson.Read<Logbook>(Json);

        // Assert
        await Assert.That(logbook.SeededWind).IsEqualTo(Wind.Unknown);
    }

    [Test]
    public async Task Read_NullOnANullableProperty_ReadsNullWithoutAsking()
    {
        // Arrange
        const string Json = """{"Id":{"S":"l1"},"SeededWind":{"NULL":true}}""";

        // Act
        var logbook = EntityJson.Read<Logbook>(Json);

        // Assert
        await Assert.That(logbook.SeededWind).IsNull();
    }

    [Test]
    [Arguments("""{"N":"1"}""")]
    [Arguments("""{"NULL":true}""")]
    [Arguments("""{"SS":["Gale"]}""")]
    public async Task Read_ValueThatIsNoString_ThrowsNamingTheAttribute(string value)
    {
        // Arrange (only a string can be a name the enum does not have, anything else is no name at all)
        var json = "{\"Id\":{\"S\":\"l1\"},\"Wind\":" + value + "}";

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(EntityJson.Read<Logbook>(json)));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Wind");
    }

    [Test]
    public async Task Read_TextThatIsNoNameOnARequiredMember_ThrowsNamingIt()
    {
        // Arrange
        const string Json = """{"Id":{"S":"s1"},"Wind":{"S":"TORNADO"}}""";

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(EntityJson.Read<Squall>(Json)));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Wind");
    }

    [Test]
    public async Task Read_TextThatIsNoNameOnAnInputOfTheKey_RestoresItFromTheKey()
    {
        // Arrange
        const string Json = """{"pk":{"S":"GUST#Gale"},"Wind":{"S":"TORNADO"}}""";

        // Act
        var gust = EntityJson.Read<Gust>(Json);

        // Assert
        await Assert.That(gust.Wind).IsEqualTo(Wind.Gale);
    }

    [Test]
    public async Task Read_TextThatIsNoNameOnAPlainKey_ThrowsWhereAPropertyWouldKeepItsInitialValue()
    {
        // Arrange (the initial value of a key is another item)
        const string Json = """{"Direction":{"S":"TORNADO"}}""";

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(EntityJson.Read<Vane>(Json)));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is FormatException)).IsEqualTo(("Direction", true));
    }

    [Test]
    [Arguments("Winds", """{"L":[{"S":"Gale"},{"S":"TORNADO"}]}""")]
    [Arguments("Seen", """{"SS":["Gale","TORNADO"]}""")]
    [Arguments("ByHour", """{"M":{"h1":{"S":"Gale"},"h2":{"S":"TORNADO"}}}""")]
    public async Task Read_TextThatIsNoNameInAnElement_ThrowsBecauseAnElementIsNotAsked(string attribute, string value)
    {
        // Arrange (dropping the element would give a shorter collection, which the next Put writes back)
        var json = "{\"Id\":{\"S\":\"l1\"},\"" + attribute + "\":" + value + "}";

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(EntityJson.Read<Logbook>(json)));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is FormatException)).IsEqualTo((attribute, true));
    }

    [Test]
    public async Task Read_NamesInElements_ReadsThem()
    {
        // Arrange
        const string Json = """{"Id":{"S":"l1"},"Winds":{"L":[{"S":"Gale"},{"S":"Calm"}]},"Seen":{"SS":["Breeze"]},"ByHour":{"M":{"h1":{"S":"Gale"}}}}""";

        // Act
        var logbook = EntityJson.Read<Logbook>(Json);

        // Assert
        await Assert.That((string.Join(",", logbook.Winds), logbook.Seen.Single(), logbook.ByHour["h1"])).IsEqualTo(("Gale,Calm", Wind.Breeze, Wind.Gale));
    }

    [Test]
    public async Task Read_DocumentBoundThroughItsConstructor_KeepsTheDefaultOfAParameter()
    {
        // Arrange
        const string Json = """{"Id":{"S":"l1"},"Sail":{"M":{"Trim":{"S":"Breeze"},"Reef":{"S":"TORNADO"}}}}""";

        // Act
        var logbook = EntityJson.Read<Logbook>(Json);

        // Assert
        await Assert.That(logbook.Sail).IsEqualTo(new Sail(Wind.Breeze));
    }

    [Test]
    public async Task Read_DocumentBoundThroughItsConstructor_ThrowsForAParameterWithoutADefault()
    {
        // Arrange
        const string Json = """{"Id":{"S":"l1"},"Sail":{"M":{"Trim":{"S":"TORNADO"}}}}""";

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(EntityJson.Read<Logbook>(Json)));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Trim");
    }

    [Test]
    [Arguments("""{"Id":{"S":"l1"},"Wind":{"S":"TORNADO"},"After":{"N":"7"}}""")]
    [Arguments("""{"After":{"N":"7"},"Id":{"S":"l1"},"Wind":{"S":"TORNADO"}}""")]
    public async Task Read_TextCountedAsMissing_IsConsumedSoThatTheNextAttributeReads(string json)
    {
        // Arrange, Act
        var logbook = EntityJson.Read<Logbook>(json);

        // Assert
        await Assert.That((logbook.Wind, logbook.After)).IsEqualTo((Wind.Unknown, 7));
    }

    [Test]
    public async Task Write_Entity_StoresTheNamesAsAnEnumWithoutTheAttribute()
    {
        // Arrange
        var logbook = new Logbook { Id = "l1", Wind = Wind.Gale, Bare = Wind.Calm, MaybeWind = Wind.Breeze, SeededWind = null, Winds = [Wind.Calm], Seen = [Wind.Gale], ByHour = new() { ["h1"] = Wind.Breeze } };

        // Act
        var json = EntityJson.Write(logbook);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"_type":{"S":"LOGBOOK"},"Id":{"S":"l1"},"Wind":{"S":"Gale"},"Bare":{"S":"Calm"},"MaybeWind":{"S":"Breeze"},"Winds":{"L":[{"S":"Calm"}]},"Seen":{"SS":["Gale"]},"ByHour":{"M":{"h1":{"S":"Breeze"}}},"After":{"N":"0"}}""");
    }

    [Test]
    public async Task SendAsync_PageWithANameTheEnumLacks_ReadsEveryItem()
    {
        // Arrange (one item from a newer version of the service, beside one this version knows)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Items":[{"Id":{"S":"l1"},"_type":{"S":"LOGBOOK"},"Wind":{"S":"Gale"}},{"Id":{"S":"l1"},"_type":{"S":"LOGBOOK"},"Wind":{"S":"HURRICANE"}}],"Count":2,"ScannedCount":2}"""));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table<Logbook>("Shop").Scan().ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static logbook => logbook.Wind))).IsEqualTo("Gale,Unknown");
    }

    [Test]
    public async Task SendAsync_SelectOfATextThatIsNoNameOnANullableProperty_IsFoundWithNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"MaybeWind":{"S":"TORNADO"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var wind = await client.Table<Logbook>("Shop").Get(Logbook.Keys.Of("l1")).Select(Logbook.Attrs.MaybeWind).SendAsync();

        // Assert
        await Assert.That((wind.Found, wind.Value)).IsEqualTo((true, (Wind?)null));
    }

    [Test]
    public async Task SendAsync_SelectOfATextThatIsNoName_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Wind":{"S":"TORNADO"}}}"""));
        using var client = TestClient.Create(handler);
        var wind = Logbook.Attrs.Wind;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Logbook>("Shop").Get(Logbook.Keys.Of("l1")).Select(wind).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Wind");
    }

    [Test]
    public async Task SendAsync_SelectOfAName_GivesTheValue()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Wind":{"S":"Breeze"}}}"""));
        using var client = TestClient.Create(handler);
        var wind = Logbook.Attrs.Wind;

        // Act
        var value = await client.Table<Logbook>("Shop").Get(Logbook.Keys.Of("l1")).Select(wind).SendAsync();

        // Assert
        await Assert.That(value.Value).IsEqualTo(Wind.Breeze);
    }

    [Test]
    public async Task SendAsync_ReturnOfATextThatIsNoName_ThrowsAsForAMissingAttribute()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Attributes":{"MaybeWind":{"S":"TORNADO"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () =>
            await client.Table<Logbook>("Shop").Update(Logbook.Keys.Of("l1")).Add(Logbook.Attrs.After, 1).Return(Logbook.Attrs.MaybeWind).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("MaybeWind");
    }
}
