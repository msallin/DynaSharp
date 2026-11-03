using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// A client that retries, on a connection that drops the first response: the request reaches the service and takes effect, and the client sees a network failure. That is
/// what makes a retry dangerous, and only a test of the wire can show it.
/// </summary>
internal sealed class LostResponseClient : IDisposable
{
    private readonly LosingHandler _handler;

    /// <summary>Initializes a new instance of the <see cref="LostResponseClient"/> class.</summary>
    /// <param name="emulator">The backend.</param>
    public LostResponseClient(Emulator emulator)
    {
        _handler = new LosingHandler(emulator.CreateHandler());
        Client = new DynamoDbClient(emulator.CreateOptions() with { MaxAttempts = 2, HttpHandler = _handler });
    }

    /// <summary>Gets the client.</summary>
    public DynamoDbClient Client { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        Client.Dispose();
        _handler.Dispose();
    }

    private sealed class LosingHandler : DelegatingHandler
    {
        private int _lost;

        public LosingHandler(HttpMessageHandler inner)
            : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (Interlocked.Exchange(ref _lost, 1) == 0)
            {
                response.Dispose();
                throw new HttpRequestException("The connection dropped after the request went out.");
            }

            return response;
        }
    }
}
