using System.Net;
using DynaSharp.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

// The credential providers must not resume on the context of their caller, see DynamoDbPipelineContextTests. A provider makes several
// requests one after the other, so each test makes one of them wait and lets the ones before it answer at once: the await after the
// waiting request is then the first that suspends while the caller's context is current.
public sealed class CredentialsContextTests
{
    private const string TokenPath = "/latest/api/token";
    private const string RolesPath = "/latest/meta-data/iam/security-credentials/";
    private const string RoleName = "my-role";
    private const string CredentialId = "12345678-90ab-cdef-1234-567890abcdef";

    private static readonly DateTimeOffset Expiration = new(2026, 10, 31, 18, 34, 56, TimeSpan.Zero);

    [Test]
    [Arguments(TokenPath)]
    [Arguments(RolesPath)]
    [Arguments(RolesPath + RoleName)]
    public async Task InstanceMetadataCredentialsProvider_RequestAnswersLater_DoesNotResumeOnTheContextOfTheCaller(string waitingPath)
    {
        // Arrange
        var answerReady = new TaskCompletionSource();
        using var handler = new DelegateHandler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == waitingPath)
            {
                await answerReady.Task.ConfigureAwait(false);
            }

            return path switch
            {
                TokenPath => Respond("token-v2"),
                RolesPath => Respond(RoleName + "\n"),
                _ => Respond(InstanceMetadataJson(Expiration)),
            };
        });
        var provider = new InstanceMetadataCredentialsProvider(Env(), handler, new ManualTimeProvider(), NullLogger.Instance, new Random(1));
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => provider.GetCredentialsAsync(CancellationToken.None).AsTask());

        // Act
        answerReady.SetResult();
        var credentials = await call;

        // Assert
        await Assert.That((context.Posts, credentials.AccessKeyId)).IsEqualTo((0, AccessKeyId));
    }

    [Test]
    public async Task ContainerCredentialsProvider_WaitBeforeTheSecondAttemptEnds_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the first attempt fails at once, so the wait before the second is the first await that suspends)
        var time = new ManualTimeProvider();
        var attempts = 0;
        using var handler = new DelegateHandler((_, _) => Task.FromResult(++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Respond(ContainerJson(Expiration))));
        var provider = new ContainerCredentialsProvider(
            Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/" + CredentialId)), NoFiles, handler, time, NullLogger.Instance);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => provider.GetCredentialsAsync(CancellationToken.None).AsTask());

        // Act
        time.Advance(TimeSpan.FromSeconds(1));
        var credentials = await call;

        // Assert
        await Assert.That((context.Posts, credentials.AccessKeyId)).IsEqualTo((0, AccessKeyId));
    }

    [Test]
    public async Task DefaultCredentialsProvider_SourceAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the sources before the container endpoint have nothing and answer at once)
        var answerReady = new TaskCompletionSource();
        using var handler = new DelegateHandler(async (_, _) =>
        {
            await answerReady.Task.ConfigureAwait(false);
            return Respond(ContainerJson(Expiration));
        });
        var environment = Env(("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/" + CredentialId));
        var provider = new DefaultCredentialsProvider(environment, NoFiles, handler, new ManualTimeProvider(), NullLoggerFactory.Instance, null);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => provider.GetCredentialsAsync(CancellationToken.None).AsTask());

        // Act
        answerReady.SetResult();
        var credentials = await call;

        // Assert
        await Assert.That((context.Posts, credentials.AccessKeyId)).IsEqualTo((0, AccessKeyId));
    }

    private static HttpResponseMessage Respond(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
}
