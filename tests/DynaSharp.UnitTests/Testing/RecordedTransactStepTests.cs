using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Consumer;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Testing;

/// <summary>The steps of a recorded transaction, which a test reads instead of walking <see cref="RecordedRequest.BodyJson"/>.</summary>
public sealed class RecordedTransactStepTests
{
    private const string Write = """
        {"TransactItems":[
          {"Put":{"TableName":"Claims","Item":{"pk":{"S":"new@example.com"},"_type":{"S":"EMAIL"},"subscriberId":{"S":"ada"}},"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}},
          {"Delete":{"TableName":"Claims","Key":{"pk":{"S":"old@example.com"}}}},
          {"Update":{"TableName":"Subscribers","Key":{"pk":{"S":"ada"}},"UpdateExpression":"SET #n0 = :v0","ConditionExpression":"#n1 = :v1","ExpressionAttributeNames":{"#n0":"email","#n1":"version"},"ExpressionAttributeValues":{":v0":{"S":"new@example.com"},":v1":{"N":"3"}}}},
          {"ConditionCheck":{"TableName":"Plans","Key":{"pk":{"S":"pro"}},"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"open"},"ExpressionAttributeValues":{":v0":{"BOOL":true}}}}
        ]}
        """;

    private const string Read = """{"TransactItems":[{"Get":{"TableName":"Subscribers","Key":{"pk":{"S":"ada"}},"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"email"}}}]}""";

    [Test]
    public async Task TransactItems_FourKinds_GivesThemInOrder()
    {
        // Arrange
        var request = await RecordAsync(Write);

        // Act
        var steps = request.TransactItems;

        // Assert
        await Assert.That(steps.Select(step => (step.Kind, step.TableName!))).IsEquivalentTo(
            [
                (TransactStepKind.Put, "Claims"),
                (TransactStepKind.Delete, "Claims"),
                (TransactStepKind.Update, "Subscribers"),
                (TransactStepKind.ConditionCheck, "Plans"),
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task TransactItems_Get_GivesItsKindAndProjection()
    {
        // Arrange
        var request = await RecordAsync(Read);

        // Act
        var step = request.TransactItems.Single();

        // Assert
        await Assert.That((step.Kind, step.TableName, step.ProjectionText)).IsEqualTo((TransactStepKind.Get, "Subscribers", "email"));
    }

    [Test]
    public async Task TransactItems_AnotherOperation_IsEmpty()
    {
        // Arrange
        var request = await RecordAsync("""{"TableName":"Users","Key":{"pk":{"S":"a"}}}""");

        // Act
        var steps = request.TransactItems;

        // Assert
        await Assert.That(steps).IsEmpty();
    }

    [Test]
    public async Task ReadItem_Put_ReadsTheAttributes()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[0];

        // Act
        var item = step.ReadItem();

        // Assert
        await Assert.That(item["subscriberId"].AsString()).IsEqualTo("ada");
    }

    [Test]
    public async Task ReadItem_PutOfAnEntity_ReadsItThroughItsMapping()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[0];

        // Act
        var claim = step.ReadItem<EmailClaim>();

        // Assert
        await Assert.That(claim).IsEqualTo(new EmailClaim("new@example.com", "ada"));
    }

    [Test]
    public async Task ReadItem_StepWithoutAnItem_ThrowsNamingTheKind()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[1];

        // Act
        var act = () => step.ReadItem();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessageContaining("Delete", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadKey_Delete_ReadsTheKey()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[1];

        // Act
        var key = step.ReadKey();

        // Assert
        await Assert.That(key).IsEqualTo(StubResponse.KeyOf(new SubscriberKey("old@example.com")));
    }

    [Test]
    public async Task ReadKey_Update_ReadsTheKeyThroughAType()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[2];

        // Act
        var key = step.ReadKey<Item>();

        // Assert
        await Assert.That(key["pk"].AsString()).IsEqualTo("ada");
    }

    [Test]
    public async Task ReadKey_StepWithoutAKey_ThrowsNamingTheKind()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[0];

        // Act
        var act = () => step.ReadKey();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>().WithMessageContaining("Put", StringComparison.Ordinal);
    }

    [Test]
    public async Task Texts_Update_ResolveWithTheMapsOfTheStep()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[2];

        // Act
        var texts = (step.UpdateText, step.ConditionText);

        // Assert
        await Assert.That(texts).IsEqualTo(("""SET email = "new@example.com" """.TrimEnd(), "version = 3"));
    }

    [Test]
    public async Task ConditionText_ConditionCheck_ResolvesWithTheMapsOfTheStep()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[3];

        // Act
        var text = step.ConditionText;

        // Assert
        await Assert.That(text).IsEqualTo("open = true");
    }

    [Test]
    public async Task Texts_DeleteWithoutExpressions_AreNull()
    {
        // Arrange
        var step = (await RecordAsync(Write)).TransactItems[1];

        // Act
        string?[] texts = [step.ConditionText, step.UpdateText, step.ProjectionText];

        // Assert
        await Assert.That(texts).All().Satisfy(text => text.IsNull());
    }

    // The request API sends what is set, also nothing or a member this library does not know, and DynamoDB rejects it. A test should see that, not a failure of its own.
    [Test]
    [Arguments("""{"TransactItems":[{}]}""", null)]
    [Arguments("""{"TransactItems":[{"Later":{"TableName":"T"}}]}""", "T")]
    public async Task TransactItems_NoKnownMember_IsUnknown(string body, string? table)
    {
        // Arrange
        var request = await RecordAsync(body);

        // Act
        var step = request.TransactItems.Single();

        // Assert
        await Assert.That((step.Kind, step.TableName)).IsEqualTo((TransactStepKind.Unknown, table));
    }

    // The real shape of a uniqueness claim: a client sends the three actions, and a test reads them by name.
    [Test]
    public async Task TransactItems_ThroughAConsumer_ReadsAsTheTransaction()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);
        var directory = new SubscriberDirectory(client);

        // Act
        await directory.ChangeEmailAsync(new Subscriber("ada", "old@example.com"), "new@example.com");

        // Assert
        var steps = stub.Requests.Single().TransactItems;
        await Assert.That((steps[0].ReadItem<EmailClaim>(), steps[0].ConditionText, steps[1].ReadKey()["pk"].AsString(), steps[2].UpdateText, steps[2].ConditionText)).IsEqualTo(
            (new EmailClaim("new@example.com", "ada"), "attribute_not_exists(pk)", "old@example.com", """SET email = "new@example.com", _type = if_not_exists(_type, "SUBSCRIBER")""", "attribute_exists(pk)"));
    }

    [Test]
    public async Task TransactItems_ConditionCheckThroughTheClient_ReadsAsWhatTheCommandSaid()
    {
        // Arrange
        using var stub = new StubHandler(_ => new StubResponse());
        using var client = StubClient.Create(stub);
        var plans = client.Table("Plans", "pk");

        // Act
        await client.TransactWrite().Check(plans, "pro").If(Attr("open").Eq(true)).Delete(plans, "free").SendAsync();

        // Assert
        var check = stub.Requests.Single().TransactItems[0];
        await Assert.That((check.Kind, check.TableName, check.ConditionText)).IsEqualTo((TransactStepKind.ConditionCheck, "Plans", "open = true"));
    }

    private static async Task<RecordedRequest> RecordAsync(string body)
    {
        using var stub = new StubHandler { DefaultResponse = new StubResponse() };
        using var response = await HandlerRequest.PostAsync(stub, body, "DynamoDB_20120810.TransactWriteItems");
        return stub.Requests.Single();
    }
}
