using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>TransactGetItems</c>: up to 100 gets, read together.</summary>
internal static class TransactGetItems
{
    /// <summary>The operation.</summary>
    public const string Name = "TransactGetItems";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response) => throw FakeError.UnknownOperation(Name);
}
