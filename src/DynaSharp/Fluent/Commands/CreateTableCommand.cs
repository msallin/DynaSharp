using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// Creates a table from what its entities declare: <c>await orders.CreateTable().Including&lt;Customer&gt;().Including&lt;Product&gt;().SendAsync(ct);</c>. <see cref="Table{T}.CreateTable"/> starts one.
/// </summary>
/// <remarks>
/// <para>
/// Key names, index names, key schemas and attribute types come from the metadata of the entities. Billing is on demand unless <see cref="Provisioned"/> says otherwise, and every index projects all attributes.
/// Local and global indexes are both created. A local index exists only if the table is created with it, so an entity left out here means an index that cannot be added later; a global one can, through
/// <c>UpdateTable</c> in <c>DynaSharp.Requests</c>.
/// </para>
/// <para>
/// One entity does not describe a single table, whose indexes are the union over its entities, so <see cref="Including{TOther}"/> names the others. A table without an entity starts from the key names it was
/// opened with, as strings, unless an included entity stores them otherwise.
/// </para>
/// <para>
/// A key template is a string, and a plain key is what its converter writes: a number, a binary value, or a string for anything else. A custom converter says what it writes only by implementing
/// <see cref="Serialization.IAddableConverter"/>, which makes the key a number; any other counts as a string, so a custom converter that writes binary needs <see cref="ToRequest"/>. <see cref="Stream"/> and
/// <see cref="TimeToLive(string)"/> cover those two. For tags, a storage class or a projection of fewer attributes, take <see cref="ToRequest"/> and send it through <c>DynaSharp.Requests</c>.
/// </para>
/// <para>
/// This is administration and not a hot path, so unlike the commands of items it is an ordinary class: a clause changes the command and returns it, it can be kept and sent again, and it is not thread-safe. A
/// retry repeats the request: when a response was lost, the retry of a table this very call created fails with <see cref="ResourceInUseException"/>, which <see cref="IfNotExists"/> turns into success.
/// </para>
/// </remarks>
public sealed class CreateTableCommand
{
    private readonly TableModel _table;

    private readonly TableSchema _schema;

    private ProvisionedThroughput? _capacity;

    private bool _ifNotExists;

    private TimeSpan? _activeTimeout;

    private StreamViewType? _stream;

    private string? _timeToLive;

    /// <summary>Initializes a new instance of the <see cref="CreateTableCommand"/> class.</summary>
    /// <param name="table">The table.</param>
    /// <param name="entity">The entity of the table, as a message names it. A table without an entity has its name in messages instead.</param>
    internal CreateTableCommand(TableModel table, string entity)
    {
        _table = table;
        _schema = new TableSchema(table.Entity is null ? $"The table {table.Name}" : entity, table.PartitionKeyName, table.SortKeyName);
        if (table.Entity is { } info)
        {
            _schema.Add(entity, info);
        }
    }

    /// <summary>
    /// Adds what another entity on this table declares, so that its indexes exist too. The result is the union: an index two entities declare exists once, and several templates over the same attributes, an
    /// overloaded index, are one index. Including an entity twice changes nothing.
    /// </summary>
    /// <typeparam name="TOther">Any entity on this table.</typeparam>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentException">
    /// The entity disagrees with the table or with an entity before it, which the message names both of: the key of the table has other names, it stores an attribute as another type, or it declares an index of
    /// that name over other attributes, or as local where the other is global.
    /// </exception>
    public CreateTableCommand Including<TOther>()
        where TOther : IDynamoDbEntity<TOther>
    {
        _schema.Add(typeof(TOther).ToString(), TOther.Info);
        return this;
    }

    /// <summary>Gives the table provisioned capacity instead of on-demand billing. DynamoDB needs capacity for each global index as well, and each gets these numbers; a local index shares that of the table. For other numbers per index, take <see cref="ToRequest"/>.</summary>
    /// <param name="read">The read capacity units per second, at least 1.</param>
    /// <param name="write">The write capacity units per second, at least 1.</param>
    /// <returns>This command.</returns>
    public CreateTableCommand Provisioned(long read, long write)
    {
        _capacity = new ProvisionedThroughput { ReadCapacityUnits = read, WriteCapacityUnits = write };
        return this;
    }

    /// <summary>Makes <see cref="SendAsync"/> wait until the table and its global secondary indexes, those the entities of this command declare among them, are <c>ACTIVE</c>, for at most five minutes. See <see cref="WaitUntilActive(TimeSpan)"/>.</summary>
    /// <returns>This command.</returns>
    public CreateTableCommand WaitUntilActive()
    {
        _activeTimeout = TableAdministration.DefaultActiveTimeout;
        return this;
    }

    /// <summary>
    /// Makes <see cref="SendAsync"/> wait until the table and its global secondary indexes are <c>ACTIVE</c>, as <see cref="Table{T}.WaitUntilActiveAsync(TimeSpan, CancellationToken)"/> does, with its rules on the timeout.
    /// Without this, the table is <c>CREATING</c> when the call returns, and a read or write of it fails until it is active. With <see cref="IfNotExists"/> over a table that exists, an index that <c>UpdateTable</c> added can
    /// still be <c>CREATING</c>, which takes minutes even on an empty table and often longer than the default of five, so give that wait half an hour or more.
    /// </summary>
    /// <remarks>
    /// Every global index that this command declares, by its entity and by <see cref="Including{TOther}"/>, has to be listed and active, and a local one needs no wait. With <see cref="IfNotExists"/> over an older table that lacks
    /// a declared index, nothing adds the index, so this <b>fails instead of returning</b>, with a <see cref="TableNotActiveException"/> that names it. It does not wait out the timeout for that: an index still not listed at
    /// the fifth look in a row, 1.5 seconds in, ends the wait, because a create lists all its indexes and only an <c>UpdateTable</c> sent before this command adds one. Add the index with <c>UpdateTable</c> in <c>DynaSharp.Requests</c> first.
    /// That needs an active table, which a table opened without an entity waits for, because it declares nothing: <c>client.Table(name, "pk", "sk").WaitUntilActiveAsync()</c>. Then this wait follows the backfill.
    /// <para>
    /// The timeout bounds the wait and nothing else, so the calls before it and time to live after it take their own time. A <see cref="CancellationToken"/> on the same timeout races it, and when the token wins the caller gets a
    /// bare <see cref="TaskCanceledException"/> or <see cref="OperationCanceledException"/>, with no table and no index named. Give the wait less time than the token.
    /// </para>
    /// </remarks>
    /// <param name="timeout">How long to wait after the table was created. <see cref="Timeout.InfiniteTimeSpan"/> is not a bound and is rejected.</param>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, which this call finds out, so that no table is created for it.</exception>
    public CreateTableCommand WaitUntilActive(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        _activeTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Gives the table a stream, which <see cref="ToRequest"/> shows. It applies to a table this command creates: with <see cref="IfNotExists"/>, the stream of a table that exists stays untouched, also when it has none or
    /// another view, and nothing says so. Change that with <c>UpdateTable</c> in <c>DynaSharp.Requests</c>.
    /// </summary>
    /// <param name="view">What a record of the stream holds.</param>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="view"/> is no view, which DynamoDB would refuse, and this call finds out before a table is created.</exception>
    public CreateTableCommand Stream(StreamViewType view)
    {
        if (view == StreamViewType.Unspecified || !Enum.IsDefined(view))
        {
            throw new ArgumentOutOfRangeException(nameof(view), view, "A stream needs a view: NewImage, OldImage, NewAndOldImages or KeysOnly.");
        }

        _stream = view;
        return this;
    }

    /// <summary>
    /// Makes sure that the table has time to live on this attribute, on a table this command creates and on one <see cref="IfNotExists"/> finds. <c>CreateTable</c> does not take it, so it is no part of
    /// <see cref="ToRequest"/>: it happens after the create, or after finding the table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It needs an active table, so it implies <see cref="WaitUntilActive()"/>, with the five minutes of that method unless <see cref="WaitUntilActive(TimeSpan)"/> gave another timeout, and with all its rules: a wait that runs out
    /// throws <see cref="TableNotActiveException"/> and leaves time to live as it was, also when a table <see cref="IfNotExists"/> found lacks a global index this command declares. So a program that adds an index to a table
    /// that exists calls this after it added the index.
    /// </para>
    /// <para>
    /// Then <c>DescribeTimeToLive</c> looks, and <c>ENABLED</c> or <c>ENABLING</c> on this attribute ends it. Anything else sends <c>UpdateTimeToLive</c>, which DynamoDB answers with a
    /// <see cref="ValidationException"/> for time to live that is on already, and for a second change within the hour that a change takes. Instances of a program start together, so that can mean that another one got
    /// there first: the command looks again and takes it for success if time to live is now on for this attribute. Otherwise that exception surfaces, and that is what time to live on another attribute comes to:
    /// DynamoDB says what is wrong, and turning it off and on again with the new name is a decision this command does not take. <see cref="CreateTableResult.TimeToLiveEnabled"/> is <see langword="true"/> only
    /// after an update DynamoDB accepted.
    /// </para>
    /// <para>
    /// <b>Permissions.</b> It takes <c>dynamodb:DescribeTimeToLive</c> on every call, <c>dynamodb:UpdateTimeToLive</c> when time to live is not on yet, and <c>dynamodb:DescribeTable</c> for the wait.
    /// </para>
    /// <para>
    /// The attribute of an item must be a number that holds a Unix time in seconds, which <c>[DynamoDbConverter(typeof(EpochSecondsConverter))]</c> on a <see cref="DateTimeOffset"/> writes. See <see cref="UpdateTimeToLiveRequest"/> for when DynamoDB deletes.
    /// </para>
    /// </remarks>
    /// <param name="attributeName">The name of the attribute that holds the expiry time.</param>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentException"><paramref name="attributeName"/> is <see langword="null"/> or empty.</exception>
    public CreateTableCommand TimeToLive(string attributeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(attributeName);
        _timeToLive = attributeName;
        return this;
    }

    /// <summary>Makes sure that the table has time to live on the attribute of a handle: <c>.TimeToLive(Order.Attrs.ExpiresAt)</c>. See <see cref="TimeToLive(string)"/>.</summary>
    /// <param name="attribute">A handle, or an <see cref="Attr"/> of a name.</param>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentException"><paramref name="attribute"/> has no path, or a nested one, because time to live reads an attribute at the top of an item.</exception>
    public CreateTableCommand TimeToLive(Attr attribute)
    {
        var path = attribute.Path;
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(attribute));
        if (path.AsSpan().IndexOfAny('.', '[') >= 0)
        {
            throw new ArgumentException($"Time to live reads an attribute at the top of an item, and {path} is a path into one.", nameof(attribute));
        }

        return TimeToLive(path);
    }

    /// <summary>
    /// Treats a table that exists as success: <c>DescribeTable</c> goes first, and a table it finds ends the create there, with its description and without a <c>CreateTable</c>, which is what makes a create at the start
    /// of a program safe to repeat. <see cref="TimeToLive(string)"/> on the chain still goes on, and can change the table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It <b>does not compare schemas</b>: a table of this name with other keys or indexes is success too, and a table that is still being created is success with that status. Reads and writes then fail, or find the
    /// indexes missing, and a <see cref="WaitUntilActive()"/> fails over a table that lacks a global index this command declares. <see cref="CreateTableResult.Created"/> says whether the table was found. The indexes and the <see cref="Stream"/> of this command reach only a table it creates.
    /// </para>
    /// <para>
    /// <b>Permissions.</b> A table that exists takes <c>dynamodb:DescribeTable</c> alone, unless <see cref="TimeToLive(string)"/> adds its own, so a role that may not create tables gets through, and no failed call shows in CloudTrail or in error metrics. Creating a table takes
    /// <c>dynamodb:CreateTable</c> as well, and <c>dynamodb:DescribeTable</c> is needed for that too: the look comes first, and any error of it other than a missing table surfaces, so a role without it gets
    /// <see cref="AccessDeniedException"/> and creates nothing. With <see cref="WaitUntilActive()"/>, a first look that already shows the table and its indexes active returns without another call.
    /// </para>
    /// <para>
    /// A table that is going away is no success, with or without a wait, because no table is left afterwards: the command goes on to <c>CreateTable</c>, and when <c>DescribeTable</c> says <c>DELETING</c> again, the
    /// <see cref="ResourceInUseException"/> of the create surfaces, and sending the command again creates the table once the delete is through.
    /// </para>
    /// <para>
    /// When <c>DescribeTable</c> finds no table, the command creates it. That can still find a table, because <c>DescribeTable</c> is eventually consistent, and instances of a program that start together create at the same
    /// time: <see cref="ResourceInUseException"/> then ends in the description of the table it found. When <c>DescribeTable</c> finds nothing after such an answer, either a delete just ended or another process just created
    /// the table, so the command creates again, up to 5 times over 1.5 seconds, which ends in a new table or in the one that turned up. The <see cref="ResourceInUseException"/> surfaces only if that settles nothing.
    /// </para>
    /// </remarks>
    /// <returns>This command.</returns>
    public CreateTableCommand IfNotExists()
    {
        _ifNotExists = true;
        return this;
    }

    /// <summary>
    /// Builds the request that <see cref="SendAsync"/> would send, and sends nothing. It is the escape hatch for what this command does not model: change it with <c>with</c>, as in
    /// <c>request with { Tags = tags }</c>, and send it with <c>client.CreateTableAsync(request)</c>. <see cref="Stream"/> is in it. <see cref="WaitUntilActive()"/>, <see cref="IfNotExists"/> and <see cref="TimeToLive(string)"/>
    /// happen around the request and are not.
    /// </summary>
    /// <returns>The request.</returns>
    public CreateTableRequest ToRequest() => _schema.ToRequest(_table.Name, _capacity, _stream);

    /// <summary>
    /// Renders the body of the <c>CreateTable</c> request that <see cref="SendAsync"/> would send, as DynamoDB JSON, and sends nothing: the JSON of <see cref="ToRequest"/>. It is meant for tests and debugging.
    /// <see cref="IfNotExists"/>, <see cref="WaitUntilActive()"/> and <see cref="TimeToLive(string)"/> make calls of their own around the request, which are not in it.
    /// </summary>
    /// <remarks>Unlike <c>ToRequestJson</c> of the commands of items, it does not end the command, which stays this ordinary class: it can be rendered and then sent.</remarks>
    /// <returns>The request body.</returns>
    public string ToRequestJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = WireJson.CreateWriter(buffer))
        {
            CreateTableOperation.WriteRequest(new DynamoDbWriter(json), ToRequest());
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Creates the table (<c>CreateTable</c>). Creation is asynchronous: without <see cref="WaitUntilActive()"/> or <see cref="TimeToLive(string)"/>, which implies it, the table is normally <c>CREATING</c> when this returns.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call, and the wait. A table DynamoDB accepted stays accepted, so a cancelled wait leaves a table behind.</param>
    /// <returns>
    /// The table and what this call did: <see cref="CreateTableResult.Table"/> is the new description, the active one with <see cref="WaitUntilActive()"/>, or the existing one with <see cref="IfNotExists"/>, which is
    /// what the first look found; <see cref="CreateTableResult.Created"/> and <see cref="CreateTableResult.TimeToLiveEnabled"/> say whether this call created the table and turned time to live on.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceInUseException">A table of that name exists and <see cref="IfNotExists"/> was not called, or it is being deleted, which <see cref="IfNotExists"/> does not cover.</exception>
    /// <exception cref="AccessDeniedException">The role lacks a permission of the calls this makes, which <see cref="IfNotExists"/> lists.</exception>
    /// <exception cref="LimitExceededException">The account has too many tables, or creates or deletes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the name is malformed or the table has too many indexes, or time to live cannot be turned on, as when it is on for another attribute.</exception>
    /// <exception cref="TableNotActiveException">
    /// The table or an index of it was not active in time, with <see cref="WaitUntilActive()"/> or <see cref="TimeToLive(string)"/>, or the table did not list a global index this command declares, which ends the wait early.
    /// </exception>
    /// <exception cref="TimeoutException">A call ran out of <see cref="DynamoDbClientOptions.AttemptTimeout"/> on its last attempt.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public async ValueTask<CreateTableResult> SendAsync(CancellationToken cancellationToken = default)
    {
        var existing = await FindExistingAsync(cancellationToken).ConfigureAwait(false);
        var (description, created) = existing is null ? await CreateAsync(cancellationToken).ConfigureAwait(false) : (existing, false);
        var declared = _schema.GlobalIndexNames;
        var timeout = _activeTimeout ?? (_timeToLive is null ? null : TableAdministration.DefaultActiveTimeout);
        if (timeout is { } bound && (existing is null || !TableAdministration.IsActive(existing, declared)))
        {
            // The create sent every declared index, and a table that exists gets one only from an UpdateTable sent before this command, so an index still not listed after the catch-up will not be.
            description = await TableAdministration.WaitUntilActiveAsync(_table, declared, bound, unlistedIsFinal: true, cancellationToken).ConfigureAwait(false);
        }

        var timeToLiveEnabled = _timeToLive is { } attributeName && await TableAdministration.EnsureTimeToLiveAsync(_table, attributeName, cancellationToken).ConfigureAwait(false);
        return new CreateTableResult(description ?? await TableAdministration.DescribeAsync(_table, cancellationToken).ConfigureAwait(false), created, timeToLiveEnabled);
    }

    /// <summary>Lets <c>await</c> send the command, for <c>await orders.CreateTable()</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<CreateTableResult> GetAwaiter() => SendAsync().GetAwaiter();

    // The description is null only for a response without one. Created is false for a table another process created first.
    private async ValueTask<(TableDescription? Description, bool Created)> CreateAsync(CancellationToken cancellationToken)
    {
        var delay = TableAdministration.FirstDelay;
        for (var create = 1; ; create++)
        {
            ResourceInUseException inUse;
            try
            {
                var response = await _table.Core
                    .ExecuteAsync<CreateTableOperation, CreateTableRequest, CreateTableResponse>(ToRequest(), cancellationToken)
                    .ConfigureAwait(false);
                return (response.TableDescription, true);
            }
            catch (ResourceInUseException exception) when (_ifNotExists)
            {
                inUse = exception;
            }

            // Outside the catch: a throw after an await in one has a branch of the compiler that nothing can take.
            var existing = await FindAsync(cancellationToken).ConfigureAwait(false);
            if (existing is { TableStatus: not TableStatus.Deleting })
            {
                return (existing, false);
            }

            if (existing is not null || create == TableAdministration.CatchUpLooks)
            {
                ExceptionDispatchInfo.Throw(inUse);
            }

            await Task.Delay(delay, _table.Core.TimeProvider, cancellationToken).ConfigureAwait(false);
            delay *= 2;
        }
    }

    // A table that is going away is none: the create loop finds out what it is once it is gone.
    private async ValueTask<TableDescription?> FindExistingAsync(CancellationToken cancellationToken)
    {
        if (!_ifNotExists)
        {
            return null;
        }

        var found = await FindAsync(cancellationToken).ConfigureAwait(false);
        return found is { TableStatus: not TableStatus.Deleting } ? found : null;
    }

    // CreateTable answers ResourceInUseException for a table on its way out too, so what it found may be gone, and DescribeTable may not see yet what another process created.
    private async ValueTask<TableDescription?> FindAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await TableAdministration.DescribeAsync(_table, cancellationToken).ConfigureAwait(false);
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }
}
