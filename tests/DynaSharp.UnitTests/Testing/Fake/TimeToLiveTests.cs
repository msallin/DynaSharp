using System.Net;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

// The bodies of the API reference for DescribeTimeToLive and UpdateTimeToLive. DynamoDB refuses to turn time to live on twice, which is what a second instance
// of a service that ensures it at start-up meets.
public sealed class TimeToLiveTests
{
    private const string Enable = """{"TableName":"Users","TimeToLiveSpecification":{"Enabled":true,"AttributeName":"expires"}}""";
    private const string Disable = """{"TableName":"Users","TimeToLiveSpecification":{"Enabled":false,"AttributeName":"expires"}}""";

    [Test]
    public async Task HandleDescribe_NewTable_AnswersDisabled()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "DescribeTimeToLive", """{"TableName":"Users"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}""", body)).IsTrue();
    }

    [Test]
    public async Task HandleUpdate_Enable_EchoesTheSpecificationAndDescribesEnabled()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTimeToLive", Enable);

        // Assert
        var description = FakeCalls.Ok(fake, "DescribeTimeToLive", """{"TableName":"Users"}""");
        await Assert.That((FakeCalls.SameJson("""{"TimeToLiveSpecification":{"Enabled":true,"AttributeName":"expires"}}""", body),
            FakeCalls.SameJson("""{"TimeToLiveDescription":{"TimeToLiveStatus":"ENABLED","AttributeName":"expires"}}""", description))).IsEqualTo((true, true));
    }

    [Test]
    public async Task HandleUpdate_DisableAfterEnable_DescribesDisabled()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "UpdateTimeToLive", Enable);

        // Act
        FakeCalls.Ok(fake, "UpdateTimeToLive", Disable);

        // Assert
        var description = FakeCalls.Ok(fake, "DescribeTimeToLive", """{"TableName":"Users"}""");
        await Assert.That(FakeCalls.SameJson("""{"TimeToLiveDescription":{"TimeToLiveStatus":"DISABLED"}}""", description)).IsTrue();
    }

    [Test]
    [Arguments(true, Enable, "TimeToLive is already enabled")]
    [Arguments(false, Disable, "TimeToLive is already disabled")]
    [Arguments(true, """{"TableName":"Users","TimeToLiveSpecification":{"Enabled":false,"AttributeName":"other"}}""", "TimeToLive is active on a different AttributeName: current AttributeName is expires")]
    [Arguments(false, """{"TableName":"Users","TimeToLiveSpecification":{"Enabled":true,"AttributeName":""}}""", "1 validation error detected: Value '' at 'timeToLiveSpecification.attributeName' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    public async Task HandleUpdate_ChangeDynamoDbRefuses_AnswersValidation(bool enabledBefore, string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        if (enabledBefore)
        {
            FakeCalls.Ok(fake, "UpdateTimeToLive", Enable);
        }

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTimeToLive", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    public async Task HandleUpdate_AttributeNameTooLong_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        var name = new string('a', 256);

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTimeToLive", $$$"""{"TableName":"Users","TimeToLiveSpecification":{"Enabled":true,"AttributeName":"{{{name}}}"}}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo($"1 validation error detected: Value '{name}' at 'timeToLiveSpecification.attributeName' failed to satisfy constraint: Member must have length less than or equal to 255");
    }

    [Test]
    public async Task HandleUpdate_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTimeToLive", Enable);

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task Expire_ItemsAtAndBeforeTheClockWithinFiveYears_DeletesThemAndNothingElse()
    {
        // Arrange: the clock of FakeCalls.With is 2026-01-01T00:00:00Z, 1767225600, and five years before it is 2021-01-01T00:00:00Z, 1609459200; DynamoDB leaves an older expiry.
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "UpdateTimeToLive", Enable);
        Put(fake, "past", """{"N":"1767225599.5"}""");
        Put(fake, "now", """{"N":"1767225600"}""");
        Put(fake, "future", """{"N":"1767225601"}""");
        Put(fake, "text", """{"S":"1"}""");
        Put(fake, "none", null);
        Put(fake, "five-years", """{"N":"1609459200"}""");
        Put(fake, "older", """{"N":"1609459199"}""");
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Orders","Item":{"pk":{"S":"o"},"sk":{"N":"1"},"expires":{"N":"1"}}}""");

        // Act
        var expired = fake.ExpireItems();

        // Assert
        await Assert.That((expired, string.Join(",", fake.Items("Users").Select(item => item["pk"].AsString()).Order(StringComparer.Ordinal)), fake.Items("Orders").Count))
            .IsEqualTo((3, "future,none,older,text", 1));
    }

    private static void Put(FakeDynamoDb fake, string key, string? expires) =>
        FakeCalls.Ok(fake, "PutItem", $$$"""{"TableName":"Users","Item":{"pk":{"S":"{{{key}}}"}{{{(expires is null ? string.Empty : ",\"expires\":" + expires)}}}}}""");
}
