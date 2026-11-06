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
/// Unlike the commands on items, this is an ordinary class: a clause changes the command and returns it, it can be kept in a field and sent again, and it is not thread-safe. A
/// retry repeats the request: when a response was lost, the retry of a table this very call created fails with <see cref="ResourceInUseException"/>, which <see cref="IfNotExists"/> turns into success.
/// </para>
/// </remarks>
public sealed class CreateTableCommand
{
    // What a CancellationTokenSource takes: uint.MaxValue - 1 milliseconds.
    private const double MaxTimeoutMilliseconds = 4294967294;

    private readonly TableModel _table;

    private readonly TableSchema _schema;

    private ProvisionedThroughput? _capacity;

    private bool _ifNotExists;

    private TimeSpan? _activeTimeout;

    private StreamViewType? _stream;

    private string? _timeToLive;

    private TimeSpan? _timeout;

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
    /// The entity disagrees with the table or with an entity before it, which the message names both of: the key of the table has other names, it stores an attribute as another type, it declares an index of
    /// that name over other attributes, or as local where the other is global, or its <see cref="TimeToLiveAttribute"/> is on another attribute, since a table has time to live on one.
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
    /// Makes <see cref="SendAsync"/> wait until the table and its global secondary indexes are <c>ACTIVE</c>, as <see cref="Table{T}.WaitUntilActiveAsync(TimeSpan, CancellationToken)"/> does.
    /// Without this, the table is <c>CREATING</c> when the call returns, and a read or write of it fails until it is active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An index added later.</b> With <see cref="IfNotExists"/> over a table that exists, an index that <c>UpdateTable</c> added can still be <c>CREATING</c>, which takes minutes even on an empty
    /// table and often longer than the default of five, so give that wait half an hour or more.
    /// </para>
    /// <para>
    /// Every global index that this command declares, by its entity and by <see cref="Including{TOther}"/>, has to be listed and active, and a local one needs no wait. With <see cref="IfNotExists"/> over an older table that lacks
    /// a declared index, nothing adds the index, so this <b>fails instead of returning</b>, with a <see cref="TableNotActiveException"/> that names it. It does not wait out the timeout for that: an index still not listed at
    /// the fifth look in a row, 1.5 seconds in, ends the wait, because a create lists all its indexes and only an <c>UpdateTable</c> sent before this command adds one. Add the index with <c>UpdateTable</c> in <c>DynaSharp.Requests</c> first.
    /// That needs an active table, which a table opened without an entity waits for, because it declares nothing: <c>client.Table(name, "pk", "sk").WaitUntilActiveAsync()</c>. Then this wait follows the backfill.
    /// </para>
    /// <para>
    /// The timeout bounds the wait and nothing else, so the calls before it and time to live after it take their own time. <see cref="Timeout(TimeSpan)"/> bounds all of it and names the table when it runs out, where a
    /// <see cref="CancellationToken"/> on a timer gives a bare <see cref="TaskCanceledException"/> or <see cref="OperationCanceledException"/>, with no table and no index named.
    /// </para>
    /// </remarks>
    /// <param name="timeout">How long to wait after the table was created. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> is not a bound and is rejected.</param>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative, which this call finds out, so that no table is created for it.</exception>
    public CreateTableCommand WaitUntilActive(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        _activeTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Gives the table a stream, which <see cref="ToRequest"/> shows. It applies to a table this command creates: with <see cref="IfNotExists"/>, the stream of a table that exists stays untouched, and one that is off or
    /// of another view is a <see cref="TableDriftKind.StreamDiffers"/> in <see cref="CreateTableResult.Drift"/>. Change it with <c>UpdateTable</c> in <c>DynaSharp.Requests</c>.
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
    /// <see cref="ToRequest"/>: it happens after the create, or after finding the table. An entity of the command with <see cref="TimeToLiveAttribute"/> asks for the same without this call, and this call
    /// replaces what the entities declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It needs an active table, so it implies <see cref="WaitUntilActive()"/>, with the five minutes of that method unless <see cref="WaitUntilActive(TimeSpan)"/> gave another timeout, and with all its rules: a wait that runs out
    /// throws <see cref="TableNotActiveException"/> and leaves time to live as it was, also when a table <see cref="IfNotExists"/> found lacks a global index this command declares. So a program that adds an index to a table
    /// that exists calls this after it added the index.
    /// </para>
    /// <para>
    /// Then <c>DescribeTimeToLive</c> looks, and <c>ENABLED</c> or <c>ENABLING</c> on this attribute ends it. <c>ENABLED</c> or <c>ENABLING</c> on another attribute ends it too, and changes nothing: it is a
    /// <see cref="TableDriftKind.TimeToLiveDiffers"/> in <see cref="CreateTableResult.Drift"/>, since turning it off and on again with the new name is a decision this command does not take, and one that DynamoDB
    /// spreads over two changes. Anything else, time to live off for one, sends <c>UpdateTimeToLive</c>, which turns it on, on a table that exists too. DynamoDB answers that with a <see cref="ValidationException"/>
    /// for time to live that is on already, and for a second change within the hour that a change takes. Instances of a program start together, so that can mean that another one got there first: the command looks
    /// again and takes it for success if time to live is now on, for this attribute or, as drift, for another. Otherwise that exception surfaces. <see cref="CreateTableResult.TimeToLiveEnabled"/> is
    /// <see langword="true"/> only after an update DynamoDB accepted.
    /// </para>
    /// <para>
    /// <b>Permissions.</b> It takes <c>dynamodb:DescribeTimeToLive</c> on every call, <c>dynamodb:UpdateTimeToLive</c> when time to live is not on yet, and <c>dynamodb:DescribeTable</c> for the wait.
    /// </para>
    /// <para>
    /// The attribute of an item must be a number that holds a Unix time in seconds, which <c>[DynamoDbConverter(typeof(EpochSecondsConverter))]</c> on a <see cref="DateTimeOffset"/> writes, and which <see cref="TimeToLiveAttribute"/> checks at build time. See <see cref="UpdateTimeToLiveRequest"/> for when DynamoDB deletes.
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
    /// Bounds the whole of <see cref="SendAsync"/>, every call it makes and every wait, for a program that creates its table at the start and must not hang there. When the time runs out,
    /// <see cref="SendAsync"/> throws a <see cref="TimeoutException"/> that names the table, the timeout and the step it was at.
    /// </summary>
    /// <remarks>
    /// The bound covers the look of <see cref="IfNotExists"/>, the <c>CreateTable</c> with its retries, the wait of <see cref="WaitUntilActive()"/>, and turning time to live on. The call in flight is
    /// cancelled, where a token of the caller on a timer gives a bare <see cref="OperationCanceledException"/>.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// The time runs on <see cref="DynamoDbClientOptions.TimeProvider"/> of the client. A table that DynamoDB accepted stays accepted, so a timeout after the create leaves the table behind, and sending the command again with
    /// <see cref="IfNotExists"/> goes on from there. The bound of <see cref="WaitUntilActive(TimeSpan)"/> still applies inside it, and a wait that runs out first throws its <see cref="TableNotActiveException"/>, which names the
    /// index it waited for. This adds no wait: without <see cref="WaitUntilActive()"/> or time to live, it bounds the calls alone. A <see cref="DynamoDbClientOptions.AttemptTimeout"/> of a call is its own and still throws its
    /// own <see cref="TimeoutException"/>.
    /// </para>
    /// <para>
    /// A token of the caller still cancels as it did, with an <see cref="OperationCanceledException"/> of that token, also when both happen at once.
    /// </para>
    /// </remarks>
    /// <param name="timeout">The time for the whole command, more than zero, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for no bound.</param>
    /// <returns>This command.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero, negative other than infinite, or above 4294967294 milliseconds, which this call finds out before a table is created.</exception>
    public CreateTableCommand Timeout(TimeSpan timeout)
    {
        if (timeout != System.Threading.Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > MaxTimeoutMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout of a CreateTable is more than zero and at most 4294967294 milliseconds, or Timeout.InfiniteTimeSpan for no bound.");
        }

        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Treats a table that exists as success: <c>DescribeTable</c> goes first, and a table it finds ends the create there, with its description and without a <c>CreateTable</c>, which is what makes a create at the start
    /// of a program safe to repeat. The table it finds is checked against what the command declares. <see cref="TimeToLive(string)"/> on the chain still goes on, and can change the table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What it checks.</b> The description it already has holds the schema, so the check takes no call. A key of the table that differs from the key of the command throws an <see cref="InvalidOperationException"/>
    /// that names the table, the key it expects and the key it found, before any wait or time to live: another partition or sort key attribute, a sort key the command has and the table lacks or the other way round,
    /// or a key attribute of another type where both sides know it. That table fails every read and write of the entities with a <see cref="ValidationException"/>, and DynamoDB cannot change the key of a table,
    /// so a deploy should stop there. A table opened without an entity, <c>client.Table(name, "pk", "sk")</c>, knows its key names and not their types, so only the names count.
    /// </para>
    /// <para>
    /// What lets part of the code work is no exception, but <see cref="CreateTableResult.Drift"/>, which the command also logs once as a warning: an index the entities declare that the table lacks, or has with other keys
    /// or as the other kind, time to live on another attribute than the one asked for, and, with <see cref="Stream"/> on the chain, a stream that is off or of another view. The command changes none of it, as the indexes
    /// and the stream of a command reach only a table it creates: a global index goes in with <c>UpdateTable</c>, and a local one only with a new table. What the table has beyond the command, such as an index or
    /// time to live it does not ask for, is no drift, since other code may use it. A table that is still being created is success with that status. A <see cref="WaitUntilActive()"/> still fails over a table that
    /// lacks a global index this command declares. <see cref="CreateTableResult.Created"/> says whether the table was found.
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
    /// Creates the table (<c>CreateTable</c>). Creation is asynchronous: without <see cref="WaitUntilActive()"/> or time to live, of <see cref="TimeToLive(string)"/> or of a <see cref="TimeToLiveAttribute"/> of an entity, which implies it, the table is normally <c>CREATING</c> when this returns.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call, and the wait. A table DynamoDB accepted stays accepted, so a cancelled wait leaves a table behind.</param>
    /// <returns>
    /// The table and what this call did: <see cref="CreateTableResult.Table"/> is the new description, the active one with <see cref="WaitUntilActive()"/>, or the existing one with <see cref="IfNotExists"/>, which is
    /// what the first look found; <see cref="CreateTableResult.Created"/> and <see cref="CreateTableResult.TimeToLiveEnabled"/> say whether this call created the table and turned time to live on, and
    /// <see cref="CreateTableResult.Drift"/> how a table it found differs from what it declares.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceInUseException">A table of that name exists and <see cref="IfNotExists"/> was not called, or it is being deleted, which <see cref="IfNotExists"/> does not cover.</exception>
    /// <exception cref="AccessDeniedException">The role lacks a permission of the calls this makes, which <see cref="IfNotExists"/> lists.</exception>
    /// <exception cref="LimitExceededException">The account has too many tables, or creates or deletes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the name is malformed or the table has too many indexes, or time to live cannot be turned on, as within the hour after a change of it.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="IfNotExists"/> found a table whose key differs from the key of the command, in attribute names, in having a sort key or in a type that both know. The message names the table, the expected and the found
    /// key. Nothing waits and nothing changes then.
    /// </exception>
    /// <exception cref="TableNotActiveException">
    /// The table or an index of it was not active in time, with <see cref="WaitUntilActive()"/> or <see cref="TimeToLive(string)"/>, or the table did not list a global index this command declares, which ends the wait early.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// The command ran out of <see cref="Timeout(TimeSpan)"/>, with a message that names the table, the timeout and the step, and the <see cref="OperationCanceledException"/> of the call in flight inside it. Or a call ran out of
    /// <see cref="DynamoDbClientOptions.AttemptTimeout"/> on its last attempt.
    /// </exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public async ValueTask<CreateTableResult> SendAsync(CancellationToken cancellationToken = default)
    {
        if (_timeout is not { } bound || bound == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return await RunAsync(null, cancellationToken).ConfigureAwait(false);
        }

        using var deadline = new CancellationTokenSource(bound, _table.Core.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var progress = new Progress();
        try
        {
            return await RunAsync(progress, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            // The calls saw the linked token, and a caller tells its own cancellation by its own token, which wins when both came.
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(exception.Message, exception, cancellationToken);
            }

            throw new TimeoutException($"The CreateTable of {_table.Name} did not end within its timeout of {bound}: it was {progress.Step}. A table DynamoDB accepted stays, and sending the command again with IfNotExists() goes on from there.", exception);
        }
    }

    /// <summary>Lets <c>await</c> send the command, for <c>await orders.CreateTable()</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<CreateTableResult> GetAwaiter() => SendAsync().GetAwaiter();

    // The steps say where a timeout met the command; without one there is nothing to tell, and no progress is kept.
    private async ValueTask<CreateTableResult> RunAsync(Progress? progress, CancellationToken cancellationToken)
    {
        progress?.At("looking for the table");
        var existing = await FindExistingAsync(cancellationToken).ConfigureAwait(false);
        progress?.At("creating the table");
        var (description, created) = existing is null ? await CreateAsync(cancellationToken).ConfigureAwait(false) : (existing, false);

        // A table this call created is what it declares. One it found is compared before anything waits for it or changes it, so a key that does not fit throws first.
        var drift = created ? [] : Check(description!);
        var declared = _schema.GlobalIndexNames;
        var timeToLive = _timeToLive ?? _schema.TimeToLiveAttributeName;
        var timeout = _activeTimeout ?? (timeToLive is null ? null : TableAdministration.DefaultActiveTimeout);
        if (timeout is { } bound && (existing is null || !TableAdministration.IsActive(existing, declared)))
        {
            progress?.At("waiting for the table to become active");

            // The create sent every declared index, and a table that exists gets one only from an UpdateTable sent before this command, so an index still not listed after the catch-up will not be.
            description = await TableAdministration.WaitUntilActiveAsync(_table, declared, bound, unlistedIsFinal: true, cancellationToken).ConfigureAwait(false);
        }

        progress?.At("turning time to live on");
        var timeToLiveEnabled = false;
        if (timeToLive is { } attributeName)
        {
            var outcome = await TableAdministration.EnsureTimeToLiveAsync(_table, attributeName, cancellationToken).ConfigureAwait(false);
            timeToLiveEnabled = outcome.TurnedOn;
            if (outcome.OtherAttribute is { } other)
            {
                var differs = $"The table {_table.Name} has time to live on the attribute {other}, and the command asks for {attributeName}, so items are not expired by {attributeName}. "
                    + "A table has time to live on one attribute: turning it to another is UpdateTimeToLive to off, and to on with the new name once DynamoDB takes a change again, within an hour.";
                drift.Add(new TableDrift(TableDriftKind.TimeToLiveDiffers, null, differs));
            }
        }

        if (drift.Count > 0)
        {
            _table.Core.LogTableDrift(_table.Name, drift);
        }

        progress?.At("describing the table");
        return new CreateTableResult(description ?? await TableAdministration.DescribeAsync(_table, cancellationToken).ConfigureAwait(false), created, timeToLiveEnabled, drift);
    }

    // The stream is compared only where the chain asks for one, and only with a description that holds a key schema, which TableSchema.Check says.
    private List<TableDrift> Check(TableDescription found)
    {
        var drift = _schema.Check(_table.Name, found);
        if (_stream is { } view && found.KeySchema.Count > 0)
        {
            var current = found.StreamSpecification is { StreamEnabled: true } stream ? stream.StreamViewType : (StreamViewType?)null;
            if (current != view)
            {
                var has = current is { } other ? $"a stream of {other}" : "no stream";
                drift.Add(new TableDrift(TableDriftKind.StreamDiffers, null, $"The table {_table.Name} has {has}, and the command asks for one of {view}. UpdateTable with a StreamSpecification changes it."));
            }
        }

        return drift;
    }

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

    /// <summary>The step a command with a timeout is at, for the message of the <see cref="TimeoutException"/>.</summary>
    private sealed class Progress
    {
        public string Step { get; private set; } = string.Empty;

        public void At(string step) => Step = step;
    }
}
