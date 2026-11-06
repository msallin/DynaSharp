using DynaSharp.Retries;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Http;

namespace DynaSharp.UnitTests;

// What makes a run of RetryOnConflictAsync run again, what ends it, how long it waits and what it leaves alone. A failure either comes from a delegate that throws it, or through
// a stub, where the wire is what is being tested. The clock is manual and the jitter fixed, so nothing here waits.
internal sealed class DynamoDbClientConflictRetryTests
{
    private const string TableName = "Shop";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(1);

    public static IEnumerable<FailureCase> Conflicts()
    {
        yield return new("ConditionalCheckFailedException", new ConditionalCheckFailedException("The conditional request failed."));
        yield return new("TransactionConflictException", new TransactionConflictException("Transaction is ongoing for the item."));

        // One reason of a conflict is enough, wherever it stands and whatever the others say.
        yield return Canceled("ConditionalCheckFailed");
        yield return Canceled("TransactionConflict");
        yield return Canceled("None", "ConditionalCheckFailed");
        yield return Canceled("None", "None", "TransactionConflict");
        yield return Canceled("ConditionalCheckFailed", "None");
        yield return Canceled("ConditionalCheckFailed", "TransactionConflict");
        yield return Canceled("ThrottlingError", "TransactionConflict");
        yield return Canceled("ProvisionedThroughputExceeded", "ConditionalCheckFailed");
        yield return Canceled("ValidationError", "ConditionalCheckFailed");
        yield return Canceled("ItemCollectionSizeLimitExceeded", "TransactionConflict");
        yield return Canceled("SomeNewCode", "ConditionalCheckFailed");
    }

    public static IEnumerable<FailureCase> OtherFailures()
    {
        // A cancelled transaction without a reason of a conflict.
        yield return new("TransactionCanceledException without reasons", new TransactionCanceledException("Transaction cancelled."));
        yield return Canceled("None");
        yield return Canceled("None", "None");
        yield return Canceled("ThrottlingError");
        yield return Canceled("ProvisionedThroughputExceeded");
        yield return Canceled("ValidationError");
        yield return Canceled("ItemCollectionSizeLimitExceeded");
        yield return Canceled("SomeNewCode");
        yield return Canceled("None", "ThrottlingError", "ValidationError");
        yield return new("TransactionCanceledException with a reason without a code", new TransactionCanceledException("Transaction cancelled.", [new()]));

        // The errors of the service, among them the ones that the pipeline repeats by itself.
        yield return new("DynamoDbException", new DynamoDbException("Something new.", "SomeNewException", System.Net.HttpStatusCode.BadRequest));
        yield return new("ValidationException", new ValidationException("The expression does not parse."));
        yield return new("ProvisionedThroughputExceededException", new ProvisionedThroughputExceededException("Throttled."));
        yield return new("ThrottlingException", new ThrottlingException("Throttled."));
        yield return new("RequestLimitExceededException", new RequestLimitExceededException("Throttled."));
        yield return new("TransactionInProgressException", new TransactionInProgressException("The transaction is in progress."));
        yield return new("ReplicatedWriteConflictException", new ReplicatedWriteConflictException("A write in another Region conflicts."));
        yield return new("IdempotentParameterMismatchException", new IdempotentParameterMismatchException("The token was used for another request."));
        yield return new("InternalServerErrorException", new InternalServerErrorException("The server failed."));
        yield return new("ResourceNotFoundException", new ResourceNotFoundException("No such table."));
        yield return new("AccessDeniedException", new AccessDeniedException("Not allowed."));
        yield return new("ItemCollectionSizeLimitExceededException", new ItemCollectionSizeLimitExceededException("Too large."));
        yield return new("BatchIncompleteException", new BatchIncompleteException("Gave up.", new Dictionary<string, IReadOnlyList<DynaSharp.Requests.WriteRequest>>(), 0));

        // Failures that are none of the service's business.
        yield return new("DynamoDbMappingException", new DynamoDbMappingException("Cart", "lines"));
        yield return new("CredentialsUnavailableException", new CredentialsUnavailableException("No credentials."));
        yield return new("TimeoutException", new TimeoutException("The attempt timed out."));
        yield return new("HttpRequestException", new HttpRequestException("The connection dropped."));
        yield return new("OperationCanceledException", new OperationCanceledException("Cancelled by the caller."));
        yield return new("InvalidOperationException", new InvalidOperationException("Something of the caller."));
    }

    public static IEnumerable<KindCase> NamedConflicts()
    {
        yield return new("ConditionalCheckFailed, ConditionalCheckFailedException", ConflictKind.ConditionalCheckFailed, Lost());
        yield return new("ConditionalCheckFailed, a cancelled transaction with ConditionalCheckFailed", ConflictKind.ConditionalCheckFailed, Canceled("ConditionalCheckFailed").Exception);
        yield return new("ConditionalCheckFailed, a cancelled transaction with both", ConflictKind.ConditionalCheckFailed, Canceled("TransactionConflict", "ConditionalCheckFailed").Exception);
        yield return new("TransactionConflict, TransactionConflictException", ConflictKind.TransactionConflict, new TransactionConflictException("Transaction is ongoing for the item."));
        yield return new("TransactionConflict, a cancelled transaction with TransactionConflict", ConflictKind.TransactionConflict, Canceled("TransactionConflict").Exception);
        yield return new("TransactionConflict, a cancelled transaction with both", ConflictKind.TransactionConflict, Canceled("ConditionalCheckFailed", "TransactionConflict").Exception);
        yield return new("TransactionConflict, a cancelled transaction with another reason too", ConflictKind.TransactionConflict, Canceled("None", "ThrottlingError", "TransactionConflict").Exception);
    }

    public static IEnumerable<KindCase> UnnamedConflicts()
    {
        yield return new("TransactionConflict, ConditionalCheckFailedException", ConflictKind.TransactionConflict, Lost());
        yield return new("TransactionConflict, a cancelled transaction with ConditionalCheckFailed", ConflictKind.TransactionConflict, Canceled("ConditionalCheckFailed").Exception);
        yield return new("TransactionConflict, a cancelled transaction with ConditionalCheckFailed and None", ConflictKind.TransactionConflict, Canceled("None", "ConditionalCheckFailed").Exception);
        yield return new("ConditionalCheckFailed, TransactionConflictException", ConflictKind.ConditionalCheckFailed, new TransactionConflictException("Transaction is ongoing for the item."));
        yield return new("ConditionalCheckFailed, a cancelled transaction with TransactionConflict", ConflictKind.ConditionalCheckFailed, Canceled("TransactionConflict").Exception);
        yield return new("ConditionalCheckFailed, a cancelled transaction with TransactionConflict and ThrottlingError", ConflictKind.ConditionalCheckFailed, Canceled("ThrottlingError", "TransactionConflict").Exception);
        yield return new("TransactionConflict, a cancelled transaction without a conflict", ConflictKind.TransactionConflict, Canceled("None", "ValidationError").Exception);
        yield return new("ConditionalCheckFailed, a cancelled transaction without reasons", ConflictKind.ConditionalCheckFailed, new TransactionCanceledException("Transaction cancelled."));
        yield return new("ConditionalCheckFailed, another error", ConflictKind.ConditionalCheckFailed, new ValidationException("The expression does not parse."));
    }

    [Test]
    [MethodDataSource(nameof(Conflicts))]
    public async Task RetryOnConflictAsync_Conflict_RunsAgainAndReturnsWhatThatRunReturns(FailureCase conflict)
    {
        // Arrange
        var script = new Script(conflict.Exception);
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync(script.RunAsync);

        // Assert
        await Assert.That((result, script.Runs)).IsEqualTo((2, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryInsideARun_MultipliesTheRuns()
    {
        // Arrange (what the docs say about a follow-up write in the delegate: the inner retry that gave up counts as a conflict of the outer one; a delegate in a variable is not what DYNA217 follows)
        var script = new Script([.. Enumerable.Repeat<Exception>(new ConditionalCheckFailedException("The conditional request failed."), 25)]);
        using var client = NewClient();
        Func<CancellationToken, Task<int>> inner = ct => client.RetryOnConflictAsync(script.RunAsync, ct);

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(ct => new ValueTask<int>(inner(ct))));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(25);
    }

    [Test]
    [MethodDataSource(nameof(Conflicts))]
    public async Task RetryOnConflictAsync_RetryThatGaveUp_SurfacesWhatARetryAroundItRunsAgainOn(FailureCase conflict)
    {
        // Arrange (DYNA217 mirrors this: whatever ends a retry as a conflict starts the retry around it over)
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(() => client.RetryOnConflictAsync<int>(_ => throw conflict.Exception));

        // Assert
        await Assert.That(FailureClassifier.IsConflict(failure, ConflictKind.Any)).IsTrue();
    }

    [Test]
    public async Task RetryOnConflictAsync_RunsUsedUp_SurfacesTheExceptionOfTheLastRunWithItsKey()
    {
        // Arrange (each of the five runs fails with an exception of its own, so the key has to be on the one that surfaces)
        using var handler = new StubHandler();
        for (var run = 0; run < 5; run++)
        {
            handler.Enqueue(StubResponse.ConditionalCheckFailed());
        }

        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var table = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(async ct =>
        {
            await table.Put().With("pk", "secret-key").IfNotExists().SendAsync(ct);
            return 1;
        }));

        // Assert
        await Assert.That((failure.Key, failure.Item, failure.Message.Contains("secret-key", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((new Item { ["pk"] = "secret-key" }, (Item?)null, false, 5));
    }

    [Test]
    [MethodDataSource(nameof(Conflicts))]
    public async Task RetryOnConflictAsync_ConflictWithoutAResult_RunsAgain(FailureCase conflict)
    {
        // Arrange
        var script = new Script(conflict.Exception);
        using var client = NewClient();

        // Act
        await client.RetryOnConflictAsync(script.RunWithoutResultAsync);

        // Assert
        await Assert.That(script.Runs).IsEqualTo(2);
    }

    [Test]
    [MethodDataSource(nameof(OtherFailures))]
    public async Task RetryOnConflictAsync_OtherFailure_SurfacesAtOnce(FailureCase other)
    {
        // Arrange
        var script = new Script(other.Exception);
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<Exception>(() => client.RetryOnConflictAsync(script.RunAsync));

        // Assert
        await Assert.That((ReferenceEquals(failure, other.Exception), script.Runs)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task RetryOnConflictAsync_OtherFailureWithoutAResult_SurfacesAtOnce()
    {
        // Arrange
        var other = new ValidationException("The expression does not parse.");
        var script = new Script(other);
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync));

        // Assert
        await Assert.That((ReferenceEquals(failure, other), script.Runs)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task RetryOnConflictAsync_DelegateThrowsBeforeItReturnsATask_RunsAgain()
    {
        // Arrange (a delegate that is not async fails where it is called, not in what it returns)
        var runs = 0;
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync<int>(_ => ++runs == 1 ? throw new ConditionalCheckFailedException("The conditional request failed.") : new ValueTask<int>(runs));

        // Assert
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_NoConflict_RunsOnceAndWaitsForNothing()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new Script();
        using var client = NewClient(time: time);

        // Act
        var result = await client.RetryOnConflictAsync(script.RunAsync);

        // Assert
        await Assert.That((result, script.Runs, time.TimersCreated)).IsEqualTo((1, 1, 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_SomethingElseOnTheThirdRun_SurfacesThatAndRunsNoMore()
    {
        // Arrange
        var other = new InvalidOperationException("Something of the caller.");
        var script = new Script(Lost(), Lost(), other);
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => client.RetryOnConflictAsync(script.RunAsync));

        // Assert
        await Assert.That((ReferenceEquals(failure, other), script.Runs)).IsEqualTo((true, 3));
    }

    [Test]
    public async Task RetryOnConflictAsync_EveryRunLoses_RunsFiveTimesAndSurfacesTheLastException()
    {
        // Arrange
        var last = Lost();
        var script = new Script(Lost(), Lost(), Lost(), Lost(), last, Lost());
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunAsync));

        // Assert
        await Assert.That((ReferenceEquals(failure, last), script.Runs)).IsEqualTo((true, 5));
    }

    [Test]
    public async Task RetryOnConflictAsync_EveryRunLosesWithoutAResult_RunsFiveTimesAndSurfacesTheLastException()
    {
        // Arrange
        var last = Lost();
        var script = new Script(Lost(), Lost(), Lost(), Lost(), last, Lost());
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync));

        // Assert
        await Assert.That((ReferenceEquals(failure, last), script.Runs)).IsEqualTo((true, 5));
    }

    [Test]
    public async Task RetryOnConflictAsync_LastRunGetsThrough_ReturnsItsResult()
    {
        // Arrange (the fifth run is the last one, and it counts)
        var script = new Script(Lost(), Lost(), Lost(), Lost());
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync(script.RunAsync);

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task RetryOnConflictAsync_LastException_KeepsTheStackOfTheRun()
    {
        // Arrange
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(ThrowingRunAsync, maxAttempts: 2));

        // Assert (unchanged also means that it was not thrown anew, from where the loop caught it)
        await Assert.That(failure.StackTrace).Contains(nameof(ThrowingRunAsync), StringComparison.Ordinal);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(8)]
    public async Task RetryOnConflictAsync_MaxAttempts_CountsTheFirstRun(int maxAttempts)
    {
        // Arrange
        var script = new Script([.. Enumerable.Range(0, 20).Select(static _ => Lost())]);
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunAsync, maxAttempts: maxAttempts));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(maxAttempts);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(8)]
    public async Task RetryOnConflictAsync_MaxAttemptsWithoutAResult_CountsTheFirstRun(int maxAttempts)
    {
        // Arrange
        var script = new Script([.. Enumerable.Range(0, 20).Select(static _ => Lost())]);
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync, maxAttempts: maxAttempts));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(maxAttempts);
    }

    [Test]
    public async Task RetryOnConflictAsync_LargestMaxAttempts_StopsAtTheFirstSuccess()
    {
        // Arrange
        var script = new Script(Lost());
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync(script.RunAsync, maxAttempts: int.MaxValue);

        // Assert
        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task RetryOnConflictAsync_MaxAttemptsBelowOne_ThrowsAtTheCallAndRunsNothing(int maxAttempts)
    {
        // Arrange
        var script = new Script();
        using var client = NewClient();

        // Act
        var failure = Catch<ArgumentOutOfRangeException>(() => client.RetryOnConflictAsync(script.RunAsync, maxAttempts: maxAttempts));

        // Assert
        await Assert.That((failure.ParamName, script.Runs)).IsEqualTo(("maxAttempts", 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_MaxAttemptsBelowOneWithoutAResult_ThrowsAtTheCallAndRunsNothing()
    {
        // Arrange
        var script = new Script();
        using var client = NewClient();

        // Act
        var failure = Catch<ArgumentOutOfRangeException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync, maxAttempts: 0));

        // Assert
        await Assert.That((failure.ParamName, script.Runs)).IsEqualTo(("maxAttempts", 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegate_ThrowsAtTheCall()
    {
        // Arrange
        using var client = NewClient();
        Func<CancellationToken, ValueTask<int>>? operation = null;

        // Act
        var failure = Catch<ArgumentNullException>(() => client.RetryOnConflictAsync(operation!));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegateWithoutAResult_ThrowsAtTheCall()
    {
        // Arrange
        using var client = NewClient();
        Func<CancellationToken, ValueTask>? operation = null;

        // Act
        var failure = Catch<ArgumentNullException>(() => client.RetryOnConflictAsync(operation!));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_DelegateWithTheRun_GetsTheNumberOfEachRun()
    {
        // Arrange
        var script = new Script(Lost(), Lost());
        var seen = new List<int>();
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync((run, ct) =>
        {
            seen.Add(run);
            return script.RunAsync(ct);
        });

        // Assert
        await Assert.That((result, string.Join(",", seen))).IsEqualTo((3, "1,2,3"));
    }

    [Test]
    public async Task RetryOnConflictAsync_DelegateWithTheRunWithoutAResult_GetsTheNumberOfEachRun()
    {
        // Arrange
        var script = new Script(Lost());
        var seen = new List<int>();
        using var client = NewClient();

        // Act
        await client.RetryOnConflictAsync(async (run, ct) =>
        {
            seen.Add(run);
            await script.RunWithoutResultAsync(ct);
        });

        // Assert
        await Assert.That(string.Join(",", seen)).IsEqualTo("1,2");
    }

    [Test]
    public async Task RetryOnConflictAsync_DelegateWithTheRunAndANamedConflict_SurfacesAnotherConflictAtOnce()
    {
        // Arrange
        var script = new Script(Lost());
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync((_, ct) => script.RunAsync(ct), ConflictKind.TransactionConflict, 5));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(1);
    }

    [Test]
    public async Task RetryOnConflictAsync_DelegateWithTheRunWithoutAResultAndMaxAttempts_CountsTheFirstRun()
    {
        // Arrange
        var script = new Script(Lost(), Lost(), Lost());
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync((_, ct) => script.RunWithoutResultAsync(ct), ConflictKind.Any, 2));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegateWithTheRun_ThrowsAtTheCall()
    {
        // Arrange
        using var client = NewClient();
        Func<int, CancellationToken, ValueTask<int>>? operation = null;

        // Act
        var failure = Catch<ArgumentNullException>(() => client.RetryOnConflictAsync(operation!));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegateWithTheRunWithoutAResult_ThrowsAtTheCall()
    {
        // Arrange
        using var client = NewClient();
        Func<int, CancellationToken, ValueTask>? operation = null;

        // Act
        var failure = Catch<ArgumentNullException>(() => client.RetryOnConflictAsync(operation!));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_DelegateWithTheRunAndMaxAttemptsBelowOne_ThrowsAtTheCallAndRunsNothing()
    {
        // Arrange
        var script = new Script();
        using var client = NewClient();

        // Act
        var failure = Catch<ArgumentOutOfRangeException>(() => client.RetryOnConflictAsync((_, ct) => script.RunAsync(ct), ConflictKind.Any, 0));

        // Assert
        await Assert.That((failure.ParamName, script.Runs)).IsEqualTo(("maxAttempts", 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_LambdaThatEndsInAnAwait_BindsToTheOverloadWithAResult()
    {
        // Arrange (the lambda fits both overloads, and a result that went missing here would not show in a run)
        var script = new Script();
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync(async ct => await script.RunAsync(ct));

        // Assert
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task RetryOnConflictAsync_EveryRun_GetsTheTokenOfTheCaller()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var seen = new List<CancellationToken>();
        var script = new Script(Lost());
        using var client = NewClient();

        // Act
        await client.RetryOnConflictAsync(
            ct =>
            {
                seen.Add(ct);
                return script.RunAsync(ct);
            },
            token);

        // Assert
        await Assert.That(seen.All(ct => ct == token) && seen.Count == 2).IsTrue();
    }

    [Test]
    public async Task RetryOnConflictAsync_EveryRunWithoutAResult_GetsTheTokenOfTheCaller()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var seen = new List<CancellationToken>();
        var script = new Script(Lost());
        using var client = NewClient();

        // Act
        await client.RetryOnConflictAsync(
            ct =>
            {
                seen.Add(ct);
                return script.RunWithoutResultAsync(ct);
            },
            token);

        // Assert
        await Assert.That(seen.All(ct => ct == token) && seen.Count == 2).IsTrue();
    }

    [Test]
    public async Task RetryOnConflictAsync_RunNumberAndAToken_GiveTheTokenToTheRunsOfBothShapes()
    {
        // Arrange (the token goes positionally after the delegate, to the form without retryOn and maxAttempts)
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        List<CancellationToken> seen = [];
        using var client = NewClient();

        ValueTask<int> Counted(CancellationToken ct)
        {
            seen.Add(ct);
            return new(1);
        }

        ValueTask Noted(CancellationToken ct)
        {
            seen.Add(ct);
            return ValueTask.CompletedTask;
        }

        // Act
        await client.RetryOnConflictAsync((_, ct) => Counted(ct), token);
        await client.RetryOnConflictAsync((_, ct) => Noted(ct), token);

        // Assert
        await Assert.That(seen.Count == 2 && seen.All(ct => ct == token)).IsTrue();
    }

    [Test]
    public async Task RetryOnConflictAsync_MaxAttemptsAndToken_GiveTheTokenToTheRuns()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        CancellationToken seen = default;
        using var client = NewClient();

        // Act
        await client.RetryOnConflictAsync(
            ct =>
            {
                seen = ct;
                return ValueTask.CompletedTask;
            },
            maxAttempts: 8,
            cancellationToken: token);

        // Assert
        await Assert.That(seen == token).IsTrue();
    }

    [Test]
    public async Task RetryOnConflictAsync_WaitsBetweenRuns_DoubleFromASmallBaseOnTheClockOfTheClient()
    {
        // Arrange (the jitter at its largest, so these are the ceilings; nothing waits before the first run or after the last)
        var time = new ManualTimeProvider();
        var script = new Script(Lost(), Lost(), Lost(), Lost(), Lost());
        using var client = NewClient(time: time, random: FixedRandom.One);

        // Act
        var call = client.RetryOnConflictAsync(script.RunAsync);
        var delays = await RetryTestSupport.AdvanceThroughDelaysAsync(call, time, AttemptTimeout);

        // Assert
        await Assert.That(delays.Select(static delay => delay.TotalMilliseconds).ToArray()).IsEquivalentTo([10.0, 20.0, 40.0, 80.0]);
    }

    [Test]
    public async Task RetryOnConflictAsync_WaitsBetweenRuns_ComeFromTheRandomSourceOfTheRetryPolicy()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new Script(Lost(), Lost());
        using var client = NewClient(time: time, random: new FixedRandom(0.5));

        // Act
        var call = client.RetryOnConflictAsync(script.RunAsync);
        var delays = await RetryTestSupport.AdvanceThroughDelaysAsync(call, time, AttemptTimeout);

        // Assert
        await Assert.That(delays.Select(static delay => delay.TotalMilliseconds).ToArray()).IsEquivalentTo([5.0, 10.0]);
    }

    [Test]
    public async Task RetryOnConflictAsync_ManyRuns_WaitNoLongerThanASecond()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new Script([.. Enumerable.Range(0, 9).Select(static _ => Lost())]);
        using var client = NewClient(time: time, random: FixedRandom.One);

        // Act
        var call = client.RetryOnConflictAsync(script.RunAsync, maxAttempts: 10);
        var delays = await RetryTestSupport.AdvanceThroughDelaysAsync(call, time, AttemptTimeout);

        // Assert
        await Assert.That(delays.TakeLast(3).Select(static delay => delay.TotalMilliseconds).ToArray()).IsEquivalentTo([640.0, 1000.0, 1000.0]);
    }

    [Test]
    public async Task RetryOnConflictAsync_WaitNotOverYet_DoesNotRunAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new Script(Lost());
        using var client = NewClient(time: time, random: FixedRandom.One);
        var call = client.RetryOnConflictAsync(script.RunAsync);

        // Act
        time.Advance(TimeSpan.FromMilliseconds(9));
        var runs = script.Runs;
        time.Advance(TimeSpan.FromMilliseconds(1));
        await call;

        // Assert
        await Assert.That((runs, script.Runs)).IsEqualTo((1, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_CancelledDuringAWait_EndsItAtOnceWithTheTokenOfTheCaller()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new Script(Lost());
        using var client = NewClient(time: time, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var call = client.RetryOnConflictAsync(script.RunAsync, token);

        // Act (the clock stands still: only the cancellation can end the wait)
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That((failure.CancellationToken == token, script.Runs)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task RetryOnConflictAsync_CancelledDuringAWaitWithoutAResult_EndsItAtOnceWithTheTokenOfTheCaller()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var script = new Script(Lost());
        using var client = NewClient(time: time, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var call = client.RetryOnConflictAsync(script.RunWithoutResultAsync, token);

        // Act
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That((failure.CancellationToken == token, script.Runs)).IsEqualTo((true, 1));
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(1.0)]
    public async Task RetryOnConflictAsync_CancelledBeforeARunLoses_RunsNoMore(double jitter)
    {
        // Arrange (a delegate that does not look at the token, and a jitter of no wait at all, which must not skip the check either)
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var script = new Script(Lost());
        using var client = NewClient(time: new ManualTimeProvider(), random: new FixedRandom(jitter));

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(() => client.RetryOnConflictAsync(script.RunAsync, cancellation.Token));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(1);
    }

    [Test]
    public async Task RetryOnConflictAsync_ManyConflicts_TakeNothingFromTheRetryQuota()
    {
        // Arrange (the quota lasts for a hundred retries, and a conflict that took from it would leave none for the call at the end)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, random: FixedRandom.Zero, maxAttempts: 2);
        var script = new Script([.. Enumerable.Range(0, 120).Select(static _ => Lost())]);
        await client.RetryOnConflictAsync(script.RunAsync, maxAttempts: 200);
        handler.Enqueue(RetryTestSupport.ServerError());
        handler.Enqueue(RetryTestSupport.Success());

        // Act
        var got = await client.Table(TableName, "pk").Get("a").SendAsync();

        // Assert
        await Assert.That((got.Found, handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_CallsOfARun_KeepTheirOwnAttempts()
    {
        // Arrange (the first run sends twice, as a call that is allowed two attempts does; the second gets through at once)
        using var handler = new StubHandler();
        handler.Enqueue(RetryTestSupport.ServerError());
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero, maxAttempts: 2);
        var shop = client.Table(TableName, "pk");

        // Act
        await client.RetryOnConflictAsync(async ct => await shop.Put(new Item { ["pk"] = "a" }).SendAsync(ct));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task RetryOnConflictAsync_RejectedConditionOnTheWire_RunsAgain()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table(TableName, "pk");

        // Act
        await client.RetryOnConflictAsync(async ct => await shop.Put(new Item { ["pk"] = "a" }).IfNotExists().SendAsync(ct));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_ItemInATransactionOnTheWire_RunsAgain()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("TransactionConflictException", "Transaction is ongoing for the item."));
        handler.Enqueue(RetryTestSupport.Success());
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table(TableName, "pk");

        // Act
        var got = await client.RetryOnConflictAsync(async ct => await shop.Get("a").SendAsync(ct));

        // Assert
        await Assert.That((got.Found, handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    [Arguments(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed)]
    [Arguments(TransactionFailure.TransactionConflict, TransactionFailure.None)]
    [Arguments(TransactionFailure.Throttled, TransactionFailure.TransactionConflict)]
    public async Task RetryOnConflictAsync_MixOfReasonsOnTheWireWithAConflict_RunsAgain(TransactionFailure first, TransactionFailure second)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(first, second));
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await client.RetryOnConflictAsync(ct => SendTransactionAsync(client, ct));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(TransactionFailure.None, TransactionFailure.Throttled)]
    [Arguments(TransactionFailure.ValidationError, TransactionFailure.None)]
    [Arguments(TransactionFailure.Throttled, TransactionFailure.ValidationError)]
    public async Task RetryOnConflictAsync_MixOfReasonsOnTheWireWithoutAConflict_SurfacesAtOnce(TransactionFailure first, TransactionFailure second)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(first, second));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<TransactionCanceledException>(() => client.RetryOnConflictAsync(ct => SendTransactionAsync(client, ct)));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RetryOnConflictAsync_BusinessRefusalCaughtInTheDelegate_EndsWithItsResult()
    {
        // Arrange (the check on the second action cannot be read, so the delegate turns its failure into a result, and the first action stays a conflict)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.ConditionalCheckFailed, TransactionFailure.None));
        handler.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table(TableName, "pk");

        // Act
        var accepted = await client.RetryOnConflictAsync(
            async ct =>
            {
                TransactMarker refused = default;
                try
                {
                    await client.TransactWrite().Put(shop, new Item { ["pk"] = "a" }).Check(shop, "b", default, out refused).If(Expr.Attr("open").Eq(true)).SendAsync(ct);
                    return true;
                }
                catch (TransactionCanceledException exception) when (exception.ReasonFor(refused) == TransactionFailure.ConditionalCheckFailed)
                {
                    return false;
                }
            });

        // Assert
        await Assert.That((accepted, handler.Requests.Count)).IsEqualTo((false, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_RunThatDoesNotCompleteAtOnce_ResumesWithoutTheContextOfTheCaller()
    {
        // Arrange
        var context = new CountingSynchronizationContext();
        var run = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = NewClient();

        // Act
        var call = context.Start(() => client.RetryOnConflictAsync(_ => new ValueTask<int>(run.Task)));
        run.SetResult(1);
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RetryOnConflictAsync_RunWithoutAResultThatDoesNotCompleteAtOnce_ResumesWithoutTheContextOfTheCaller()
    {
        // Arrange
        var context = new CountingSynchronizationContext();
        var run = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = NewClient();

        // Act
        var call = context.Start(() => client.RetryOnConflictAsync(_ => new ValueTask(run.Task)));
        run.SetResult();
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RetryOnConflictAsync_WaitBetweenRuns_ResumesWithoutTheContextOfTheCaller()
    {
        // Arrange
        var context = new CountingSynchronizationContext();
        var time = new ManualTimeProvider();
        var script = new Script(Lost());
        using var client = NewClient(time: time, random: FixedRandom.One);

        // Act
        var call = context.Start(() => client.RetryOnConflictAsync(script.RunAsync));
        time.Advance(TimeSpan.FromMilliseconds(10));
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(nameof(NamedConflicts))]
    public async Task RetryOnConflictAsync_ConflictThatRetryOnNames_RunsAgain(KindCase named)
    {
        // Arrange
        var script = new Script(named.Exception);
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync(script.RunAsync, named.RetryOn);

        // Assert
        await Assert.That((result, script.Runs)).IsEqualTo((2, 2));
    }

    [Test]
    [MethodDataSource(nameof(UnnamedConflicts))]
    public async Task RetryOnConflictAsync_ConflictThatRetryOnDoesNotName_SurfacesAtOnce(KindCase unnamed)
    {
        // Arrange
        var script = new Script(unnamed.Exception);
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(() => client.RetryOnConflictAsync(script.RunAsync, unnamed.RetryOn));

        // Assert
        await Assert.That((ReferenceEquals(failure, unnamed.Exception), script.Runs)).IsEqualTo((true, 1));
    }

    [Test]
    [MethodDataSource(nameof(Conflicts))]
    public async Task RetryOnConflictAsync_Any_RunsAgainOnWhatTheOverloadWithoutItRunsAgainOn(FailureCase conflict)
    {
        // Arrange
        var script = new Script(conflict.Exception);
        using var client = NewClient();

        // Act
        var result = await client.RetryOnConflictAsync(script.RunAsync, ConflictKind.Any);

        // Assert
        await Assert.That((result, script.Runs)).IsEqualTo((2, 2));
    }

    [Test]
    [MethodDataSource(nameof(OtherFailures))]
    public async Task RetryOnConflictAsync_AnyAndOtherFailure_SurfacesAtOnce(FailureCase other)
    {
        // Arrange
        var script = new Script(other.Exception);
        using var client = NewClient();

        // Act
        var failure = await TestClient.CaptureAsync<Exception>(() => client.RetryOnConflictAsync(script.RunAsync, ConflictKind.Any));

        // Assert
        await Assert.That((ReferenceEquals(failure, other.Exception), script.Runs)).IsEqualTo((true, 1));
    }

    [Test]
    [MethodDataSource(nameof(NamedConflicts))]
    public async Task RetryOnConflictAsync_NamedConflictWithoutAResult_RunsAgain(KindCase named)
    {
        // Arrange
        var script = new Script(named.Exception);
        using var client = NewClient();

        // Act
        await client.RetryOnConflictAsync(script.RunWithoutResultAsync, named.RetryOn);

        // Assert
        await Assert.That(script.Runs).IsEqualTo(2);
    }

    [Test]
    public async Task RetryOnConflictAsync_UnnamedConflictWithoutAResult_SurfacesAtOnce()
    {
        // Arrange
        var script = new Script(Lost());
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync, ConflictKind.TransactionConflict));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(1);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(8)]
    public async Task RetryOnConflictAsync_RetryOnAndMaxAttempts_CountsTheFirstRun(int maxAttempts)
    {
        // Arrange
        var script = new Script([.. Enumerable.Range(0, 20).Select(static _ => new TransactionConflictException("Transaction is ongoing for the item."))]);
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<TransactionConflictException>(() => client.RetryOnConflictAsync(script.RunAsync, ConflictKind.TransactionConflict, maxAttempts));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(maxAttempts);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(8)]
    public async Task RetryOnConflictAsync_RetryOnAndMaxAttemptsWithoutAResult_CountsTheFirstRun(int maxAttempts)
    {
        // Arrange
        var script = new Script([.. Enumerable.Range(0, 20).Select(static _ => Lost())]);
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync, ConflictKind.ConditionalCheckFailed, maxAttempts));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(maxAttempts);
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryOnEveryRunLoses_RunsFiveTimes()
    {
        // Arrange
        var script = new Script([.. Enumerable.Range(0, 20).Select(static _ => Lost())]);
        using var client = NewClient();

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(script.RunAsync, ConflictKind.ConditionalCheckFailed));

        // Assert
        await Assert.That(script.Runs).IsEqualTo(5);
    }

    [Test]
    [Arguments(0)]
    [Arguments(4)]
    [Arguments(7)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    [Arguments(int.MaxValue)]
    public async Task RetryOnConflictAsync_RetryOnThatIsNoneOfTheThree_ThrowsAtTheCallAndRunsNothing(int value)
    {
        // Arrange (none, a bit that names nothing, and Any with such a bit)
        var script = new Script();
        using var client = NewClient();

        // Act
        var failure = Catch<ArgumentOutOfRangeException>(() => client.RetryOnConflictAsync(script.RunAsync, (ConflictKind)value));

        // Assert
        await Assert.That((failure.ParamName, failure.ActualValue, script.Runs)).IsEqualTo(("retryOn", (object?)(ConflictKind)value, 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryOnThatIsNoneOfTheThreeWithoutAResult_ThrowsAtTheCallAndRunsNothing()
    {
        // Arrange
        var script = new Script();
        using var client = NewClient();

        // Act
        var failure = Catch<ArgumentOutOfRangeException>(() => client.RetryOnConflictAsync(script.RunWithoutResultAsync, default(ConflictKind), 3));

        // Assert
        await Assert.That((failure.ParamName, script.Runs)).IsEqualTo(("retryOn", 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryOnAndMaxAttemptsBelowOne_ThrowsForMaxAttempts()
    {
        // Arrange
        var script = new Script();
        using var client = NewClient();

        // Act
        var failure = Catch<ArgumentOutOfRangeException>(() => client.RetryOnConflictAsync(script.RunAsync, ConflictKind.TransactionConflict, 0));

        // Assert
        await Assert.That((failure.ParamName, script.Runs)).IsEqualTo(("maxAttempts", 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_NullDelegateAndRetryOnOfNoKind_ThrowsForTheDelegate()
    {
        // Arrange
        using var client = NewClient();
        Func<CancellationToken, ValueTask>? operation = null;

        // Act
        var failure = Catch<ArgumentNullException>(() => client.RetryOnConflictAsync(operation!, default(ConflictKind)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("operation");
    }

    [Test]
    public async Task RetryOnConflictAsync_TransactionConflictOnTheWire_RunsTheRegistrationAgainUntilTheValueIsTaken()
    {
        // Arrange (the uniqueness recipe: a claim in flight elsewhere is asked again, and a taken claim is the answer, also when the record's own condition failed beside it)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.TransactionConflict, TransactionFailure.None));
        handler.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.ConditionalCheckFailed, TransactionFailure.ConditionalCheckFailed));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var shop = client.Table(TableName, "pk");

        // Act
        var registered = await client.RetryOnConflictAsync(
            async ct =>
            {
                TransactMarker claim = default;
                try
                {
                    await client.TransactWrite().Put(shop, new Item { ["pk"] = "EMAIL#a" }, out claim).IfNotExists().Put(shop, new Item { ["pk"] = "USER#a" }).IfNotExists().SendAsync(ct);
                    return true;
                }
                catch (TransactionCanceledException exception) when (exception.ReasonFor(claim) == TransactionFailure.ConditionalCheckFailed)
                {
                    return false;
                }
            },
            ConflictKind.TransactionConflict);

        // Assert
        await Assert.That((registered, handler.Requests.Count)).IsEqualTo((false, 2));
    }

    [Test]
    public async Task RetryOnConflictAsync_FailedConditionOfTheRecordOnTheWire_SurfacesAtOnce()
    {
        // Arrange (what the loop by hand let through: the record exists, and asking again changes nothing)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(() => client.RetryOnConflictAsync(ct => SendTransactionAsync(client, ct), ConflictKind.TransactionConflict));

        // Assert
        await Assert.That((failure.Failures.Single().Reason, handler.Requests.Count)).IsEqualTo((TransactionFailure.ConditionalCheckFailed, 1));
    }

    private static FailureCase Canceled(params string[] codes) =>
        new($"TransactionCanceledException with {string.Join(", ", codes)}", new TransactionCanceledException("Transaction cancelled.", [.. codes.Select(static code => new CancellationReason { Code = code })]));

    private static ConditionalCheckFailedException Lost() => new("The conditional request failed.");

    private static DynamoDbClient NewClient(TimeProvider? time = null, Random? random = null) =>
        TestClient.Create(new DelegateHandler(static (_, _) => throw new InvalidOperationException("A test of the loop sends nothing.")), time, attemptTimeout: AttemptTimeout, random: random ?? FixedRandom.Zero);

    private static async ValueTask<int> ThrowingRunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw Lost();
    }

    private static async ValueTask SendTransactionAsync(DynamoDbClient client, CancellationToken cancellationToken)
    {
        var shop = client.Table(TableName, "pk");
        await client.TransactWrite().Put(shop, new Item { ["pk"] = "a" }).Put(shop, new Item { ["pk"] = "b" }).SendAsync(cancellationToken);
    }

    private static TException Catch<TException>(Action act)
        where TException : Exception
    {
        try
        {
            act();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"The action did not throw {typeof(TException).Name}.");
    }

    /// <summary>One failure of a run. The display name in test output is <paramref name="Name"/>.</summary>
    /// <param name="Name">The name shown in test output.</param>
    /// <param name="Exception">What the run fails with.</param>
    internal readonly record struct FailureCase(string Name, Exception Exception)
    {
        public override string ToString() => Name;
    }

    /// <summary>A failure of a run and the conflicts a retry names. The display name in test output is <paramref name="Name"/>.</summary>
    /// <param name="Name">The name shown in test output.</param>
    /// <param name="RetryOn">What the retry runs again on.</param>
    /// <param name="Exception">What the run fails with.</param>
    internal readonly record struct KindCase(string Name, ConflictKind RetryOn, Exception Exception)
    {
        public override string ToString() => Name;
    }

    /// <summary>A delegate that fails with these exceptions, one per run, and then returns the number of the run.</summary>
    private sealed class Script(params Exception[] failures)
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public ValueTask<int> RunAsync(CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref _runs);
            return run <= failures.Length ? ValueTask.FromException<int>(failures[run - 1]) : ValueTask.FromResult(run);
        }

        public async ValueTask RunWithoutResultAsync(CancellationToken cancellationToken) => await RunAsync(cancellationToken);
    }
}
