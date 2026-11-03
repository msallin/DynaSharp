using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// DeleteItem answers {} whether there was an item or not, as the API reference describes it without ReturnValues.
public sealed class DeleteItemTests
{
    private const string Key = """{"pk":{"S":"c#1"},"sk":{"N":"1"}}""";

    [Test]
    public async Task Handle_StoredItem_RemovesIt()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$"""{"TableName":"Orders","Item":{{Key}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "DeleteItem", $$"""{"TableName":"Orders","Key":{{Key}}}""");

        // Assert
        await Assert.That((FakeCalls.SameJson("{}", body), fake.Items("Orders").Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Handle_NoItem_AnswersAnEmptyObject()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var body = FakeCalls.Ok(fake, "DeleteItem", $$"""{"TableName":"Orders","Key":{{Key}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ConditionExpression_AnswersUnsupportedAndKeepsTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$"""{"TableName":"Orders","Item":{{Key}}}""");

        // Act
        var (_, code, _) = FakeCalls.Error(fake, "DeleteItem", $$"""{"TableName":"Orders","Key":{{Key}},"ConditionExpression":"attribute_exists(pk)"}""");

        // Assert
        await Assert.That((code, fake.Items("Orders").Count)).IsEqualTo(("UnsupportedByFakeException", 1));
    }

    [Test]
    public async Task Handle_KeyWithoutTheSortKey_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "DeleteItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "The provided key element does not match the schema"));
    }
}
