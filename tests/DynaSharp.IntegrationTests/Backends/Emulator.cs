namespace DynaSharp.IntegrationTests.Backends;

/// <summary>A backend that is up, an emulator or real AWS: which one it is and how to reach it.</summary>
/// <param name="Backend">The backend.</param>
/// <param name="CreateOptions">Creates the options of a client of this backend. They do not hold a handler, so a caller can add one.</param>
/// <param name="Raw">A raw client for the table API, for setup and for reading unmapped responses.</param>
/// <param name="RawStreams">A raw client for the streams API.</param>
internal sealed record Emulator(Backend Backend, Func<DynamoDbClientOptions> CreateOptions, RawDynamoDb Raw, RawDynamoDb RawStreams);
