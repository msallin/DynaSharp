using System.Diagnostics;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// One DynamoDB operation as a command runs it. The body is the state's, so an operation only names itself and reads its response. When
/// adding one: a struct with these members, and a terminal in the command that calls <see cref="CommandState.SendAsync"/> with it.
/// </summary>
/// <typeparam name="TResponse">What the call returns.</typeparam>
internal interface ICommandOperation<TResponse>
{
    /// <summary>Gets the operation name as DynamoDB knows it.</summary>
    public static abstract string Name { get; }

    /// <summary>Reads the body of a successful response, and leaves the capacity in <see cref="CommandState.ResponseCapacity"/>.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="state">The state of the command, which is whole during the call.</param>
    /// <returns>The response.</returns>
    public static abstract TResponse ReadResponse(ref DynamoDbReader reader, CommandState state);

    /// <summary>Adds request parameters that are worth a tag on the span. The same tags as the operation of the request API; never a key or a value.</summary>
    /// <param name="activity">The span, with all data requested.</param>
    /// <param name="state">The state of the command.</param>
    public static virtual void SetActivityTags(Activity activity, CommandState state)
    {
    }

    /// <summary>Adds response values that are worth a tag on the span. Never a key or a value.</summary>
    /// <param name="activity">The span, with all data requested.</param>
    /// <param name="response">The response.</param>
    public static virtual void SetResponseTags(Activity activity, TResponse response)
    {
    }
}
