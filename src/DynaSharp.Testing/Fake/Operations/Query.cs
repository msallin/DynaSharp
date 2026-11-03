using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>Query</c>: one partition of the table or an index, in sort key order, cut into pages.</summary>
internal static class Query
{
    /// <summary>The operation.</summary>
    public const string Name = "Query";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response) => throw FakeError.UnknownOperation(Name);
}
