using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Generators;
using DynaSharp.UnitTests.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What <see cref="IDynamoDbConverter{T}.CountsAsMissing"/> of a converter of your own does to a read: as if the attribute were not in the item. The items are written by hand from the model of <c>api.md</c>,
/// and handles sit in variables, because <c>DYNA209</c> reports a <c>Select</c> of them where it sees one.
/// </summary>
public sealed class CountsAsMissingTests
{
    [Test]
    public async Task To_BlankStringOnANullableProperty_ReadsNull()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"Payload":{"S":""}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That(mooring.Payload).IsNull();
    }

    [Test]
    public async Task To_TextOnANullableProperty_ReadsTheValue()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"Payload":{"S":"text"}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That(mooring.Payload).IsEqualTo(new Payload("text"));
    }

    [Test]
    public async Task SendAsync_BlankStringOnANullableProperty_ReadsNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Id":{"S":"m1"},"Payload":{"S":""}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var mooring = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).ItemAsync();

        // Assert
        await Assert.That(mooring!.Payload).IsNull();
    }

    [Test]
    public async Task To_BlankStringOnANullablePropertyWithAnInitializer_KeepsTheInitializer()
    {
        // Arrange (as if the attribute were not in the item, and a missing attribute leaves an initializer alone)
        var item = ReadItem("""{"Id":{"S":"m1"},"Seeded":{"S":""}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That(mooring.Seeded).IsEqualTo(new Payload("seed"));
    }

    [Test]
    public async Task SendAsync_SelectOfABlankStringWithAnInitializer_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Seeded":{"S":""}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var seeded = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(Mooring.Attrs.Seeded).SendAsync();

        // Assert
        await Assert.That((seeded.Found, seeded.Value)).IsEqualTo((true, (Payload?)null));
    }

    [Test]
    public async Task SendAsync_SelectOfABlankStringOnANullableProperty_IsFoundWithNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Payload":{"S":""}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var payload = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(Mooring.Attrs.Payload).SendAsync();

        // Assert
        await Assert.That((payload.Found, payload.Value)).IsEqualTo((true, (Payload?)null));
    }

    [Test]
    [Arguments("Position", "Bow", "Bow")]
    [Arguments("Position", "TELEPORTED", "Unknown")]
    [Arguments("Pinned", "Stern", "Stern")]
    [Arguments("Pinned", "TELEPORTED", "Unknown")]
    [Arguments("Shared", "Bow", "Bow")]
    [Arguments("Shared", "TELEPORTED", "Unknown")]
    public async Task To_EnumNameOnAPropertyWithAnInitializer_ReadsItOrKeepsTheInitializer(string attribute, string name, string expected)
    {
        // Arrange
        var item = ReadItem("{\"Id\":{\"S\":\"m1\"},\"" + attribute + "\":{\"S\":\"" + name + "\"}}");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That((attribute switch { "Position" => mooring.Position, "Pinned" => mooring.Pinned, _ => mooring.Shared }).ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task To_NameTheTypeLacksOnANullableValue_ReadsNull()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"MaybePosition":{"S":"TELEPORTED"},"MaybePinned":{"S":"TELEPORTED"}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That((mooring.MaybePosition, mooring.MaybePinned)).IsEqualTo(((Position?)null, (Position?)null));
    }

    [Test]
    public async Task To_NullOnANullableValue_ReadsNullWithoutAsking()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"MaybePosition":{"NULL":true},"Payload":{"NULL":true}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That((mooring.MaybePosition, mooring.Payload)).IsEqualTo(((Position?)null, (Payload?)null));
    }

    [Test]
    public async Task To_NameTheTypeLacksOnARequiredMember_ThrowsNamingIt()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"h1"},"Position":{"S":"TELEPORTED"}}""");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Hull>()));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Position");
    }

    [Test]
    public async Task To_NameTheTypeLacksOnAnInputOfTheKey_RestoresItFromTheKey()
    {
        // Arrange
        var item = ReadItem("""{"pk":{"S":"BERTH#Bow"},"Side":{"S":"TELEPORTED"}}""");

        // Act
        var berth = item.To<Berth>();

        // Assert
        await Assert.That(berth.Side).IsEqualTo(Position.Bow);
    }

    [Test]
    public async Task To_NameTheTypeLacksOnAPlainKey_ThrowsWhereAPropertyWouldKeepItsInitialValue()
    {
        // Arrange (the initial value of a key is another item)
        var item = ReadItem("""{"Side":{"S":"TELEPORTED"}}""");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Cleat>()));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is FormatException)).IsEqualTo(("Side", true));
    }

    [Test]
    public async Task To_DocumentBoundThroughItsConstructor_KeepsTheDefaultOfAParameter()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"Rig":{"M":{"Keel":{"S":"Bow"},"Mast":{"S":"TELEPORTED"}}}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That(mooring.Rig).IsEqualTo(new Rigging(Position.Bow));
    }

    [Test]
    public async Task To_DocumentBoundThroughItsConstructor_ThrowsForAParameterWithoutADefault()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"Rig":{"M":{"Keel":{"S":"TELEPORTED"}}}}""");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Mooring>()));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Keel");
    }

    [Test]
    public async Task To_ConverterOnTheType_IsAskedForAProperty()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"Tide":{"S":"TELEPORTED"}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That(mooring.Tide).IsEqualTo(Tide.High);
    }

    [Test]
    [Arguments("Tides", """{"L":[{"S":"TELEPORTED"}]}""")]
    [Arguments("Seen", """{"SS":["TELEPORTED"]}""")]
    public async Task To_NameTheTypeLacksInAnElement_ThrowsBecauseAnElementIsNotAsked(string attribute, string value)
    {
        // Arrange
        var item = ReadItem("{\"Id\":{\"S\":\"m1\"},\"" + attribute + "\":" + value + "}");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Mooring>()));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo(attribute);
    }

    [Test]
    [Arguments("""{"Id":{"S":"m1"},"Payload":{"S":""},"After":{"N":"7"}}""")]
    [Arguments("""{"After":{"N":"7"},"Id":{"S":"m1"},"Payload":{"S":""}}""")]
    [Arguments("""{"Id":{"S":"m1"},"Mail":{"M":{"sender":{"S":"a"},"body":{"S":""}}},"After":{"N":"7"}}""")]
    public async Task To_ValueCountedAsMissing_IsConsumedSoThatTheNextAttributeReads(string json)
    {
        // Arrange
        var item = ReadItem(json);

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That(mooring.After).IsEqualTo(7);
    }

    [Test]
    public async Task To_MapWhoseBodyIsBlank_LooksInsideACopyAndSkipsTheMap()
    {
        // Arrange (a map that a converter counts as missing by what it holds, past a member before the one it looks for)
        var item = ReadItem("""{"Id":{"S":"m1"},"Mail":{"M":{"sender":{"S":"a"},"body":{"S":""}}},"After":{"N":"7"}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That((mooring.Mail, mooring.After)).IsEqualTo(((Envelope?)null, 7));
    }

    [Test]
    public async Task To_MapWithABody_ReadsAllOfIt()
    {
        // Arrange (the walk went over a copy, so Read starts at the first member of the map)
        var item = ReadItem("""{"Id":{"S":"m1"},"Mail":{"M":{"sender":{"S":"a"},"body":{"S":"hello"}}},"After":{"N":"7"}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That((mooring.Mail, mooring.After)).IsEqualTo((new Envelope("a", "hello"), 7));
    }

    [Test]
    public async Task SendAsync_SelectOfAMapWhoseBodyIsBlank_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Mail":{"M":{"sender":{"S":"a"},"body":{"S":""}}},"After":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var mail = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(Mooring.Attrs.Mail).SendAsync();

        // Assert
        await Assert.That((mail.Found, mail.Value)).IsEqualTo((true, (Envelope?)null));
    }

    [Test]
    public async Task SendAsync_SelectOfANameTheTypeLacks_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Position":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);
        var position = Mooring.Attrs.Position;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(position).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Position");
    }

    [Test]
    public async Task SendAsync_SelectOfANameTheTypeHas_GivesTheValue()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Pinned":{"S":"Stern"}}}"""));
        using var client = TestClient.Create(handler);
        var pinned = Mooring.Attrs.Pinned;

        // Act
        var value = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(pinned).SendAsync();

        // Assert
        await Assert.That(value.Value).IsEqualTo(Position.Stern);
    }

    [Test]
    public async Task SendAsync_SelectOfANameTheTypeLacksOnANullableValue_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"MaybePinned":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var value = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(Mooring.Attrs.MaybePinned).SendAsync();

        // Assert
        await Assert.That((value.Found, value.Value)).IsEqualTo((true, (Position?)null));
    }

    [Test]
    public async Task SendAsync_SelectOfAValueItsConverterAnswersFor_GivesTheAnswerOfTheConverter()
    {
        // Arrange (the converter says what a missing attribute reads as, so this has no reason to throw)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Answering":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var value = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(Mooring.Attrs.Answering).SendAsync();

        // Assert
        await Assert.That(value.Value).IsEqualTo(Position.Unknown);
    }

    [Test]
    [Arguments("""{"Attributes":{"Position":{"S":"TELEPORTED"}}}""")]
    [Arguments("""{"Attributes":{"After":{"N":"1"}}}""")]
    public async Task SendAsync_ReturnOfAValueCountedAsMissing_ThrowsAsForAMissingAttribute(string response)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(response));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () =>
            await client.Table<Mooring>("Shop").Update(Mooring.Keys.Of("m1")).Add(Mooring.Attrs.After, 1).Return(Mooring.Attrs.Position).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Position");
    }

    [Test]
    public async Task SendAsync_ReturnOfABlankStringOnANullableHandle_ThrowsNeverADefault()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Attributes":{"Payload":{"S":""}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () =>
            await client.Table<Mooring>("Shop").Update(Mooring.Keys.Of("m1")).Add(Mooring.Attrs.After, 1).Return(Mooring.Attrs.Payload).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Payload");
    }

    [Test]
    public async Task SendAsync_ReturnOfANameTheTypeLacksOnANullableValueHandle_ThrowsNeverADefault()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Attributes":{"MaybePosition":{"S":"TELEPORTED"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () =>
            await client.Table<Mooring>("Shop").Update(Mooring.Keys.Of("m1")).Add(Mooring.Attrs.After, 1).Return(Mooring.Attrs.MaybePosition).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("MaybePosition");
    }

    [Test]
    public async Task SendAsync_ReturnOfANameTheTypeHas_GivesTheValue()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Attributes":{"Position":{"S":"Stern"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var position = await client.Table<Mooring>("Shop").Update(Mooring.Keys.Of("m1")).Add(Mooring.Attrs.After, 1).Return(Mooring.Attrs.Position).SendAsync();

        // Assert
        await Assert.That(position).IsEqualTo(Position.Stern);
    }

    [Test]
    public async Task To_OmitEmptyString_AsksItsConverterAndKeepsAStoredEmptyString()
    {
        // Arrange (OmitEmpty passes the question on and has no answer of its own)
        var item = ReadItem("""{"Id":{"S":"m1"},"Text":{"S":"-"},"MaybeText":{"S":""}}""");

        // Act
        var mooring = item.To<Mooring>();

        // Assert
        await Assert.That((mooring.Text, mooring.MaybeText)).IsEqualTo(("initial", (string?)string.Empty));
    }

    [Test]
    public async Task SendAsync_SelectOfAnOmitEmptyStringOnANullableHandle_GivesNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"MaybeText":{"S":"-"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var text = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(Mooring.Attrs.MaybeText).SendAsync();

        // Assert
        await Assert.That((text.Found, text.Value)).IsEqualTo((true, (string?)null));
    }

    [Test]
    public async Task SendAsync_SelectThroughOmitEmptyString_AsksItsConverter()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Text":{"S":"-"}}}"""));
        using var client = TestClient.Create(handler);
        var text = Mooring.Attrs.Text;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(text).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Text");
    }

    [Test]
    public async Task SendAsync_SelectThroughOmitEmptyList_AsksItsConverter()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Lines":{"S":"torn"}}}"""));
        using var client = TestClient.Create(handler);
        var lines = Mooring.Attrs.Lines;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(lines).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Lines");
    }

    [Test]
    public async Task SendAsync_SelectThroughOmitEmptySet_AsksItsConverter()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Labels":{"S":"torn"}}}"""));
        using var client = TestClient.Create(handler);
        var labels = Mooring.Attrs.Labels;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(labels).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("Labels");
    }

    [Test]
    public async Task SendAsync_SelectThroughOmitEmptySetThatHoldsASet_GivesTheValue()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Labels":{"SS":["a"]}}}"""));
        using var client = TestClient.Create(handler);
        var labels = Mooring.Attrs.Labels;

        // Act
        var value = await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(labels).SendAsync();

        // Assert
        await Assert.That(value.Value.Single()).IsEqualTo("a");
    }

    [Test]
    public async Task CountsAsMissing_ConverterThatTriesToMoveTheReader_LeavesItOnTheValue()
    {
        // Arrange, Act (ReadEndAttribute and TryReadAttributeName each move a copy, so the next attribute is where it was)
        var seen = TestJson.Read("""{"a":{"S":"kept"},"b":{"N":"1"}}""", static (ref reader) =>
        {
            reader.ReadStartItem();
            _ = reader.TryReadAttributeName();
            var found = reader.TryReadAttribute<string, MovingConverter>(out var value);
            _ = reader.TryReadAttributeName();
            return (found, value, reader.GetAttributeName());
        });

        // Assert
        await Assert.That(seen).IsEqualTo((true, (string?)"kept", "b"));
    }

    [Test]
    public async Task CountsAsMissing_ConverterThatAssignsOrPassesOnTheReader_DoesNotCompile()
    {
        // Arrange (the moves that a copy would not catch, because they reach the variable and not a copy of it)
        const string Source = """
            using DynaSharp;
            using DynaSharp.Serialization;

            public readonly struct Reassigning : IDynamoDbConverter<string>
            {
                public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

                public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();

                public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type)
                {
                    reader = default;
                    _ = StringConverter.Read(ref reader, type);
                    return false;
                }
            }
            """;

        // Act
        var errors = GeneratorRun.CreateCompilation(Source, LanguageVersion.Latest).GetDiagnostics()
            .Where(static problem => problem.Severity == DiagnosticSeverity.Error)
            .Select(static problem => problem.Id)
            .Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(string.Join(",", errors)).IsEqualTo("CS8329,CS8331");
    }

    [Test]
    public async Task TryReadAttribute_ConverterWithoutAnAnswer_ReadsTheValue()
    {
        // Arrange, Act (the default of the interface counts nothing, so a blank string is a value here)
        var value = TestJson.Read("""{"S":""}""", static (ref reader) => (reader.TryReadAttribute<string, StringConverter>(out var read), read));

        // Assert
        await Assert.That(value).IsEqualTo((true, (string?)string.Empty));
    }

    [Test]
    public async Task To_ConverterThatFailsWhenAsked_NamesTheAttribute()
    {
        // Arrange
        var item = ReadItem("""{"Id":{"S":"m1"},"Refused":{"N":"1"}}""");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(item.To<Mooring>()));

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is FormatException)).IsEqualTo(("Refused", true));
    }

    [Test]
    public async Task SendAsync_SelectOfAConverterThatFailsWhenAsked_NamesTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"Refused":{"N":"1"}}}"""));
        using var client = TestClient.Create(handler);
        var refused = Mooring.Attrs.Refused;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Mooring>("Shop").Get(Mooring.Keys.Of("m1")).Select(refused).SendAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is FormatException)).IsEqualTo(("Refused", true));
    }

    [Test]
    public async Task TryReadAttribute_NullOnAWrapperOfNull_AnswersWithoutAskingItsConverter()
    {
        // Arrange, Act (the converter fails whenever it is asked)
        var value = TestJson.Read("""{"NULL":true}""", static (ref reader) => (reader.TryReadAttribute<int?, NullableConverter<int, RefusingConverter>>(out var read), read));

        // Assert
        await Assert.That(value).IsEqualTo((true, (int?)null));
    }

    [Test]
    public async Task TryReadAttribute_NullOnAWrapperOfNullOfAReference_AnswersWithoutAskingItsConverter()
    {
        // Arrange, Act
        var value = TestJson.Read("""{"NULL":true}""", static (ref reader) => (reader.TryReadAttribute<string?, NullableReferenceConverter<string, RefusingTextConverter>>(out var read), read));

        // Assert
        await Assert.That(value).IsEqualTo((true, (string?)null));
    }

    private static Item ReadItem(string json) => (Item)TestJson.ReadValue("{\"M\":" + json + "}").AsMap();
}
