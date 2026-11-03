using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>An <c>UpdateItem</c>: the update expression on the item, or on its key for a new one, under its condition.</summary>
internal static class UpdateItem
{
    /// <summary>The operation.</summary>
    public const string Name = "UpdateItem";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response) => throw FakeError.UnknownOperation(Name);
}
