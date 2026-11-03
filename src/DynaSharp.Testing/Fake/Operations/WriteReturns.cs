using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>What a write returns: <c>ReturnValues</c> and <c>ReturnItemCollectionMetrics</c>. <see cref="Check"/> runs before the write changes anything, <see cref="Write"/> after it.</summary>
internal static class WriteReturns
{
    /// <summary>Checks what a write asks to return against what its operation allows.</summary>
    /// <param name="operation">The operation: <c>PutItem</c> and <c>DeleteItem</c> take <c>NONE</c> and <c>ALL_OLD</c>, <c>UpdateItem</c> all five.</param>
    /// <param name="returnValues">The <c>ReturnValues</c>.</param>
    /// <param name="metrics">The <c>ReturnItemCollectionMetrics</c>.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a value the operation does not take.</exception>
    public static void Check(string operation, ReturnValue returnValues, ReturnItemCollectionMetrics metrics) => throw FakeError.Unsupported("ReturnValues and ReturnItemCollectionMetrics", operation);

    /// <summary>Writes <c>Attributes</c> and <c>ItemCollectionMetrics</c> into the response of a write that went through.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="table">The table.</param>
    /// <param name="returnValues">The <c>ReturnValues</c>.</param>
    /// <param name="metrics">The <c>ReturnItemCollectionMetrics</c>.</param>
    /// <param name="oldImage">The item before, or <see langword="null"/>.</param>
    /// <param name="newImage">The item after, or <see langword="null"/> for a delete.</param>
    public static void Write(Utf8JsonWriter response, FakeTable table, ReturnValue returnValues, ReturnItemCollectionMetrics metrics, Item? oldImage, Item? newImage) =>
        throw FakeError.Unsupported("ReturnValues and ReturnItemCollectionMetrics");
}
