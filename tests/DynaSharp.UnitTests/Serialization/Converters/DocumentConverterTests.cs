using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Serialization.Converters;

public sealed class DocumentConverterTests
{
    [Test]
    public async Task Write_Document_WritesAMap()
    {
        // Arrange
        var address = new Address("Bern");

        // Act
        var json = TestJson.Write(writer => DocumentConverter<Address>.Write(writer, address));

        // Assert
        await Assert.That(json).IsEqualTo("""{"M":{"city":{"S":"Bern"}}}""");
    }

    [Test]
    public async Task Read_Map_ReadsTheDocument()
    {
        // Arrange
        const string json = """{"M":{"unknown":{"N":"1"},"city":{"S":"Bern"}}}""";

        // Act
        var address = TestJson.Read(json, static (ref reader) => reader.ReadAttribute<Address, DocumentConverter<Address>>());

        // Assert
        await Assert.That(address).IsEqualTo(new Address("Bern"));
    }

    [Test]
    public async Task Read_ValueAfterTheMap_LeavesTheReaderOnIt()
    {
        // Arrange
        const string json = """{"a":{"M":{"city":{"S":"Bern"}}},"b":{"S":"next"}}""";

        // Act
        var next = TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartItem();
            reader.TryReadAttributeName();
            reader.ReadAttribute<Address, DocumentConverter<Address>>();
            reader.TryReadAttributeName();
            return reader.ReadAttribute<string, StringConverter>();
        });

        // Assert
        await Assert.That(next).IsEqualTo("next");
    }

    [Test]
    public async Task Read_ValueOfAnotherType_Throws()
    {
        // Arrange
        const string json = """{"S":"Bern"}""";

        // Act
        var failure = await TestClient.CaptureAsync<JsonException>(() => Task.FromResult(TestJson.Read(json, static (ref reader) => reader.ReadAttribute<Address, DocumentConverter<Address>>())));

        // Assert
        await Assert.That(failure.Message).Contains("Map");
    }

    [Test]
    public async Task Read_MapOfAnotherEntity_PassesTheMappingExceptionOn()
    {
        // Arrange
        const string json = """{"M":{"pk":{"S":"CUSTOMER#a"},"_type":{"S":"ORDER"},"fullName":{"S":"x"}}}""";

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => Task.FromResult(TestJson.Read(json, static (ref reader) => reader.ReadAttribute<Customer, DocumentConverter<Customer>>())));

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task Write_Null_Throws()
    {
        // Arrange
        Address address = null!;

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(TestJson.Write(writer => DocumentConverter<Address>.Write(writer, address))));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("value");
    }

    [Test]
    public async Task RoundTrip_ListOfDocuments_KeepsThem()
    {
        // Arrange
        List<Address> addresses = [new("Bern"), new("Basel")];

        // Act
        var json = TestJson.Write(writer => ListConverter<Address, DocumentConverter<Address>>.Write(writer, addresses));
        var read = TestJson.Read(json, static (ref reader) => reader.ReadAttribute<List<Address>, ListConverter<Address, DocumentConverter<Address>>>());

        // Assert
        await Assert.That(read).IsEquivalentTo(addresses);
    }

    [Test]
    public async Task RoundTrip_StructDocument_KeepsIt()
    {
        // Arrange
        var session = new Session("s");

        // Act
        var json = TestJson.Write(writer => DocumentConverter<Session>.Write(writer, session));
        var read = TestJson.Read(json, static (ref reader) => reader.ReadAttribute<Session, DocumentConverter<Session>>());

        // Assert
        await Assert.That(read).IsEqualTo(session);
    }
}
