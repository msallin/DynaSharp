using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// <c>UpdateItem</c> with <c>UPDATED_NEW</c>, read for one attribute: <c>{"Attributes":{"count":{"N":"7"}}}</c>, and for a nested path
/// <c>{"Attributes":{"stats":{"M":{"count":{"N":"7"}}}}}</c>. A missing attribute throws rather than reads as <see langword="default"/>, because
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
                found = TryReadPath(ref reader, path, path, out value);
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

    // Walks the attributes of a map whose brace the reader has consumed, up to and including the closing one.
    private static bool TryReadPath(ref DynamoDbReader reader, ReadOnlySpan<char> rest, string path, out TValue? value)
    {
        var dot = rest.IndexOf('.');
        var name = dot < 0 ? rest : rest[..dot];
        value = default;
        var found = false;
        while (reader.TryReadAttributeName())
        {
            if (found || !reader.Json.ValueTextEquals(name))
            {
                reader.SkipAttributeValue();
                continue;
            }

            var type = reader.ReadStartAttribute();
            if (dot < 0)
            {
                value = Convert(ref reader, type, path);
                found = true;
            }
            else if (type == AttributeType.Map)
            {
                found = TryReadPath(ref reader, rest[(dot + 1)..], path, out value);
            }
            else
            {
                reader.Json.Skip();
            }

            reader.ReadEndAttribute();
        }

        return found;
    }

    // The write has happened by now, so this is not a bad response but a value the caller's type cannot hold.
    private static TValue Convert(ref DynamoDbReader reader, AttributeType type, string path)
    {
        try
        {
            return TConverter.Read(ref reader, type);
        }
        catch (Exception exception) when (DynamoDbMappingException.IsConversionFailure(exception))
        {
            throw new DynamoDbMappingException(typeof(TValue).ToString(), path, exception);
        }
    }
}
