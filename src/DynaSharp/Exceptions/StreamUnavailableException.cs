using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// The stream of a table cannot be read as asked: it is off, or its view lacks the images a typed reading needs. <see cref="ViewType"/> and <see cref="RequiredViewType"/> tell the two apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it comes from.</b> The reading of a table, <c>ReadStreamAsync</c> and <c>ReadChangesAsync</c>, when the <c>DescribeTable</c> it starts with shows no stream it can use, before any record is read.
/// Nothing was read, so a reader that waits for an operator to turn the stream on catches this type alone, waits and starts the reading again.
/// </para>
/// <para>
/// <b>Not a DynamoDB error.</b> DynamoDB answered; the table is not set up for the reading. It is an <see cref="InvalidOperationException"/>, as the plain type was before, so a
/// <c>catch (InvalidOperationException)</c> still catches it. A stream that is switched on again is a new stream, which the next reading finds.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     await foreach (var change in orders.ReadChangesAsync(streams, ShardIteratorType.Latest, cancellationToken: ct))
///     {
///         Console.WriteLine(change.Kind);
///     }
/// }
/// catch (StreamUnavailableException exception) when (exception.ViewType is null)
/// {
///     Console.WriteLine($"The stream of {exception.TableName} is off: retrying later.");
/// }
/// </code>
/// </example>
public sealed class StreamUnavailableException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="StreamUnavailableException"/> class.</summary>
    /// <param name="message">The message, which names the table and says what to change.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="viewType">The view of the stream of the table, or <see langword="null"/> if it has no enabled stream.</param>
    /// <param name="requiredViewType">The view the reading needs, or <see langword="null"/> if it takes any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    public StreamUnavailableException(string message, string tableName, StreamViewType? viewType, StreamViewType? requiredViewType)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        TableName = tableName;
        ViewType = viewType;
        RequiredViewType = requiredViewType;
    }

    /// <summary>Gets the name of the table, or its ARN, as the table was opened.</summary>
    public string TableName { get; }

    /// <summary>Gets the view of the stream the table has, or <see langword="null"/> if its stream is off, or it never had one.</summary>
    public StreamViewType? ViewType { get; }

    /// <summary>
    /// Gets the view the reading needs, <see cref="StreamViewType.NewAndOldImages"/> for a typed reading, or <see langword="null"/> for a reading of records, which takes any view and fails only on a stream that is off.
    /// </summary>
    public StreamViewType? RequiredViewType { get; }
}
