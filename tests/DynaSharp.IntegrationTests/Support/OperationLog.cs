using System.Collections.Concurrent;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>Tells which operations a client sent, for a test about what a command leaves out, which the state of a table cannot show.</summary>
internal sealed class OperationLog : DelegatingHandler
{
    private const string TargetHeader = "X-Amz-Target";

    private readonly ConcurrentQueue<string> _operations = new();

    /// <summary>Initializes a new instance of the <see cref="OperationLog"/> class.</summary>
    /// <param name="emulator">The backend, whose handler sends. This handler disposes it.</param>
    public OperationLog(Emulator emulator)
        : base(emulator.CreateHandler())
    {
    }

    /// <summary>Gets the operations so far, in the order they went out, separated by commas.</summary>
    public string Operations => string.Join(",", _operations);

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _operations.Enqueue(request.Headers.GetValues(TargetHeader).Single().Split('.')[^1]);
        return base.SendAsync(request, cancellationToken);
    }
}
