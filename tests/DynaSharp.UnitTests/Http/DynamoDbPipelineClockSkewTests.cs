using System.Globalization;
using System.Net;
using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

// A DynamoDB that rejects requests signed more than five minutes away from its own clock is simulated by a responder that reads
// X-Amz-Date, so the tests exercise the signing time of every attempt and not just a canned error.
public sealed class DynamoDbPipelineClockSkewTests
{
    [Test]
    public async Task ExecuteAsync_LocalClockBehindTheService_CorrectsTheClockAndReturnsTheResponse()
    {
        // Arrange (the service is 15 minutes ahead)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task ExecuteAsync_LocalClockBehindTheService_SendsAgainWithTheServiceTime()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', service.Handler.Requests.Select(request => request.Headers["X-Amz-Date"]))).IsEqualTo("20261031T123456Z,20261031T124956Z");
    }

    [Test]
    public async Task ExecuteAsync_LocalClockAheadOfTheService_CorrectsTheClockBackwards()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(-20));
        using var client = CreateClient(service.Handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', service.Handler.Requests.Select(request => request.Headers["X-Amz-Date"]))).IsEqualTo("20261031T123456Z,20261031T121456Z");
    }

    [Test]
    [Arguments("RequestTimeTooSkewed")]
    [Arguments("RequestExpired")]
    [Arguments("RequestInTheFuture")]
    [Arguments("InvalidSignatureException")]
    [Arguments("SignatureDoesNotMatch")]
    [Arguments("AuthFailure")]
    public async Task ExecuteAsync_ErrorCodeOfAClockProblemWithADateFarOff_SendsAgain(string errorCode)
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15), errorCode);
        using var client = CreateClient(service.Handler, time);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((response.Item!["pk"].AsString(), service.Handler.Requests.Count)).IsEqualTo(("user-1", 2));
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrectionWithMaximumJitter_SendsAgainWithoutWaiting()
    {
        // Arrange (the clock of the test never moves, so a wait would leave the call pending)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time, random: FixedRandom.One);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(call.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrected_SignsLaterCallsWithTheCorrectedTime()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        time.Advance(TimeSpan.FromSeconds(10));
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(service.Handler.Requests[2].Headers["X-Amz-Date"]).IsEqualTo("20261031T125006Z");
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrected_SendsOnlyTheResendMoreThanASingleCall()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert (two requests for the first call, one for the second)
        await Assert.That(service.Handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ExecuteAsync_ClockFixedAfterACorrection_CorrectsBackAndSucceeds()
    {
        // Arrange (corrected by +15 minutes; then the local clock is set right, so the requests are 15 minutes ahead of the service)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time);
        await client.GetItemAsync(TestClient.GetItem());
        service.Offset = TimeSpan.Zero;

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert (the stale correction is replaced, not added to: the last request carries the local time again)
        await Assert.That(string.Join(',', service.Handler.Requests.Skip(2).Select(request => request.Headers["X-Amz-Date"]))).IsEqualTo("20261031T124956Z,20261031T123456Z");
    }

    [Test]
    public async Task ExecuteAsync_DateWithinFourMinutes_ThrowsTheErrorWithoutRetry()
    {
        // Arrange (the signature is wrong for another reason: the clocks agree closely)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(3));
        service.RejectEverything = true;
        using var client = CreateClient(service.Handler, time);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, service.Handler.Requests.Count)).IsEqualTo(("InvalidSignatureException", 1));
    }

    [Test]
    public async Task ExecuteAsync_ResponseWithoutDateHeader_ThrowsTheErrorWithoutRetry()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("InvalidSignatureException", "The request signature we calculated does not match.") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("InvalidSignatureException", 1));
    }

    [Test]
    public async Task ExecuteAsync_ResponseWithUnreadableDateHeader_ThrowsTheErrorWithoutRetry()
    {
        // Arrange
        var response = new StubResponse
        {
            StatusCode = HttpStatusCode.BadRequest,
            Body = StubResponse.DynamoDbError("InvalidSignatureException", "The request signature we calculated does not match.").Body,
            Headers = new Dictionary<string, string> { ["Date"] = "yesterday, around noon" },
        };
        using var handler = new StubHandler { DefaultResponse = response };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("InvalidSignatureException", 1));
    }

    [Test]
    public async Task ExecuteAsync_OtherErrorWithADateFarOff_ThrowsTheErrorAndKeepsSigningWithTheLocalClock()
    {
        // Arrange (a validation error says nothing about the clock, however far off the Date header is)
        var time = new ManualTimeProvider();
        var response = new StubResponse
        {
            StatusCode = HttpStatusCode.BadRequest,
            Body = StubResponse.DynamoDbError("ValidationException", "One or more parameter values were invalid.").Body,
            Headers = new Dictionary<string, string> { ["Date"] = time.GetUtcNow().AddHours(3).ToString("R", CultureInfo.InvariantCulture) },
        };
        using var handler = new StubHandler { DefaultResponse = response };
        using var client = CreateClient(handler, time);
        await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(string.Join(',', handler.Requests.Select(request => request.Headers["X-Amz-Date"]))).IsEqualTo("20261031T123456Z,20261031T123456Z");
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrectionWithRetriesSwitchedOff_ThrowsTheErrorButSignsLaterCallsRight()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time, maxAttempts: 1);
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert (one request failed, then one succeeded: no resend inside the first call)
        await Assert.That((response.Item!["pk"].AsString(), service.Handler.Requests.Count)).IsEqualTo(("user-1", 2));
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrectionCountsAsAnAttempt_LeavesNoAttemptForALaterFailure()
    {
        // Arrange (two attempts: the skewed one, then a 500)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        service.Override = request => request == 2 ? ServerError() : null;
        using var client = CreateClient(service.Handler, time, maxAttempts: 2);

        // Act
        var exception = await TestClient.CaptureAsync<InternalServerErrorException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, service.Handler.Requests.Count)).IsEqualTo(("InternalServerError", 2));
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrectionAfterARetry_KeepsTheQuotaOfThatRetryForTheRefund()
    {
        // Arrange (a throttled attempt takes 5 tokens; the skewed attempt that follows takes none; the success gives the 5 back)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        service.Override = request => request == 1 ? Throttled() : null;
        using var client = CreateClient(service.Handler, time, maxAttempts: int.MaxValue);
        await client.GetItemAsync(TestClient.GetItem());
        service.Override = static _ => Throttled();
        var before = service.Handler.Requests.Count;

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert (full quota again: 100 retries)
        await Assert.That(service.Handler.Requests.Count - before).IsEqualTo(101);
    }

    [Test]
    public async Task ExecuteAsync_ClockCorrection_UsesNoQuota()
    {
        // Arrange (the skew resend is free: afterwards the client still has all its tokens)
        var time = new ManualTimeProvider();
        using var service = Service(time, TimeSpan.FromMinutes(15));
        using var client = CreateClient(service.Handler, time, maxAttempts: int.MaxValue);
        await client.GetItemAsync(TestClient.GetItem());
        service.Override = static _ => Throttled();
        var before = service.Handler.Requests.Count;

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(service.Handler.Requests.Count - before).IsEqualTo(101);
    }

    [Test]
    public async Task ExecuteAsync_ServiceDateInTheLastSecondOfYear9999_StillSignsWhenTheClockMovesOn()
    {
        // Arrange (a nonsensical Date header must not make the client unable to sign)
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return SkewRejection(DateTimeOffset.MaxValue.AddSeconds(-1));
            }

            return Success();
        });
        using var client = CreateClient(handler, time);
        await client.GetItemAsync(TestClient.GetItem());
        time.Advance(TimeSpan.FromDays(1));

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    private static SkewedService Service(ManualTimeProvider time, TimeSpan offset, string errorCode = "InvalidSignatureException") => new(time, offset, errorCode);

    // A DynamoDB whose clock is the clock of the test plus an offset. It accepts a request signed within five minutes of that clock and
    // rejects any other with the Date header set, as the real service does.
    private sealed class SkewedService : IDisposable
    {
        private readonly ManualTimeProvider _time;
        private readonly string _errorCode;

        public SkewedService(ManualTimeProvider time, TimeSpan offset, string errorCode)
        {
            _time = time;
            _errorCode = errorCode;
            Offset = offset;
            Handler = new StubHandler(Respond);
        }

        public StubHandler Handler { get; }

        /// <summary>Gets or sets how far the clock of the service is ahead of the clock of the test.</summary>
        public TimeSpan Offset { get; set; }

        /// <summary>Gets or sets a value indicating whether every request is rejected as badly signed, whatever its time.</summary>
        public bool RejectEverything { get; set; }

        /// <summary>Gets or sets a function that returns the answer to the nth request (counting from 1), or <see langword="null"/> to check the time as usual.</summary>
        public Func<int, StubResponse?>? Override { get; set; }

        public void Dispose() => Handler.Dispose();

        private StubResponse Respond(RecordedRequest request)
        {
            // The handler records the request before it asks for the response.
            if (Override?.Invoke(Handler.Requests.Count) is { } scripted)
            {
                return scripted;
            }

            var serverNow = _time.GetUtcNow() + Offset;
            var signedAt = DateTimeOffset.ParseExact(
                request.Headers["X-Amz-Date"],
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return RejectEverything || (signedAt - serverNow).Duration() > TimeSpan.FromMinutes(5) ? SkewRejection(serverNow, _errorCode) : Success();
        }
    }
}
