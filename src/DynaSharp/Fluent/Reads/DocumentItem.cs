using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>An item read as a whole, which is what every read does but <c>Select</c>.</summary>
/// <typeparam name="T">How the item is read.</typeparam>
internal readonly struct DocumentItem<T> : IItemReader<T>
    where T : IDynamoDbDocument<T>
{
    public static T Read(ref DynamoDbReader reader, CommandState state) => T.ReadAttributes(ref reader);
}
