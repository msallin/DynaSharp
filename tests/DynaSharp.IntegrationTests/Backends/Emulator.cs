namespace DynaSharp.IntegrationTests.Backends;

/// <summary>A running emulator: which backend it is and where it listens.</summary>
/// <param name="Backend">The backend.</param>
/// <param name="Endpoint">The base address of its DynamoDB API.</param>
/// <param name="Raw">A raw client for the table API, for setup and for reading unmapped responses.</param>
/// <param name="RawStreams">A raw client for the streams API.</param>
internal sealed record Emulator(Backend Backend, Uri Endpoint, RawDynamoDb Raw, RawDynamoDb RawStreams);
