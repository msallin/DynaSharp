using System.Net;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

public sealed class DynamoDbPipelineExpiredCredentialsTests
{
    private static readonly Func<string, string?> ContainerEnvironment = Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/credentials"));

    [Test]
    public async Task ExecuteAsync_ClockFifteenMinutesBehind_RefreshesBeforeTheCredentialsExpire()
    {
        // Arrange (55 minutes on: 5 minutes are left on the clock of the service, and 20 on the local clock)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time, TimeSpan.FromMinutes(15));
        using var client = ClientOverTheDefaultChain(account, time);
        await client.GetItemAsync(TestClient.GetItem());
        time.Advance(TimeSpan.FromMinutes(55));

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((account.KeysUsed[^1], account.Fetches)).IsEqualTo(("key-2", 2));
    }

    [Test]
    public async Task ExecuteAsync_ClockFifteenMinutesBehindAtTheExpiry_NeverSendsExpiredCredentials()
    {
        // Arrange (the credentials expire 45 minutes from now on the clock of the service, and 60 on the local clock)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time, TimeSpan.FromMinutes(15));
        using var client = ClientOverTheDefaultChain(account, time);
        await client.GetItemAsync(TestClient.GetItem());
        time.Advance(TimeSpan.FromMinutes(61));

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert (the skew, the first success, and one request with new credentials)
        await Assert.That(string.Join(',', account.KeysUsed)).IsEqualTo("key-1,key-1,key-2");
    }

    [Test]
    public async Task ListStreamsAsync_ClockFifteenMinutesBehind_RefreshesBeforeTheCredentialsExpire()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time, TimeSpan.FromMinutes(15)) { SuccessJson = "{}" };
        using var client = new DynamoDbStreamsClient(
            TestClient.Options(account.Service, time, maxAttempts: 3) with { Credentials = null },
            ContainerEnvironment,
            null,
            null,
            FixedRandom.Zero,
            NoFiles,
            credentialsHandler: account.Endpoint);
        await client.ListStreamsAsync(TestClient.ListStreams());
        time.Advance(TimeSpan.FromMinutes(55));

        // Act
        await client.ListStreamsAsync(TestClient.ListStreams());

        // Assert
        await Assert.That((account.KeysUsed[^1], account.Fetches)).IsEqualTo(("key-2", 2));
    }

    [Test]
    [Arguments("ExpiredTokenException")]
    [Arguments("ExpiredToken")]
    public async Task ExecuteAsync_ExpiredResponse_RefreshesAndSucceeds(string errorCode)
    {
        // Arrange (the credentials look good for another hour, and the service rejects them anyway)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time) { ExpiredCode = errorCode };
        using var client = CreateClient(account.Service, time, credentials: ContainerProvider(account, time));
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((response.Item!["pk"].AsString(), string.Join(',', account.KeysUsed), account.Fetches)).IsEqualTo(("user-1", "key-1,key-1,key-2", 2));
    }

    [Test]
    public async Task ExecuteAsync_ExpiredResponseOverTheDefaultChain_RefreshesAndSucceeds()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = ClientOverTheDefaultChain(account, time);
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', account.KeysUsed)).IsEqualTo("key-1,key-1,key-2");
    }

    [Test]
    public async Task ListStreamsAsync_ExpiredResponse_RefreshesAndSucceeds()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time) { SuccessJson = "{}" };
        using var client = TestClient.CreateStreams(account.Service, time, credentials: ContainerProvider(account, time), maxAttempts: 3, random: FixedRandom.Zero);
        await client.ListStreamsAsync(TestClient.ListStreams());
        account.Revoke("key-1");

        // Act
        await client.ListStreamsAsync(TestClient.ListStreams());

        // Assert
        await Assert.That(string.Join(',', account.KeysUsed)).IsEqualTo("key-1,key-1,key-2");
    }

    [Test]
    public async Task GetRecordsAsync_ExpiredIterator_IsNotAnExpiredCredential()
    {
        // Arrange (DynamoDB Streams also has an error with "Expired" in its name, about something else)
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ExpiredIteratorException", "Iterator expired.") };
        var provider = new SwappingProvider();
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), credentials: provider, maxAttempts: 3, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<ExpiredIteratorException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((provider.Rejected.Count, handler.Requests.Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task ExecuteAsync_SecondExpiry_SurfacesTheExceptionAfterOneResend()
    {
        // Arrange (attempts are left, and a stale identity must not use them)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        account.RejectEverything = true;
        using var client = CreateClient(account.Service, time, maxAttempts: 10, credentials: ContainerProvider(account, time));

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, string.Join(',', account.KeysUsed))).IsEqualTo(("ExpiredTokenException", "key-1,key-2"));
    }

    [Test]
    public async Task ExecuteAsync_ThrottledBetweenTwoExpiries_StillResendsOnlyOnce()
    {
        // Arrange (the throttled attempt does not earn the call another resend)
        var provider = new SwappingProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Expired());
        handler.Enqueue(Throttled());
        handler.Enqueue(Expired());
        handler.DefaultResponse = Success();
        using var client = CreateClient(handler, maxAttempts: 10, credentials: provider);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("ExpiredTokenException", 3));
    }

    [Test]
    public async Task ExecuteAsync_SecondExpiry_DropsTheSecondCredentialsForTheNextCall()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        account.Revoke("key-1");
        account.Revoke("key-2");
        using var client = CreateClient(account.Service, time, credentials: ContainerProvider(account, time));
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', account.KeysUsed)).IsEqualTo("key-1,key-2,key-3");
    }

    [Test]
    public async Task ExecuteAsync_ManyConcurrentExpiries_FetchOnceAndAllSucceed()
    {
        // Arrange (all calls are on the wire with the same credentials before the first one gets its rejection)
        const int calls = 8;
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = CreateClient(account.Service, time, credentials: ContainerProvider(account, time));
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");
        var arrived = 0;
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        account.BeforeAnswer = () =>
        {
            if (Interlocked.Increment(ref arrived) == calls)
            {
                allArrived.SetResult();
            }

            return allArrived.Task;
        };

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, calls).Select(_ => client.GetItemAsync(TestClient.GetItem())));

        // Assert (the first call, then every call twice)
        await Assert.That((responses.Length, account.Fetches, account.KeysUsed.Count(key => key == "key-2"))).IsEqualTo((calls, 2, calls));
    }

    [Test]
    public async Task ExecuteAsync_NewerCredentialsAlreadyCached_ResendsWithoutFetching()
    {
        // Arrange (one call is rejected after another call has already replaced the credentials it used)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = CreateClient(account.Service, time, credentials: ContainerProvider(account, time));
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");
        var onTheWire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        account.BeforeAnswer = () =>
        {
            account.BeforeAnswer = null;
            onTheWire.SetResult();
            return answer.Task;
        };
        var slow = client.GetItemAsync(TestClient.GetItem());
        await onTheWire.Task;
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        answer.SetResult();
        await slow;

        // Assert
        await Assert.That((account.KeysUsed[^1], account.Fetches)).IsEqualTo(("key-2", 2));
    }

    [Test]
    public async Task ExecuteAsync_StaticProvider_SurfacesTheExceptionUnchanged()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Expired("request-1") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, exception.Message, exception.StatusCode, exception.RequestId, handler.Requests.Count))
            .IsEqualTo(("ExpiredTokenException", "The security token included in the request is expired", HttpStatusCode.BadRequest, "request-1", 1));
    }

    [Test]
    public async Task ExecuteAsync_CustomProviderThatCannotBeRefreshed_SurfacesTheException()
    {
        // Arrange
        var provider = new CountingCredentialsProvider();
        using var handler = new StubHandler { DefaultResponse = Expired() };
        using var client = CreateClient(handler, credentials: provider);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count, provider.Count)).IsEqualTo(("ExpiredTokenException", 1, 1));
    }

    [Test]
    public async Task ExecuteAsync_DefaultChainOnStaticKeys_SurfacesTheException()
    {
        // Arrange (the environment has keys: the chain implements the capability, its source does not)
        using var handler = new StubHandler { DefaultResponse = Expired() };
        var environment = Env(("AWS_ACCESS_KEY_ID", "AKIAIOSFODNN7EXAMPLE"), ("AWS_SECRET_ACCESS_KEY", "secret"), ("AWS_SESSION_TOKEN", "token"));
        using var client = new DynamoDbClient(TestClient.Options(handler, maxAttempts: 3) with { Credentials = null }, environment, null, null, FixedRandom.Zero, NoFiles);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("ExpiredTokenException", 1));
    }

    [Test]
    public async Task ExecuteAsync_ProviderThatHasNothingElse_SurfacesTheException()
    {
        // Arrange
        var provider = new SwappingProvider { HasOtherCredentials = false };
        using var handler = new StubHandler { DefaultResponse = Expired() };
        using var client = CreateClient(handler, credentials: provider);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count, provider.Rejected.Count)).IsEqualTo(("ExpiredTokenException", 1, 1));
    }

    [Test]
    public async Task ExecuteAsync_RefreshableProviderOfTheApplication_GetsTheInstanceThatWasRejected()
    {
        // Arrange
        var provider = new SwappingProvider();
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);
        using var handler = new StubHandler();
        handler.Enqueue(Expired());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, credentials: provider);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(provider.Rejected.Single()).IsSameReferenceAs(rejected);
    }

    [Test]
    public async Task ExecuteAsync_ProviderFailsToRefresh_SurfacesTheCredentialsFailure()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = CreateClient(account.Service, time, credentials: ContainerProvider(account, time));
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");
        account.EndpointBroken = true;

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert (the request is not sent again without credentials)
        await Assert.That((exception.Message.Contains("unusable response", StringComparison.Ordinal), account.KeysUsed.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task ExecuteAsync_ProviderRecoversAfterAFailedRefresh_TheNextCallSucceeds()
    {
        // Arrange (the rejected credentials stay dropped, so the next call does not try them again)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = CreateClient(account.Service, time, credentials: ContainerProvider(account, time));
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");
        account.EndpointBroken = true;
        await TestClient.CaptureAsync<CredentialsUnavailableException>(() => client.GetItemAsync(TestClient.GetItem()));
        account.EndpointBroken = false;

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', account.KeysUsed)).IsEqualTo("key-1,key-1,key-2");
    }

    [Test]
    public async Task ExecuteAsync_RetriesSwitchedOff_SurfacesTheExceptionButTheNextCallHasNewCredentials()
    {
        // Arrange (1 must mean one request, and the rejection still tells the provider what it needs to know)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = CreateClient(account.Service, time, maxAttempts: 1, credentials: ContainerProvider(account, time));
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((exception.ErrorCode, string.Join(',', account.KeysUsed))).IsEqualTo(("ExpiredTokenException", "key-1,key-1,key-2"));
    }

    [Test]
    public async Task ExecuteAsync_ResendCountsAsAnAttempt_LeavesNoAttemptForALaterFailure()
    {
        // Arrange (two attempts: the expired one, then a 500)
        using var handler = new StubHandler();
        handler.Enqueue(Expired());
        handler.DefaultResponse = ServerError();
        using var client = CreateClient(handler, maxAttempts: 2, credentials: new SwappingProvider());

        // Act
        var exception = await TestClient.CaptureAsync<InternalServerErrorException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("InternalServerError", 2));
    }

    [Test]
    public async Task ExecuteAsync_ResendWithMaximumJitter_SendsAgainWithoutWaiting()
    {
        // Arrange (the clock of the test never moves, so a wait would leave the call pending)
        using var handler = new StubHandler();
        handler.Enqueue(Expired());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, random: FixedRandom.One, credentials: new SwappingProvider());

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(call.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_Resend_UsesNoQuota()
    {
        // Arrange (the resend is free: afterwards the client still has all its tokens)
        using var handler = new StubHandler();
        handler.Enqueue(Expired());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, maxAttempts: int.MaxValue, credentials: new SwappingProvider());
        await client.GetItemAsync(TestClient.GetItem());
        handler.DefaultResponse = Throttled();
        var before = handler.Requests.Count;

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert (100 retries of 5 tokens)
        await Assert.That(handler.Requests.Count - before).IsEqualTo(101);
    }

    [Test]
    public async Task ExecuteAsync_ResendAfterARetry_KeepsTheQuotaOfThatRetryForTheRefund()
    {
        // Arrange (a throttled attempt takes 5 tokens; the expired attempt that follows takes none; the success gives the 5 back)
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Expired());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, maxAttempts: int.MaxValue, credentials: new SwappingProvider());
        await client.GetItemAsync(TestClient.GetItem());
        handler.DefaultResponse = Throttled();
        var before = handler.Requests.Count;

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert (full quota again: 100 retries)
        await Assert.That(handler.Requests.Count - before).IsEqualTo(101);
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrectedAndCredentialsExpired_ResendsForBoth()
    {
        // Arrange (the first attempt finds the wrong clock, the second one the expired credentials)
        var time = new ManualTimeProvider();
        using var account = new ExpiringCredentialsAccount(time, TimeSpan.FromMinutes(15));
        using var client = ClientOverTheDefaultChain(account, time);
        account.Revoke("key-1");

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', account.KeysUsed)).IsEqualTo("key-1,key-1,key-2");
    }

    [Test]
    public async Task ExecuteAsync_ForcedRefresh_LogsOneEventWithoutSecrets()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        using var account = new ExpiringCredentialsAccount(time);
        using var client = ClientOverTheDefaultChain(account, time, loggerFactory: logs);
        await client.GetItemAsync(TestClient.GetItem());
        account.Revoke("key-1");

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(logs.Entries.Where(entry => entry.EventId.Id > 20).Select(entry => (entry.EventId.Id, entry.Level, entry.Category, entry.Message)).Single()).IsEqualTo((
            23,
            LogLevel.Warning,
            "DynaSharp.Credentials.ContainerCredentialsProvider",
            "DynamoDB rejected the credentials from the container credentials endpoint as expired. They are dropped and fetched again."));
    }

    [Test]
    public async Task ExecuteAsync_StaleIdentity_NoSecretReachesTheLogsOrTheException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var logs = new CapturingLoggerFactory();
        using var account = new ExpiringCredentialsAccount(time);
        account.RejectEverything = true;
        using var client = ClientOverTheDefaultChain(account, time, loggerFactory: logs);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        var text = string.Join('\n', logs.Entries.Select(entry => entry.Message)) + exception;
        await Assert.That(text.Contains(ExpiringCredentialsAccount.SecretPrefix, StringComparison.Ordinal) || text.Contains(ExpiringCredentialsAccount.TokenPrefix, StringComparison.Ordinal)).IsFalse();
    }

    private static StubResponse Expired(string? requestId = null) =>
        StubResponse.DynamoDbError("ExpiredTokenException", "The security token included in the request is expired", requestId: requestId);

    private static ContainerCredentialsProvider ContainerProvider(ExpiringCredentialsAccount account, TimeProvider time) =>
        new(ContainerEnvironment, NoFiles, account.Endpoint, time, NullLogger.Instance);

    private static DynamoDbClient ClientOverTheDefaultChain(ExpiringCredentialsAccount account, ManualTimeProvider time, ILoggerFactory? loggerFactory = null) =>
        new(
            TestClient.Options(account.Service, time, maxAttempts: 3, loggerFactory: loggerFactory) with { Credentials = null },
            ContainerEnvironment,
            null,
            null,
            FixedRandom.Zero,
            NoFiles,
            credentialsHandler: account.Endpoint);

    /// <summary>A provider of the application that hands out a new instance whenever the current one is dropped.</summary>
    private sealed class SwappingProvider : IRefreshableCredentialsProvider
    {
        private readonly Lock _gate = new();
        private readonly List<AwsCredentials> _rejected = [];
        private AwsCredentials _current = new("first", "secret");

        /// <summary>Gets a value indicating whether the provider has other credentials to offer.</summary>
        public bool HasOtherCredentials { get; init; } = true;

        /// <summary>Gets what the provider was told about.</summary>
        public IReadOnlyList<AwsCredentials> Rejected
        {
            get
            {
                lock (_gate)
                {
                    return [.. _rejected];
                }
            }
        }

        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return ValueTask.FromResult(_current);
            }
        }

        public bool TryInvalidate(AwsCredentials rejected)
        {
            lock (_gate)
            {
                _rejected.Add(rejected);
                _current = new AwsCredentials("next", "secret");
                return HasOtherCredentials;
            }
        }
    }
}
