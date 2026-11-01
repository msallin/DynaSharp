using System.Reflection;

namespace DynaSharp.UnitTests;

/// <summary>
/// Looks at how an <see cref="HttpClient"/> was set up. What the library sets (the handler and its options, the timeout) is a
/// contract that no request shows without a server to talk to, so a test reads it from the client.
/// </summary>
internal static class HttpClientInspector
{
    /// <summary>Gets the handler that <paramref name="client"/> sends through.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The handler.</returns>
    public static HttpMessageHandler HandlerOf(HttpClient client) =>
        (HttpMessageHandler)typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
}
