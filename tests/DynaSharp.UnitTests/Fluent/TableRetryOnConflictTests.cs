using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <c>RetryOnConflictAsync</c> on a table adds to the one on a client, which is the reach. What makes a run run again is in <see cref="DynamoDbClientConflictRetryTests"/>, because it is one loop.</summary>
internal sealed class TableRetryOnConflictTests
{
    [Test]
    public async Task RetryOnConflictAsync_Conflict_RunsAgainAndReturnsWhatThatRunReturns()
    {
        // Arrange (no client in reach, as in a service that holds a table)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await shop.RetryOnConflictAsync(async ct =>
        {
            await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct);
            return 7;
        });

        // Assert
        await Assert.That((result, handler.Requests.Count)).IsEqualTo((7, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_NeverGetsThrough_RunsFiveTimes()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(async ct =>
        {
            await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct);
            return 7;
        }));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(5);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task RetryOnConflictAsync_MaxAttempts_CountsTheFirstRun(int maxAttempts)
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(
            async ct =>
            {
                await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct);
                return 7;
            },
            maxAttempts));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(maxAttempts);
    }

    [Test]
    public async Task RetryOnConflictAsync_ConflictWithoutAResult_RunsAgain()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await shop.RetryOnConflictAsync(async ct => await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_NeverGetsThroughWithoutAResult_RunsFiveTimes()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(async ct => await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct)));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(5);
    }

    [Test]
    public async Task RetryOnConflictAsync_MaxAttemptsWithoutAResult_CountsTheFirstRun()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(async ct => await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct), maxAttempts: 2));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_TableOfAnEntity_IsTheSameLoop()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var tickets = client.Table<Ticket>("Shop");
        var key = Ticket.Keys.Of("t1", new DateOnly(2026, 11, 2));

        // Act
        var saved = await tickets.RetryOnConflictAsync(async ct => await tickets.Update(key).Set(Ticket.Attrs.Note, "n").IfVersion(3).SendAsync(ct));

        // Assert
        await Assert.That((saved.Version, handler.Requests.Count)).IsEqualTo(((long?)4L, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegate_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        Func<CancellationToken, ValueTask<int>> operation = null!;

        // Act
        var failure = TestClient.Catch(() => _ = shop.RetryOnConflictAsync(operation));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegateWithoutAResult_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        Func<CancellationToken, ValueTask> operation = null!;

        // Act
        var failure = TestClient.Catch(() => _ = shop.RetryOnConflictAsync(operation));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_MaxAttemptsBelowOne_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.RetryOnConflictAsync(static _ => new ValueTask<int>(1), 0));

        // Assert
        await Assert.That(((ArgumentOutOfRangeException)failure).ParamName).IsEqualTo("maxAttempts");
    }

    [Test]
    public async Task RetryOnConflictAsync_MaxAttemptsBelowOneWithoutAResult_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.RetryOnConflictAsync(static _ => ValueTask.CompletedTask, 0));

        // Assert
        await Assert.That(((ArgumentOutOfRangeException)failure).ParamName).IsEqualTo("maxAttempts");
    }

    [Test]
    public async Task RetryOnConflictAsync_TransactionConflictOnly_RunsAgainOnATransactionInTheWay()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("TransactionConflictException", "Transaction is ongoing for the item."));
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await shop.RetryOnConflictAsync(
            async ct =>
            {
                await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct);
                return 7;
            },
            ConflictKind.TransactionConflict);

        // Assert
        await Assert.That((result, handler.Requests.Count)).IsEqualTo((7, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_TransactionConflictOnly_SurfacesAFailedConditionAtOnce()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(
            async ct =>
            {
                await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct);
                return 7;
            },
            ConflictKind.TransactionConflict));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task RetryOnConflictAsync_RetryOnAndMaxAttempts_CountsTheFirstRun(int maxAttempts)
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(
            async ct =>
            {
                await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct);
                return 7;
            },
            ConflictKind.ConditionalCheckFailed,
            maxAttempts));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(maxAttempts);
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryOnWithoutAResult_RunsAgainOnWhatItNames()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await shop.RetryOnConflictAsync(async ct => { await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct); }, ConflictKind.ConditionalCheckFailed);

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryOnAndMaxAttemptsWithoutAResult_CountsTheFirstRun()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => shop.RetryOnConflictAsync(async ct => { await shop.Put().With("pk", "a").IfNotExists().SendAsync(ct); }, ConflictKind.Any, maxAttempts: 2));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryOnOfNoKind_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.RetryOnConflictAsync(static _ => ValueTask.CompletedTask, default(ConflictKind)));

        // Assert
        await Assert.That(((ArgumentOutOfRangeException)failure).ParamName).IsEqualTo("retryOn");
    }
}
