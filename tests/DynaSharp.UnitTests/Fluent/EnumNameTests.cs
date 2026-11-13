using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.EnumNames;
using DynaSharp.UnitTests.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What <c>[DynamoDbEnumName]</c> changes: every place the generated converter writes or reads an enum by name takes the stored name, an attribute, a set, a key and its <c>Parse</c>, a condition and a
/// key condition, and only the stored name reads. A placeholder with <c>d&lt;w&gt;</c> writes the value. Aliases write the stored name of the first member and read each one's. The enums are internal,
/// so the cases pass numbers.
/// </summary>
public sealed class EnumNameTests
{
    private static readonly YardMessage Message = new()
    {
        Owner = MessageOwner.LoadingUnit,
        OwnerKey = "4711",
        Kind = MessageOwner.Topic,
        Id = "m1",
        Priority = Priority.Minor,
        Seen = [Priority.High],
    };

    [Test]
    public async Task Write_MemberWithAStoredName_StoresThatNameInTheAttributeAndTheSetAndAnAliasTheFirstOne()
    {
        // Act
        var json = EntityJson.Write(Message);

        // Assert
        await Assert.That((json.Contains("\"Priority\":{\"S\":\"LOW\"}", StringComparison.Ordinal), json.Contains("\"Seen\":{\"SS\":[\"HIGH\"]}", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Write_KeyOfAMemberWithAStoredName_HoldsThatNameAndTheValueUnderDigits()
    {
        // Act
        var json = EntityJson.WriteKey(Message);

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"LOADING_UNIT#4711"},"sk":{"S":"MSG#2#m1"}}""");
    }

    [Test]
    [Arguments(0, "APPOINTMENT#4711")]
    [Arguments(1, "LOADING_UNIT#4711")]
    [Arguments(2, "Topic#4711")]
    public async Task FormatAndParse_PartitionKey_RoundTripsThroughTheStoredName(int value, string expected)
    {
        // Arrange
        var owner = (MessageOwner)value;

        // Act
        var text = YardMessage.Keys.Pk.Format(owner, "4711");
        var parts = YardMessage.Keys.Pk.Parse(text);

        // Assert
        await Assert.That((text, parts.Owner, YardMessage.Keys.Pk.Format(parts.Owner, parts.OwnerKey))).IsEqualTo((expected, owner, expected));
    }

    [Test]
    [Arguments("LoadingUnit#4711")]
    [Arguments("loading_unit#4711")]
    public async Task Parse_KeyWithTheMemberNameOrAnotherCase_IsNoKey(string text)
    {
        // Act
        var failure = TestClient.Catch(() => _ = YardMessage.Keys.Pk.Parse(text));

        // Assert
        await Assert.That(failure).IsTypeOf<KeyFormatException>();
    }

    [Test]
    [Arguments("LOW", "Low")]
    [Arguments("LO", "Low")]
    [Arguments("HIGH", "High")]
    [Arguments("Normal", "Normal")]
    [Arguments("High", "Normal")]
    [Arguments("high", "Normal")]
    public async Task Read_StoredText_ReadsTheStoredNameOfAMemberOrAnAliasOrCountsAsMissing(string text, string expected)
    {
        // Arrange (the property starts as Normal, which a name that is no stored name leaves, as a missing attribute does)
        var json = "{\"pk\":{\"S\":\"APPOINTMENT#1\"},\"sk\":{\"S\":\"MSG#0#m1\"},\"_type\":{\"S\":\"YARD_MESSAGE\"},\"Owner\":{\"S\":\"APPOINTMENT\"},\"Priority\":{\"S\":\"" + text + "\"}}";

        // Act
        var message = EntityJson.Read<YardMessage>(json);

        // Assert
        await Assert.That(message.Priority.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Condition_EnumValue_IsSentAsItsStoredName()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var json = client.Table<YardMessage>("Yard").Delete(YardMessage.Keys.Of(MessageOwner.Appointment, "1", MessageOwner.Topic, "m1")).If(YardMessage.Attrs.Priority.Eq(Priority.High)).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("{\"S\":\"HIGH\"}", StringComparison.Ordinal), json.Contains("{\"S\":\"APPOINTMENT#1\"}", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Query_PartitionKeyOfAMemberWithAStoredName_SendsTheStoredName()
    {
        // Arrange
        using var client = TestClient.Create(new StubHandler());

        // Act
        var json = client.Table<YardMessage>("Yard").Query(YardMessage.Keys.Pk.Of(MessageOwner.Appointment, "1")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("{\"S\":\"APPOINTMENT#1\"}");
    }
}
