using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;

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
/// <see cref="Serialization.IAddableConverter"/>, which makes the key a number; any other counts as a string, so a custom converter that writes binary needs <see cref="ToRequest"/>. For streams, tags, time to
/// live, a storage class or a projection of fewer attributes, take <see cref="ToRequest"/> and send it through <c>DynaSharp.Requests</c>.
/// </para>
/// <para>
/// This is administration and not a hot path, so unlike the commands of items it is an ordinary class: a clause changes the command and returns it, it can be kept and sent again, and it is not thread-safe. A
/// retry repeats the request: when a response was lost, the retry of a table this very call created fails with <see cref="ResourceInUseException"/>, which <see cref="IfNotExists"/> turns into success.
/// </para>
/// </remarks>
public sealed class CreateTableCommand
{
    // With the waits of TableAdministration.FirstDelay in between, doubling, this gives DescribeTable 1.5 seconds to catch up with a create it cannot see yet.
    private const int MostCreates = 5;

    private readonly TableModel _table;

    private readonly TableSchema _schema;

    private ProvisionedThroughput? _capacity;

    private bool _ifNotExists;

    private TimeSpan? _activeTimeout;

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

    /// <summary>Makes <see cref="SendAsync"/> wait until the table and its global secondary indexes are <c>ACTIVE</c>, for at most five minutes. See <see cref="WaitUntilActive(TimeSpan)"/>.</summary>
    /// <returns>This command.</returns>
    public CreateTableCommand WaitUntilActive()
    {
        _activeTimeout = TableAdministration.DefaultActiveTimeout;
        return this;
    }

    /// <summary>
    /// Makes <see cref="SendAsync"/> wait until the table and its global secondary indexes are <c>ACTIVE</c>, as <see cref="Table{T}.WaitUntilActiveAsync(TimeSpan, CancellationToken)"/> does, with its rules on the timeout.
    /// Without this, the table is <c>CREATING</c> when the call returns, and a read or write of it fails until it is active. With <see cref="IfNotExists"/> over a table that has data, an index can still backfill, which
    /// can take longer than the default of five minutes.
    /// </summary>
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
    /// Treats a table that exists as success: <see cref="ResourceInUseException"/> ends in the description of the existing table, which <c>DescribeTable</c> fetches, and that is what makes a create at the start of a
    /// program safe to repeat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It <b>does not compare schemas</b>: a table of this name with other keys or indexes is success too, and a table that is still being created is success with that status. Reads and writes then fail, or find the
    /// indexes missing.
    /// </para>
    /// <para>
    /// A table that is going away is no success, with or without a wait, because no table is left afterwards: when <c>DescribeTable</c> says <c>DELETING</c>, the <see cref="ResourceInUseException"/> of the create
    /// surfaces, and sending the command again creates the table once the delete is through.
    /// </para>
    /// <para>
    /// When <c>DescribeTable</c> finds no table, either a delete just ended or, because that call is eventually consistent, another process just created the table, as instances of a program that start together do.
    /// So the command creates again, up to 5 times over 1.5 seconds, which ends in a new table or in the one that turned up. The <see cref="ResourceInUseException"/> surfaces only if that settles nothing.
    /// </para>
    /// <para>
    /// It finds out by trying: <c>CreateTable</c> goes out on every call, also for a table that exists, and <c>DescribeTable</c> follows when that fails. So the role needs <c>dynamodb:CreateTable</c> as well as
    /// <c>dynamodb:DescribeTable</c>, also where the table is always there, and every call on an existing table leaves a failed <c>CreateTable</c> in CloudTrail and in error metrics. With
    /// <see cref="WaitUntilActive()"/> on an existing table the command describes twice, once to find the table and once to see that it is active.
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
    /// <c>request with { Tags = tags }</c>, and send it with <c>client.CreateTableAsync(request)</c>. <see cref="WaitUntilActive()"/> and <see cref="IfNotExists"/> happen around the request and are not in it.
    /// </summary>
    /// <returns>The request.</returns>
    public CreateTableRequest ToRequest() => _schema.ToRequest(_table.Name, _capacity);

    /// <summary>
    /// Creates the table (<c>CreateTable</c>). Creation is asynchronous: without <see cref="WaitUntilActive()"/> the table is normally <c>CREATING</c> when this returns.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call, and the wait. A table DynamoDB accepted stays accepted, so a cancelled wait leaves a table behind.</param>
    /// <returns>The description of the table: the new one, the active one with <see cref="WaitUntilActive()"/>, or the existing one with <see cref="IfNotExists"/>.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceInUseException">A table of that name exists and <see cref="IfNotExists"/> was not called, or it is being deleted, which <see cref="IfNotExists"/> does not cover.</exception>
    /// <exception cref="LimitExceededException">The account has too many tables, or creates or deletes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the name is malformed or the table has too many indexes.</exception>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time, with <see cref="WaitUntilActive()"/>.</exception>
    /// <exception cref="TimeoutException">A call ran out of <see cref="DynamoDbClientOptions.AttemptTimeout"/> on its last attempt.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public async ValueTask<TableDescription> SendAsync(CancellationToken cancellationToken = default)
    {
        var created = await CreateAsync(cancellationToken).ConfigureAwait(false);
        if (_activeTimeout is { } timeout)
        {
            return await TableAdministration.WaitUntilActiveAsync(_table, timeout, cancellationToken).ConfigureAwait(false);
        }

        return created ?? await TableAdministration.DescribeAsync(_table, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lets <c>await</c> send the command, for <c>await orders.CreateTable()</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<TableDescription> GetAwaiter() => SendAsync().GetAwaiter();

    // Null only for a response without a description.
    private async ValueTask<TableDescription?> CreateAsync(CancellationToken cancellationToken)
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
                return response.TableDescription;
            }
            catch (ResourceInUseException exception) when (_ifNotExists)
            {
                inUse = exception;
            }

            // Outside the catch: a throw after an await in one has a branch of the compiler that nothing can take.
            var existing = await FindAsync(cancellationToken).ConfigureAwait(false);
            if (existing is { TableStatus: not TableStatus.Deleting })
            {
                return existing;
            }

            if (existing is not null || create == MostCreates)
            {
                ExceptionDispatchInfo.Throw(inUse);
            }

            await Task.Delay(delay, _table.Core.TimeProvider, cancellationToken).ConfigureAwait(false);
            delay *= 2;
        }
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
