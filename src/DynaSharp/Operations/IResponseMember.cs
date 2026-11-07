using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// A member of a response that the fluent API and the request API read in forms of their own: the items of a page into an entity or into dictionaries, a key located in the body or parsed. An
/// envelope reads the members both read alike and hands such a member to the reader it is given, so a member that both read alike is added in one place.
/// </summary>
/// <remarks>
/// A reader is a struct that keeps what it read, and an envelope takes it by reference and as a type argument, so each kind of read gets code of its own and calls no delegate.
/// </remarks>
internal interface IResponseMember
{
    /// <summary>Reads the value of the member.</summary>
    /// <param name="reader">The reader, on the name of the member. It ends after the value.</param>
    public void Read(ref DynamoDbReader reader);
}
