using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>The <c>Item</c> of a <c>GetItem</c> response as a command reads it: a whole item or some attributes, by <typeparamref name="TReader"/>.</summary>
/// <typeparam name="TValue">What the item gives.</typeparam>
/// <typeparam name="TReader">What reads the item.</typeparam>
/// <param name="state">The state of the command.</param>
internal struct GetItemMember<TValue, TReader>(CommandState state) : IResponseMember
    where TReader : IItemReader<TValue>
{
    /// <summary>Gets the item, or the default if the response has none.</summary>
    public TValue? Value { get; private set; }

    /// <inheritdoc/>
    public void Read(ref DynamoDbReader reader)
    {
        reader.ReadStartItem();
        try
        {
            Value = TReader.Read(ref reader, state);
        }
        catch (DynamoDbMappingException failure)
        {
            // The key of the item is the one of the request, which stays until the call ends.
            failure.Key = state.ReadKey();
            throw;
        }
    }
}
