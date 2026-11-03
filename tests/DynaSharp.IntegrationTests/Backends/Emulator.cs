namespace DynaSharp.IntegrationTests.Backends;

/// <summary>A backend that is up, an emulator, the fake or real AWS: which one it is and how to reach it.</summary>
/// <param name="Backend">The backend.</param>
/// <param name="CreateOptions">Creates the options of a client of this backend. They do not hold a handler, so a caller can add one.</param>
/// <param name="Raw">A raw client for the table API, for setup and for reading unmapped responses.</param>
/// <param name="RawStreams">A raw client for the streams API.</param>
/// <param name="CreateHandler">
/// Creates the handler a client of this backend sends over, which a test that watches the wire wraps in a <see cref="DelegatingHandler"/>: a new <see cref="SocketsHttpHandler"/> for a
/// backend on the network, a handler of the fake for <see cref="Backend.Fake"/>, which has no network.
/// </param>
internal sealed record Emulator(Backend Backend, Func<DynamoDbClientOptions> CreateOptions, RawDynamoDb Raw, RawDynamoDb RawStreams, Func<HttpMessageHandler> CreateHandler);
