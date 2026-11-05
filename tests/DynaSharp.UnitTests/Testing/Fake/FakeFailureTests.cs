using System.Net;
using DynaSharp.Streams;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

// FakeDynamoDb.FailNext: the next requests of an operation get the answer of the test, through every handler of the fake, and the rest go to the fake.
public sealed class FakeFailureTests
{
    private static readonly Item Alice = new() { ["pk"] = "alice", ["v"] = 1 };

    private static StubResponse ServerError => StubResponse.DynamoDbError("InternalServerError", "Failure of the service.", HttpStatusCode.InternalServerError);

    [Test]
    public async Task FailNext_PutItemOfAClientMadeBefore_FailsAndWritesNothing()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var client = fake.CreateClient();
        fake.FailNext("PutItem", ServerError);

        // Act
        var failure = await TestClient.CaptureAsync<InternalServerErrorException>(async () => await client.Table("Users", "pk").Put(Alice).SendAsync());

        // Assert
        await Assert.That((failure.StatusCode, fake.Items("Users").Count)).IsEqualTo((HttpStatusCode.InternalServerError, 0));
    }

    [Test]
    public async Task FailNext_Times_FailsThatManyRequestsAndTheFakeAnswersTheNext()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var client = fake.CreateClient();
        var users = client.Table("Users", "pk");
        fake.FailNext("PutItem", ServerError, times: 2);

        // Act
        var failures = 0;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await users.Put(Alice).SendAsync();
            }
            catch (InternalServerErrorException)
            {
                failures++;
            }
        }

        // Assert
        await Assert.That((failures, fake.Items("Users").Count)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task FailNext_When_FailsOnlyTheRequestsItAccepts()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Users.Replace("\"Users\"", "\"Orders\"", StringComparison.Ordinal));
        using var client = fake.CreateClient();
        fake.FailNext("PutItem", ServerError, when: static request => request.BodyJson.GetProperty("TableName").GetString() == "Orders");

        // Act
        await client.Table("Users", "pk").Put(Alice).SendAsync();
        var failure = await TestClient.CaptureAsync<InternalServerErrorException>(async () => await client.Table("Orders", "pk").Put(Alice).SendAsync());

        // Assert
        await Assert.That((failure is not null, fake.Items("Users").Count, fake.Items("Orders").Count)).IsEqualTo((true, 1, 0));
    }

    [Test]
    public async Task FailNext_OtherOperation_IsAnsweredByTheFake()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        fake.Put("Users", Alice);
        using var client = fake.CreateClient();
        fake.FailNext("DynamoDB_20120810.PutItem", ServerError);

        // Act
        var found = (await client.Table("Users", "pk").Get("alice").SendAsync()).Found;

        // Assert
        await Assert.That((found, Left(fake))).IsEqualTo((true, "1 of PutItem"));
    }

    [Test]
    public async Task FailNext_RawAnswer_TakesTheFailure()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        fake.FailNext("PutItem", ServerError);

        // Act
        var (status, code, _) = FakeCalls.Error(fake, "DynamoDB_20120810.PutItem", """{"TableName":"Users","Item":{"pk":{"S":"alice"}}}""");

        // Assert
        await Assert.That((status, code, fake.Items("Users").Count)).IsEqualTo((HttpStatusCode.InternalServerError, "InternalServerError", 0));
    }

    [Test]
    public async Task FailNext_ResponseQueuedOnTheHandler_ComesFirst()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        using var handler = fake.CreateHandler();
        using var client = StubClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));
        fake.FailNext("PutItem", ServerError);

        // Act
        await client.Table("Users", "pk").Put(Alice).SendAsync();

        // Assert
        await Assert.That(Left(fake)).IsEqualTo("1 of PutItem");
    }

    [Test]
    public async Task FailNext_ThrottledGetRecords_ReadStreamAsyncReadsThrough()
    {
        // Arrange: the reader of the shard waits on the clock of the fake before it asks again
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var fake = FakeStreams.UsersOn(time);
        FakeStreams.Put(fake, "k", 1);
        using var streams = fake.CreateStreamsClient();
        fake.FailNext("GetRecords", StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded"));
        var reading = streams.ReadStreamAsync(new ReadStreamRequest { StreamArn = FakeStreams.UsersArn, ShardIteratorType = ShardIteratorType.TrimHorizon }).Take(1).ToListAsync().AsTask();

        // Act
        while (!reading.IsCompleted)
        {
            if (time.NextDueIn is { } dueIn)
            {
                time.Advance(dueIn);
            }
            else
            {
                await Task.Yield();
            }
        }

        // Assert
        await Assert.That(((await reading).Single().Record.DynamoDb!.Keys!["pk"].AsString(), Left(fake))).IsEqualTo(("k", string.Empty));
    }

    [Test]
    public async Task ThrowIfFailuresLeft_FailuresNotTaken_NamesThem()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.FailNext("PutItem", ServerError, times: 2);
        fake.FailNext("GetRecords", ServerError);

        // Act
        var message = Left(fake);

        // Assert
        await Assert.That(message).IsEqualTo("2 of PutItem, 1 of GetRecords");
    }

    [Test]
    public async Task Reset_FailuresNotTaken_DropsThem()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.FailNext("PutItem", ServerError);

        // Act
        fake.Reset();

        // Assert
        await Assert.That(Left(fake)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task FailNext_InvalidArguments_Throw()
    {
        // Arrange
        var fake = new FakeDynamoDb();

        // Act
        var failures = new[]
        {
            Failure(() => fake.FailNext(null!, ServerError)),
            Failure(() => fake.FailNext(string.Empty, ServerError)),
            Failure(() => fake.FailNext("PutItem", null!)),
            Failure(() => fake.FailNext("PutItem", ServerError, times: 0)),
        };

        // Assert
        await Assert.That(string.Join(",", failures)).IsEqualTo("ArgumentNullException,ArgumentException,ArgumentNullException,ArgumentOutOfRangeException");
    }

    private static string Failure(Action act)
    {
        try
        {
            act();
            return "none";
        }
        catch (ArgumentException failure)
        {
            return failure.GetType().Name;
        }
    }

    // What ThrowIfFailuresLeft names, or empty when nothing is left.
    private static string Left(FakeDynamoDb fake)
    {
        try
        {
            fake.ThrowIfFailuresLeft();
            return string.Empty;
        }
        catch (InvalidOperationException failure)
        {
            const string Start = "FakeDynamoDb has planned failures that no request took: ";
            return failure.Message[Start.Length..failure.Message.IndexOf(". The code", StringComparison.Ordinal)];
        }
    }
}
