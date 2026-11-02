using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>DeleteItem</c> as a command runs it.</summary>
/// <typeparam name="T">How the returned attributes are read.</typeparam>
internal readonly struct DeleteOperation<T> : ICommandOperation<WriteResult<T>>
    where T : IDynamoDbDocument<T>
{
    public static string Name => DeleteItemOperation.Name;

    public static WriteResult<T> ReadResponse(ref DynamoDbReader reader, CommandState state) => WriteResponse.Read<T>(ref reader, state);
}
