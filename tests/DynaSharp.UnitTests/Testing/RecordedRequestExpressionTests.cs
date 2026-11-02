using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Consumer;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Testing;

/// <summary>The expressions of a recorded request with their placeholders resolved.</summary>
public sealed class RecordedRequestExpressionTests
{
    [Test]
    public async Task ConditionText_NameAndNumber_ReadsAsTheCondition()
    {
        // Arrange
        var request = await RecordAsync("""{"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"version"},"ExpressionAttributeValues":{":v0":{"N":"3"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("version = 3");
    }

    [Test]
    public async Task ConditionText_String_IsInQuotes()
    {
        // Arrange
        var request = await RecordAsync("""{"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"Status"},"ExpressionAttributeValues":{":v0":{"S":"Placed"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("""Status = "Placed" """.TrimEnd());
    }

    [Test]
    public async Task UpdateText_SeveralClauses_ResolvesEveryPlaceholder()
    {
        // Arrange
        var request = await RecordAsync(
            """{"UpdateExpression":"SET #n0 = :v0, #n1 = #n1 + :v1 REMOVE #n2","ExpressionAttributeNames":{"#n0":"plan","#n1":"visits","#n2":"coupon"},"ExpressionAttributeValues":{":v0":{"S":"pro"},":v1":{"N":"1"}}}""");

        // Act
        var text = request.UpdateText;

        // Assert
        await Assert.That(text).IsEqualTo("""SET plan = "pro", visits = visits + 1 REMOVE coupon""");
    }

    [Test]
    public async Task KeyConditionText_FunctionOfTheSortKey_KeepsTheFunction()
    {
        // Arrange
        var request = await RecordAsync(
            """{"KeyConditionExpression":"#n0 = :v0 AND begins_with(#n1, :v1)","ExpressionAttributeNames":{"#n0":"pk","#n1":"sk"},"ExpressionAttributeValues":{":v0":{"S":"a"},":v1":{"S":"ORDER#"}}}""");

        // Act
        var text = request.KeyConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("""pk = "a" AND begins_with(sk, "ORDER#")""");
    }

    [Test]
    public async Task FilterText_Filter_ReadsAsTheFilter()
    {
        // Arrange
        var request = await RecordAsync("""{"FilterExpression":"#n0 IN (:v0, :v1)","ExpressionAttributeNames":{"#n0":"plan"},"ExpressionAttributeValues":{":v0":{"S":"pro"},":v1":{"S":"free"}}}""");

        // Act
        var text = request.FilterText;

        // Assert
        await Assert.That(text).IsEqualTo("""plan IN ("pro", "free")""");
    }

    [Test]
    public async Task ProjectionText_NamesAndPaths_ReadsAsTheProjection()
    {
        // Arrange
        var request = await RecordAsync("""{"ProjectionExpression":"#n0, #n1.#n2, plain","ExpressionAttributeNames":{"#n0":"name","#n1":"address","#n2":"city"}}""");

        // Act
        var text = request.ProjectionText;

        // Assert
        await Assert.That(text).IsEqualTo("name, address.city, plain");
    }

    [Test]
    public async Task Texts_RequestWithoutExpressions_AreAllNull()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Users","Key":{"pk":{"S":"a"}}}""");

        // Act
        string?[] texts = [request.ConditionText, request.UpdateText, request.KeyConditionText, request.FilterText, request.ProjectionText];

        // Assert
        await Assert.That(texts).All().Satisfy(text => text.IsNull());
    }

    // Replacing the strings in turn would turn :v10 into the value of :v1 with a 0 behind it, and #n10 likewise.
    [Test]
    public async Task ConditionText_PlaceholderIsAPrefixOfAnother_ResolvesEachWhole()
    {
        // Arrange
        var request = await RecordAsync(
            """{"ConditionExpression":"#n1 = :v1 AND #n10 = :v10","ExpressionAttributeNames":{"#n1":"one","#n10":"ten"},"ExpressionAttributeValues":{":v1":{"N":"1"},":v10":{"N":"10"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("one = 1 AND ten = 10");
    }

    // What was put in is not read again: a value that looks like a placeholder stays a value.
    [Test]
    public async Task ConditionText_ValuesThatLookLikePlaceholders_StayAsTheyAre()
    {
        // Arrange
        var request = await RecordAsync(
            """{"ConditionExpression":"#n0 = :v0 AND #n1 = :v1","ExpressionAttributeNames":{"#n0":"a","#n1":"b"},"ExpressionAttributeValues":{":v0":{"S":":v1"},":v1":{"S":"#n1"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("""a = ":v1" AND b = "#n1" """.TrimEnd());
    }

    [Test]
    public async Task ConditionText_DocumentPath_ResolvesEachName()
    {
        // Arrange
        var request = await RecordAsync(
            """{"ConditionExpression":"#n0.#n1[2].#n2 = :v0","ExpressionAttributeNames":{"#n0":"order","#n1":"lines","#n2":"sku"},"ExpressionAttributeValues":{":v0":{"S":"KB-01"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("""order.lines[2].sku = "KB-01" """.TrimEnd());
    }

    // A dot in a name is legal, and without the backticks one attribute would read as a path through two.
    [Test]
    [Arguments("a.b")]
    [Arguments("a[1]")]
    [Arguments("a]")]
    public async Task ConditionText_NameThatReadsLikeAPath_IsInBackticks(string name)
    {
        // Arrange
        var request = await RecordAsync(
            $$$$"""{"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"{{{{name}}}}"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo($"`{name}` = 1");
    }

    [Test]
    public async Task ConditionText_PlaceholdersThatTheMapsLack_StaySoThatTheMistakeShows()
    {
        // Arrange
        var request = await RecordAsync("""{"ConditionExpression":"#n0 = :v0 AND #n1 = :v1","ExpressionAttributeNames":{"#n0":"a"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("a = 1 AND #n1 = :v1");
    }

    [Test]
    public async Task ConditionText_NoMaps_LeavesTheExpressionAsItIs()
    {
        // Arrange
        var request = await RecordAsync("""{"ConditionExpression":"attribute_not_exists(pk) AND #n0 = :v0"}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("attribute_not_exists(pk) AND #n0 = :v0");
    }

    [Test]
    public async Task ConditionText_PlaceholdersWrittenByHand_TakeLettersDigitsAndUnderscores()
    {
        // Arrange
        var request = await RecordAsync(
            """{"ConditionExpression":"(#my_name = :my_Value1)","ExpressionAttributeNames":{"#my_name":"a"},"ExpressionAttributeValues":{":my_Value1":{"N":"1"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("(a = 1)");
    }

    [Test]
    public async Task ConditionText_SignsWithoutAName_StayAsTheyAre()
    {
        // Arrange
        var request = await RecordAsync("""{"ConditionExpression":"a # b : c :","ExpressionAttributeNames":{"#n0":"x"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("a # b : c :");
    }

    [Test]
    [Arguments("""{"S":"a"}""", "\"a\"")]
    [Arguments("""{"S":""}""", "\"\"")]
    [Arguments("""{"S":"Zürich"}""", "\"Zürich\"")]
    [Arguments("""{"S":"say \"hi\""}""", "\"say \\\"hi\\\"\"")]
    [Arguments("""{"N":"3"}""", "3")]
    [Arguments("""{"N":"-3.10"}""", "-3.10")]
    [Arguments("""{"N":"1E+2"}""", "1E+2")]
    [Arguments("""{"BOOL":true}""", "true")]
    [Arguments("""{"BOOL":false}""", "false")]
    [Arguments("""{"NULL":true}""", "null")]
    [Arguments("""{"B":"aGk="}""", "b\"aGk=\"")]
    [Arguments("""{"L":[]}""", "[]")]
    [Arguments("""{"L":[{"S":"a"},{"N":"1"},{"L":[{"BOOL":true}]}]}""", "[\"a\", 1, [true]]")]
    [Arguments("""{"M":{}}""", "{}")]
    [Arguments("""{"M":{"k":{"N":"1"},"say \"j\"":{"S":"x"}}}""", "{\"k\": 1, \"say \\\"j\\\"\": \"x\"}")]
    [Arguments("""{"M":{"m":{"M":{"n":{"NULL":true}}}}}""", "{\"m\": {\"n\": null}}")]
    [Arguments("""{"SS":["a","b"]}""", "<<\"a\", \"b\">>")]
    [Arguments("""{"NS":["1","2.5"]}""", "<<1, 2.5>>")]
    [Arguments("""{"BS":["aGk=","aA=="]}""", "<<b\"aGk=\", b\"aA==\">>")]
    [Arguments("""{"X":{"not":"a type"}}""", "{\"not\":\"a type\"}")]
    [Arguments("""{"XY":"not a set"}""", "\"not a set\"")]
    public async Task ConditionText_Value_RendersItInTheStyleOfTheDocumentation(string wire, string expected)
    {
        // Arrange
        var request = await RecordAsync($$$"""{"ConditionExpression":":v0","ExpressionAttributeValues":{":v0":{{{wire}}}}}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    public async Task ConditionText_MemberIsJsonNull_IsNull()
    {
        // Arrange
        var request = await RecordAsync("""{"ConditionExpression":null}""");

        // Act
        var text = request.ConditionText;

        // Assert
        await Assert.That(text).IsNull();
    }

    [Test]
    public async Task ConditionText_BodyIsNotJson_ThrowsAsTheBodyDoes()
    {
        // Arrange
        var request = await RecordAsync("not json");

        // Act
        var act = () => request.ConditionText;

        // Assert
        await Assert.That(act).ThrowsException().And.IsAssignableTo<JsonException>();
    }

    // A test of an optimistic write says what it means, with what the client really sent.
    [Test]
    public async Task ConditionText_UpdateThroughTheClient_ReadsAsWhatTheCommandSaid()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);

        // Act
        await client.Table("Users", "pk").Update("a").Set("plan", "pro").If(Attr("version").Eq(3).And(Attr("address.city").Exists())).SendAsync();

        // Assert
        var request = stub.Requests.Single();
        await Assert.That((request.UpdateText, request.ConditionText)).IsEqualTo(("""SET plan = "pro" """.TrimEnd(), "(version = 3 AND attribute_exists(address.city))"));
    }

    [Test]
    public async Task Texts_QueryThroughTheClient_ReadAsWhatTheCommandSaid()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Items(Array.Empty<Item>()));
        using var client = StubClient.Create(stub);

        // Act
        await client.Table("Users", "pk").Query("a").Filter(Attr("plan").Ne("free")).Project("plan", "email").ItemAsync();

        // Assert
        var request = stub.Requests.Single();
        await Assert.That((request.KeyConditionText, request.FilterText, request.ProjectionText)).IsEqualTo(("""pk = "a" """.TrimEnd(), """plan <> "free" """.TrimEnd(), "plan, email"));
    }

    // The condition of IfNotExists comes from the entity, on an attribute name that goes through a placeholder as well.
    [Test]
    public async Task ConditionText_PutThroughAConsumer_NamesTheKey()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);
        var directory = new SubscriberDirectory(client);

        // Act
        await directory.RegisterAsync(new Subscriber("ada", "ada@example.com"));

        // Assert
        await Assert.That(stub.Requests.Single().ConditionText).IsEqualTo("attribute_not_exists(pk)");
    }

    private static async Task<RecordedRequest> RecordAsync(string body)
    {
        using var stub = new StubHandler { DefaultResponse = new StubResponse() };
        using var response = await HandlerRequest.PostAsync(stub, body);
        return stub.Requests.Single();
    }
}
