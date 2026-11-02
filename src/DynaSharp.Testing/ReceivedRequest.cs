namespace DynaSharp.Testing;

/// <summary>A request as it came over the socket of a <see cref="LoopbackHttpServer"/>.</summary>
/// <param name="Headers">The lines of the headers, such as <c>Host: 127.0.0.1:50123</c>.</param>
/// <param name="Body">The body, decoded as UTF-8.</param>
internal sealed record ReceivedRequest(IReadOnlyList<string> Headers, string Body);
