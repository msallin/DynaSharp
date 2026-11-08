namespace DynaSharp.Testing.Fake;

/// <summary>
/// Where a map of attribute values stands in a request, which decides how DynamoDB words what it rejects in it: a number out of range, a set with a member twice, a value nested too deep.
/// </summary>
internal enum ItemLayer
{
    /// <summary>A request of its own that reads, such as <c>GetItem</c> and <c>Query</c>. The message comes bare.</summary>
    Bare,

    /// <summary><c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c>. The message comes inside the validation envelope, <c>1 validation error detected: ...</c>, wherever the value sits.</summary>
    Enveloped,

    /// <summary>An element of a batch or of a transaction. The message comes bare, and a value nested too deep is worded in the plainest form.</summary>
    Element,
}
