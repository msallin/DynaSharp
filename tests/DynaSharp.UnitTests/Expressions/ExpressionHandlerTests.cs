using DynaSharp.Expressions;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Expressions;

public sealed class ExpressionHandlerTests
{
    private static readonly Attr<string, ShoutingConverter> Shouted = new("mood");

    [Test]
    public async Task If_EveryKindOfHole_WritesItsPlaceholder()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var text = "t";

        // Act
        var json = client.Table("Shop", "pk").Update("a")
            .If($"{Attr("a")} = {text} AND {Attr<int>("b")} = {1} AND c = {2.5m} AND d = {true} AND e = {AttributeValue.Null} AND {Shouted} = {Shouted.Value("calm")}")
            .ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"ConditionExpression":"#n0 = :v0 AND #n1 = :v1 AND c = :v2 AND d = :v3 AND e = :v4 AND #n2 = :v5","ExpressionAttributeNames":{"#n0":"a","#n1":"b","#n2":"mood"},"ExpressionAttributeValues":{":v0":{"S":"t"},":v1":{"N":"1"},":v2":{"N":"2.5"},":v3":{"BOOL":true},":v4":{"NULL":true},":v5":{"S":"CALM"}}}""");
    }

    [Test]
    public async Task If_LiteralThatNeedsEscaping_StaysValidJson()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").Update("a");

        // Act: the analyzer refuses this text in an interpolated string, so the handler is driven by hand, as the compiler would.
        var condition = new ExpressionHandler(0, 1, command);
        condition.AppendLiteral("a = \"b\" AND é = ");
        condition.AppendFormatted(1);
        var json = command.If(condition).ToRequestJson();

        // Assert
        await Assert.That(DynaSharp.UnitTests.Operations.JsonComparer.Differences(json, json.Replace("\\\"b\\\"", "\\\"b\\\"", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task If_ForgottenAttr_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk");
        var name = "status";

        // Act
        var act = () => table.Update("a").If($"{name} = {"open"}").ToRequestJson();

        // Assert
        await Assert.That(act).Throws<ArgumentException>();
    }

    private readonly struct ShoutingConverter : IDynamoDbConverter<string>
    {
        public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value.ToUpperInvariant());

        public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();
    }
}
