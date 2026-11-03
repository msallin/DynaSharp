using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>Scan</c>: every partition of the table or an index, or those of one segment, cut into pages.</summary>
internal static class Scan
{
    /// <summary>The operation.</summary>
    public const string Name = "Scan";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response) => throw FakeError.UnknownOperation(Name);
}
