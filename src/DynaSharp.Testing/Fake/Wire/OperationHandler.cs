using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>Answers one request of an operation: reads <paramref name="request"/>, changes the state, and writes the members of the response into an object the wire opened.</summary>
/// <param name="db">The state.</param>
/// <param name="request">The body of the request, a JSON object.</param>
/// <param name="response">The writer, inside the object of the response.</param>
internal delegate void OperationHandler(FakeDatabase db, JsonElement request, Utf8JsonWriter response);
