using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// <c>UpdateItem</c> with <c>UPDATED_NEW</c>, read for one attribute: <c>{"Attributes":{"count":{"N":"7"}}}</c>, and for a nested path
/// <c>{"Attributes":{"stats":{"M":{"count":{"N":"7"}}}}}</c>. A missing attribute, or one the converter counts as missing, throws rather than reads as <see langword="default"/>, because
/// a counter that silently reads 0 after a <c>REMOVE</c> is the bug this exists to prevent.
/// </summary>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal readonly struct UpdateValueOperation<TValue, TConverter> : ICommandOperation<TValue>
    where TConverter : IDynamoDbConverter<TValue>
{
    public static string Name => UpdateItemOperation.Name;

    public static TValue ReadResponse(ref DynamoDbReader reader, CommandState state)
    {
        var path = state.ReturnPath!;
        var value = default(TValue);
        var found = false;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Attributes"u8))
            {
                reader.ReadStartItem();
                found = AttributePath<TValue, TConverter>.TryRead(ref reader, path, path, null, out value);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                state.ResponseCapacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return found ? value! : throw DynamoDbMappingException.Missing(typeof(TValue).ToString(), path);
    }
}
