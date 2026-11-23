namespace DynaSharp.Testing;

/// <summary>A request as it came over the socket of a <see cref="LoopbackHttpServer"/>.</summary>
/// <param name="RequestLine">The first line, such as <c>POST / HTTP/1.1</c>.</param>
/// <param name="Headers">The lines of the headers, such as <c>Host: 127.0.0.1:50123</c>.</param>
/// <param name="Body">The body, decoded as UTF-8.</param>
internal sealed record ReceivedRequest(string RequestLine, IReadOnlyList<string> Headers, string Body);
