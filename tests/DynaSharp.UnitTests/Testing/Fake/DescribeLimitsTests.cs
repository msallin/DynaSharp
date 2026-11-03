namespace DynaSharp.UnitTests.Testing.Fake;

// The default quotas of the API reference, DescribeLimits: 80,000 read and write units per account and 40,000 per table.
public sealed class DescribeLimitsTests
{
    [Test]
    public async Task Handle_EmptyRequest_AnswersTheDefaultQuotas()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var body = FakeCalls.Ok(fake, "DescribeLimits", "{}");

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """{"AccountMaxReadCapacityUnits":80000,"AccountMaxWriteCapacityUnits":80000,"TableMaxReadCapacityUnits":40000,"TableMaxWriteCapacityUnits":40000}""",
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_AnyMember_AnswersUnsupported()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "DescribeLimits", """{"TableName":"Users"}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("UnsupportedByFakeException");
    }
}
