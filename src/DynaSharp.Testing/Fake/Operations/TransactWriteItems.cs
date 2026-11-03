using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>TransactWriteItems</c>: up to 100 actions, all or none, idempotent by client request token.</summary>
internal static class TransactWriteItems
{
    /// <summary>The operation.</summary>
    public const string Name = "TransactWriteItems";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response) => throw FakeError.UnknownOperation(Name);
}
