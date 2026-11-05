using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

// What ends the waits of a reader of a client of FakeDynamoDb.CreateStreamsClient: a change of a stream completes the task its NextStreamChange gave before, and a request that
// changes no stream completes none, so readers do not wake each other.
public sealed class StreamChangeTests
{
    [Test]
    [Arguments("put")]
    [Arguments("delete-item")]
    [Arguments("split")]
    [Arguments("disable")]
    [Arguments("delete-table")]
    [Arguments("reset")]
    public async Task NextStreamChange_ChangeOfAStream_Completes(string change)
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());
        FakeStreams.Put(fake, "a", 1);
        using var streams = fake.CreateStreamsClient();
        var next = streams.NextStreamChange!();

        // Act
        Change(fake, change);

        // Assert
        await Assert.That(next.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task NextStreamChange_EnableStream_Completes()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var streams = fake.CreateStreamsClient();
        var next = streams.NextStreamChange!();

        // Act
        fake.EnableStream("Users", StreamViewType.NewImage);

        // Assert
        await Assert.That(next.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    [Arguments("read")]
    [Arguments("same-put")]
    [Arguments("put-without-stream")]
    [Arguments("failed-put")]
    public async Task NextStreamChange_RequestThatChangesNoStream_DoesNotComplete(string request)
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());
        FakeCalls.Ok(fake, "CreateTable", FakeCalls.Users.Replace("\"Users\"", "\"Plain\"", StringComparison.Ordinal));
        FakeStreams.Put(fake, "a", 1);
        using var streams = fake.CreateStreamsClient();
        var next = streams.NextStreamChange!();

        // Act
        Request(fake, request);

        // Assert
        await Assert.That(next.IsCompleted).IsFalse();
    }

    [Test]
    public async Task NextStreamChange_AfterAChange_GivesATaskOfTheChangeAfterIt()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());
        using var streams = fake.CreateStreamsClient();
        FakeStreams.Put(fake, "a", 1);

        // Act
        var next = streams.NextStreamChange!();

        // Assert
        await Assert.That(next.IsCompleted).IsFalse();
    }

    [Test]
    public async Task CreateStreamsClient_OtherClock_StillWakesOnTheChangesOfThisFake()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        using var streams = fake.CreateStreamsClient(options => options with { TimeProvider = Clock() });

        // Assert
        await Assert.That(streams.NextStreamChange).IsNotNull();
    }

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static void Change(FakeDynamoDb fake, string change)
    {
        switch (change)
        {
            case "put":
                FakeStreams.Put(fake, "b", 1);
                break;
            case "delete-item":
                FakeStreams.Delete(fake, "a");
                break;
            case "split":
                fake.SplitStreamShards("Users");
                break;
            case "disable":
                fake.DisableStream("Users");
                break;
            case "delete-table":
                fake.DeleteTable("Users");
                break;
            default:
                fake.Reset();
                break;
        }
    }

    private static void Request(FakeDynamoDb fake, string request)
    {
        switch (request)
        {
            case "read":
                _ = FakeStreams.Records(fake);
                break;
            case "same-put":
                FakeStreams.Put(fake, "a", 1);
                break;
            case "put-without-stream":
                FakeCalls.Ok(fake, "PutItem", """{"TableName":"Plain","Item":{"pk":{"S":"a"}}}""");
                break;
            default:
                _ = FakeCalls.Send(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"b"}},"ConditionExpression":"attribute_exists(pk)"}""");
                break;
        }
    }
}
