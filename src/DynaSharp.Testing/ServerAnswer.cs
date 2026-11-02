using System.Net;

namespace DynaSharp.Testing;

/// <summary>What a <see cref="LoopbackHttpServer"/> answers to a request.</summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Body">The body, which goes out as UTF-8 and <c>text/xml</c>.</param>
internal readonly record struct ServerAnswer(HttpStatusCode StatusCode, string Body);
