using System.Text;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The size of an item as DynamoDB counts it, the one measure behind the limit of 400 KB, the page of 1 MB, the capacity units and <c>TableSizeBytes</c>.
/// </summary>
/// <remarks>
/// The rules of the Developer Guide, "Item sizes and formats": an attribute is the UTF-8 bytes of its name plus its value. A string is its UTF-8 bytes, a binary its bytes, a number
/// 1 byte per two significant digits, rounded up, plus 1, a boolean or a null 1 byte. A list or a map is 3 bytes plus 1 byte per element plus the elements, with the name of each
/// entry of a map. A set is the sum of its elements. <c>{"name":{"S":"Alice"}}</c> is 4 + 5 = 9 bytes, <c>{"n":{"N":"123"}}</c> 1 + 2 + 1 = 4.
/// </remarks>
internal static class ItemSize
{
    /// <summary>The largest item DynamoDB stores.</summary>
    public const int MaxItemBytes = 400 * 1024;

    /// <summary>Measures an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The size in bytes.</returns>
    public static long Of(IReadOnlyDictionary<string, AttributeValue> item)
    {
        long size = 0;
        foreach (var (name, value) in item)
        {
            size += Encoding.UTF8.GetByteCount(name) + Of(value);
        }

        return size;
    }

    /// <summary>Measures a value without a name, such as the value of a key.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The size in bytes.</returns>
    public static long Of(AttributeValue value) => value.Type switch
    {
        AttributeType.String => Encoding.UTF8.GetByteCount(value.AsString()),
        AttributeType.Number => OfNumber(value.AsNumber()),
        AttributeType.Binary => value.AsBinary().Length,
        AttributeType.List => 3 + value.AsList().Sum(static element => 1 + Of(element)),
        AttributeType.Map => 3 + value.AsMap().Sum(static entry => 1 + Encoding.UTF8.GetByteCount(entry.Key) + Of(entry.Value)),
        AttributeType.StringSet => value.AsStringSet().Sum(static element => (long)Encoding.UTF8.GetByteCount(element)),
        AttributeType.NumberSet => value.AsNumberSet().Sum(OfNumber),
        AttributeType.BinarySet => value.AsBinarySet().Sum(static element => (long)element.Length),
        _ => 1,
    };

    private static long OfNumber(string text) => ((DecimalNumber.Parse(text).SignificantDigits + 1) / 2) + 1;
}
