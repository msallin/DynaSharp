using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// The warning of a provider that its endpoint is reached over plain http on another host, which it logs once in its life: the endpoint is
/// allowed, and a warning at every fetch would drown the log.
/// </summary>
/// <param name="logger">The logger of the provider.</param>
/// <param name="log">The log message, which names the service, with the host.</param>
internal sealed class UnencryptedEndpointWarning(ILogger logger, Action<ILogger, string> log)
{
    private int _warned;

    /// <summary>Logs the warning if the endpoint leaves the machine unencrypted and this has not warned before.</summary>
    /// <param name="endpoint">The endpoint of a fetch.</param>
    public void Check(Uri endpoint)
    {
        if (ClientConfiguration.IsUnencryptedRemote(endpoint) && Interlocked.Exchange(ref _warned, 1) == 0)
        {
            log(logger, endpoint.Host);
        }
    }
}
