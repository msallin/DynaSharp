using DynaSharp.Credentials;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Consumer;

// Tests as an application writes them: the code under test gets a client over a StubHandler, the arrangement says what DynamoDB answers, the assertions say what was sent,
// and nothing here holds DynamoDB JSON. The client is made with the public constructor, as an application makes it.
public sealed class SubscriberDirectoryTests
{
    private const string TableName = SubscriberDirectory.TableName;

    private static readonly Subscriber Ada = new("ada", "ada@example.com", "pro");
    private static readonly Subscriber Grace = new("grace", "grace@example.com");

    [Test]
    public async Task FindAsync_ExistingSubscriber_ReturnsIt()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Item(Ada));
        using var client = CreateClient(stub);

        // Act
        var found = await new SubscriberDirectory(client).FindAsync("ada");

        // Assert
        await Assert.That(found).IsEqualTo(Ada);
    }

    [Test]
    public async Task FindAsync_NoSuchSubscriber_ReturnsNull()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Item<Subscriber>(null));
        using var client = CreateClient(stub);

        // Act
        var found = await new SubscriberDirectory(client).FindAsync("nobody");

        // Assert
        await Assert.That(found).IsNull();
    }

    [Test]
    public async Task FindAsync_Id_AsksForTheKeyOfTheSubscriber()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Item(Ada));
        using var client = CreateClient(stub);

        // Act
        await new SubscriberDirectory(client).FindAsync("ada");

        // Assert
        await Assert.That(stub.Requests.Single().ReadKey()).IsEqualTo(StubResponse.KeyOf(Ada));
    }

    [Test]
    public async Task RegisterAsync_NewId_PutsTheSubscriber()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(new StubResponse());
        using var client = CreateClient(stub);

        // Act
        await new SubscriberDirectory(client).RegisterAsync(Ada);

        // Assert
        await Assert.That(stub.Requests.Single().ReadItem<Subscriber>()).IsEqualTo(Ada);
    }

    [Test]
    public async Task RegisterAsync_IdTaken_ReturnsWhoHoldsIt()
    {
        // Arrange (a conditional failure with the item, as ReturnOldOnFailure gets it)
        var holder = Ada with { Plan = "team" };
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.ConditionalCheckFailed(holder));
        using var client = CreateClient(stub);

        // Act
        var registered = await new SubscriberDirectory(client).RegisterAsync(Ada);

        // Assert
        await Assert.That(registered).IsEqualTo(holder);
    }

    [Test]
    public async Task RegisterAsync_FailureWithoutTheItem_LetsTheExceptionThrough()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.ConditionalCheckFailed());
        using var client = CreateClient(stub);
        var directory = new SubscriberDirectory(client);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => directory.RegisterAsync(Ada));

        // Assert
        await Assert.That((failure.Item, failure.Message)).IsEqualTo(((Item?)null, "The conditional request failed"));
    }

    [Test]
    public async Task ChangePlanAsync_ExistingSubscriber_ReturnsTheNewItem()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Attributes(Ada with { Plan = "team" }));
        using var client = CreateClient(stub);

        // Act
        var changed = await new SubscriberDirectory(client).ChangePlanAsync("ada", "team");

        // Assert
        await Assert.That(changed).IsEqualTo(Ada with { Plan = "team" });
    }

    [Test]
    public async Task ChangeEmailAsync_AddressFree_ClaimsItInTheFirstAction()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(new StubResponse());
        using var client = CreateClient(stub);

        // Act
        var holder = await new SubscriberDirectory(client).ChangeEmailAsync(Ada, "ada@new.example.com");

        // Assert (the items of a transaction are at the end of a path, which the body as JSON reaches)
        var put = stub.Requests.Single().BodyJson.GetProperty("TransactItems")[0].GetProperty("Put");
        await Assert.That((holder, put.GetProperty("TableName").GetString(), put.GetProperty("Item").GetProperty("subscriberId").GetProperty("S").GetString()))
            .IsEqualTo(((string?)null, TableName, "ada"));
    }

    [Test]
    public async Task ChangeEmailAsync_AddressTaken_ReturnsWhoHoldsIt()
    {
        // Arrange (a cancellation that the marker of the claim finds, at the first of three actions)
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.TransactionCanceled(
            StubResponse.ConditionalCheckFailedReason(new EmailClaim("taken@example.com", "grace")),
            StubResponse.Reason(TransactionFailure.None),
            StubResponse.Reason(TransactionFailure.None)));
        using var client = CreateClient(stub);

        // Act
        var holder = await new SubscriberDirectory(client).ChangeEmailAsync(Ada, "taken@example.com");

        // Assert
        await Assert.That(holder).IsEqualTo("grace");
    }

    [Test]
    public async Task ChangeEmailAsync_AnotherActionFailed_LetsTheExceptionThrough()
    {
        // Arrange (the marker of the claim reads None, so the filter of the service does not take this one)
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.TransactionConflict, TransactionFailure.None));
        using var client = CreateClient(stub);
        var directory = new SubscriberDirectory(client);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(() => directory.ChangeEmailAsync(Ada, "new@example.com"));

        // Assert
        await Assert.That(failure.Failures.Single()).IsEqualTo(new TransactionActionFailure(1, TransactionFailure.TransactionConflict));
    }

    [Test]
    public async Task ImportAsync_NothingLeft_WritesInOneRequest()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Unprocessed(TableName));
        using var client = CreateClient(stub);

        // Act
        var written = await new SubscriberDirectory(client).ImportAsync([Ada, Grace]);

        // Assert
        await Assert.That((written, stub.Requests.Count)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task ImportAsync_UnprocessedWrites_SendsThemAgainOnTheFakeClockWithoutWaiting()
    {
        // Arrange (seven rounds that get nothing done, as the eighth would end the batch: the wait of a round is random and can round down to nothing, but
        // not seven times in a row, so the clock shows that the waits ran on it)
        var clock = new ManualTimeProvider();
        var started = clock.GetUtcNow();
        using var stub = new StubHandler();
        for (var round = 0; round < 7; round++)
        {
            stub.Enqueue(StubResponse.Unprocessed(TableName, StubResponse.Put(Ada), StubResponse.Put(Grace)));
        }

        stub.Enqueue(StubResponse.Unprocessed(TableName));
        using var client = CreateClient(stub, clock);

        // Act
        var import = new SubscriberDirectory(client).ImportAsync([Ada, Grace]);
        await AdvanceUntilDoneAsync(clock, import);
        var written = await import;

        // Assert
        await Assert.That((written, stub.Requests.Count, clock.GetUtcNow() > started)).IsEqualTo((2, 8, true));
    }

    [Test]
    public async Task ImportAsync_OneWriteLeft_SendsOnlyThatWriteAgain()
    {
        // Arrange (a round that gets something done waits too, up to 50 ms, so it needs the clock as well)
        var clock = new ManualTimeProvider();
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Unprocessed(TableName, StubResponse.Put(Grace)));
        stub.Enqueue(StubResponse.Unprocessed(TableName));
        using var client = CreateClient(stub, clock);

        // Act
        var import = new SubscriberDirectory(client).ImportAsync([Ada, Grace]);
        await AdvanceUntilDoneAsync(clock, import);

        // Assert
        var retried = stub.Requests[1].BodyJson.GetProperty("RequestItems").GetProperty(TableName);
        await Assert.That((retried.GetArrayLength(), retried[0].GetProperty("PutRequest").GetProperty("Item").GetProperty("pk").GetProperty("S").GetString())).IsEqualTo((1, "grace"));
    }

    [Test]
    public async Task ImportAsync_NothingEverProcessed_ReportsWhatWasNotWritten()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        using var stub = new StubHandler { DefaultResponse = StubResponse.Unprocessed(TableName, StubResponse.Put(Ada)) };
        using var client = CreateClient(stub, clock);

        // Act
        var import = new SubscriberDirectory(client).ImportAsync([Ada]);
        await AdvanceUntilDoneAsync(clock, import);
        var failure = await TestClient.CaptureAsync<BatchIncompleteException>(() => import);

        // Assert
        await Assert.That(failure.UnprocessedItems[TableName].Single().PutRequest!.Item["pk"].AsString()).IsEqualTo("ada");
    }

    [Test]
    public async Task ListAsync_TwoPages_ReadsThemInOrder()
    {
        // Arrange (a page that more pages follow, and the one that ends the read)
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Items([Ada], StubResponse.KeyOf(Ada)));
        stub.Enqueue(StubResponse.Items([Grace]));
        using var client = CreateClient(stub);

        // Act
        var all = await new SubscriberDirectory(client).ListAsync();

        // Assert
        await Assert.That(all).IsEquivalentTo([Ada, Grace]);
    }

    [Test]
    public async Task ListAsync_FilteredEmptyPage_GoesOnFromItsKey()
    {
        // Arrange
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Items(Array.Empty<Subscriber>(), new SubscriberKey("ada"), scannedCount: 50));
        stub.Enqueue(StubResponse.Items([Grace], scannedCount: 50));
        using var client = CreateClient(stub);

        // Act
        await new SubscriberDirectory(client).ListAsync();

        // Assert
        await Assert.That(stub.Requests[1].ReadItem("ExclusiveStartKey")).IsEqualTo(StubResponse.KeyOf(new SubscriberKey("ada")));
    }

    [Test]
    public async Task ReadManyAsync_OneKeyLeft_AsksForItAgainAndReturnsBoth()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        using var stub = new StubHandler();
        stub.Enqueue(StubResponse.Unprocessed(TableName, StubResponse.Keys(new SubscriberKey("grace")), [Ada]));
        stub.Enqueue(StubResponse.BatchItems(TableName, [Grace]));
        using var client = CreateClient(stub, clock);

        // Act
        var read = new SubscriberDirectory(client).ReadManyAsync(["ada", "grace"]);
        await AdvanceUntilDoneAsync(clock, read);

        // Assert
        await Assert.That(await read).IsEquivalentTo([Ada, Grace]);
    }

    // The wait of a round is random, so a test moves the clock by the longest one until the call is done, however many it takes.
    private static async Task AdvanceUntilDoneAsync(ManualTimeProvider clock, Task call)
    {
        while (!call.IsCompleted)
        {
            clock.Advance(TimeSpan.FromSeconds(20));
            await Task.Yield();
        }
    }

    private static DynamoDbClient CreateClient(StubHandler stub, TimeProvider? clock = null) => new(new DynamoDbClientOptions
    {
        // Every option that would otherwise come from the machine, and a timeout no step of the clock reaches.
        Region = AwsRegion.FromName("eu-central-2"),
        ServiceUrl = new Uri("https://dynamodb.eu-central-2.amazonaws.com"),
        Credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")),
        AttemptTimeout = TimeSpan.FromHours(1),
        HttpHandler = stub,
        MaxAttempts = 1,
        TimeProvider = clock ?? TimeProvider.System,
    });
}
