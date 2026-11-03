using DynaSharp.Credentials;
using DynaSharp.Testing;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// The in-memory <see cref="FakeDynamoDb"/> of <c>DynaSharp.Testing</c> as a backend: one fake for the whole run, as one container serves the emulators, and the tests keep apart by table name.
/// There is no container and no network: a client sends over a handler of the fake, and the raw clients call <see cref="FakeDynamoDb.Answer(string, string)"/>.
/// </summary>
internal static class FakeBackend
{
    // Never contacted: the handler answers every request. It only gives the client an endpoint that is not AWS.
    private static readonly Uri Endpoint = new("http://fake.localhost");

    /// <summary>Creates the fake of this run.</summary>
    /// <returns>The backend.</returns>
    public static Emulator Create()
    {
        var fake = new FakeDynamoDb();
        return new Emulator(
            Backend.Fake,
            CreateOptions,
            new RawDynamoDb((operation, json, _) => Task.FromResult(Send(fake, RawDynamoDb.TableTargetPrefix + operation, json))),
            new RawDynamoDb((operation, json, _) => Task.FromResult(Send(fake, RawDynamoDb.StreamsTargetPrefix + operation, json))),
            fake.CreateHandler);
    }

    // As for the emulators: one attempt, so a failure shows up at once.
    private static DynamoDbClientOptions CreateOptions() => new()
    {
        Region = AwsRegion.FromName("us-east-1"),
        ServiceUrl = Endpoint,
        Credentials = new StaticCredentialsProvider(new AwsCredentials("test", "test")),
        MaxAttempts = 1,
        AttemptTimeout = TimeSpan.FromMinutes(2),
    };

    private static RawResponse Send(FakeDynamoDb fake, string target, string json)
    {
        var answer = fake.Answer(target, json);
        return new RawResponse(answer.StatusCode, answer.Body, answer.Headers?.GetValueOrDefault("x-amzn-RequestId"));
    }
}
