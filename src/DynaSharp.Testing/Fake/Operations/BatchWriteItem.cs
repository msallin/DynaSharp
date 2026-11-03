using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>A <c>BatchWriteItem</c>: up to 25 puts and deletes over several tables, atomic here.</summary>
internal static class BatchWriteItem
{
    /// <summary>The operation.</summary>
    public const string Name = "BatchWriteItem";

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response) => throw FakeError.UnknownOperation(Name);
}
