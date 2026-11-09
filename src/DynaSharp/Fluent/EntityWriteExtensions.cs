using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// The clauses of a write of an entity that its write rules ask for: <c>IfVersion</c> and <c>IgnoreVersion</c> for an entity with a <see cref="VersionAttribute"/>, and
/// <c>StoreKeyInputs</c> for an update that may create the item. These are extension methods so that a write of a table without a schema, which has neither, does not offer them.
/// </summary>
/// <example>
/// <code>
/// var cart = await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(ct);
/// await carts.Update(Cart.Keys.Of("bob")).Set(Cart.Attrs.Lines, lines).IfVersion(cart!.Revision).SendAsync(ct);
/// </code>
/// </example>
public static class EntityWriteExtensions
{
    /// <summary>
    /// Updates only if the item has this version, and stores the next one, which <see cref="WriteResult{T}.Version"/> returns. If the item has another version, sending throws
    /// <see cref="ConditionalCheckFailedException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required for a versioned entity.</b> A key does not carry a version, so an update of an entity with a <see cref="VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion{T}(UpdateCommand{T})"/>. Nothing is read or retried by itself:
    /// <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> is how to ask for that, around code that reads the version again.
    /// </para>
    /// <para>
    /// <b>Version 0</b> stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key and fails on any item under the key, also on one without a version
    /// attribute, which <see cref="IgnoreVersion{T}(UpdateCommand{T})"/> writes to. With <see cref="VersionMissing.Allow"/> it looks at the version attribute instead, and writes to an item
    /// without one or with 0 in it.
    /// </para>
    /// <para>
    /// <b>Setting the version yourself.</b> An update that sets, adds to or removes the version attribute by its own action, before or after this, keeps that action in place of the next version,
    /// and still checks this one. <see cref="WriteResult{T}.Version"/> is then <see langword="null"/>, because the update does not know what the action stores.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="version">The version last read.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or <see cref="long.MaxValue"/>, which has no next one.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no <see cref="VersionAttribute"/>.</exception>
    public static UpdateCommand<T> IfVersion<T>(this UpdateCommand<T> command, long version)
        where T : IDynamoDbEntity<T>
    {
        WriteRules.CheckVersion(version);
        var state = command.Enter();
        WriteRules.AdvanceVersion(state, version);
        return new(state, state.Table);
    }

    /// <summary>
    /// Stores the inputs of the key of the table as attributes of the item, as a <c>Put</c> does, for an update that may create the item: without it, an item an update created holds the
    /// key alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is written.</b> <c>SET #userId = if_not_exists(#userId, :v)</c> per input, after the actions of the update and the keys of its indexes and before the discriminator. An item that
    /// exists keeps what it has, so this goes with <c>IfExists()</c>, <c>IfNotExists()</c> and a version alike. The value goes through the converter of the property and is the value, not the
    /// text of the key, and an action of the update on the attribute by a path given as a string takes precedence.
    /// </para>
    /// <para>
    /// <b>Why.</b> A read of the entity restores the inputs from the key, but <c>Select</c> of such an input and a filter on it find nothing in an item that holds the key alone. A plain key
    /// and a key made of one property over its own attribute are their attribute, and a <see cref="Mapping.KeyOnlyAttribute"/> has none, so an entity with nothing else changes nothing here.
    /// </para>
    /// <para>
    /// <b>A key given as strings.</b> A template that cuts a value off, such as a timestamp in <c>{At:yyyy-MM-dd}</c>, gives it back only from a key the generator built, so a key given as
    /// strings throws for it when the update ends.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no key inputs, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static UpdateCommand<T> StoreKeyInputs<T>(this UpdateCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        WriteRules.StoreKeyInputs(state);
        return new(state, state.Table);
    }

    /// <summary>
    /// Lets the update go out without the check of the version, and still stores the next version, so that a copy read before it fails its next checked write. <see cref="WriteResult{T}.Version"/> is
    /// <see langword="null"/>, because the update does not know the version it advanced from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version still advances.</b> Every write of a versioned entity stores a new version, also one without the check: an update adds 1 to the stored version,
    /// <c>SET #v = if_not_exists(#v, :zero) + :one</c>, so an item without one, or a new item, gets version 1, and a put or a batch stores the version of its entity plus one, as a checked put does. A writer
    /// that holds a copy read before this write then fails with <see cref="ConditionalCheckFailedException"/>, or in a transaction with <see cref="TransactionCanceledException"/>, instead of overwriting it.
    /// What this write itself overwrites is not checked: it wins over whatever another writer stored since, so it suits a change that does not depend on what was read, such as an <c>Add</c> to a counter.
    /// </para>
    /// <para>
    /// <b>Setting the version yourself.</b> An update that sets, adds to or removes the version attribute by its own action keeps that action, and the version is not advanced besides it.
    /// <c>IfVersion(n)</c> after this checks <c>n</c> and stores <c>n + 1</c>; before it, this throws.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="InvalidOperationException">The update already has <c>IfVersion</c>, whose condition cannot be taken back.</exception>
    public static UpdateCommand<T> IgnoreVersion<T>(this UpdateCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        state.IgnoreVersion(isUpdate: true);
        return new(state, state.Table);
    }

    /// <summary>
    /// Writes the entity without the check of its version, for imports, migrations and an item written without a version, and still stores the next version, as a checked put does:
    /// <see cref="WriteResult{T}.Version"/> is the version of the entity plus one. Conditions of your own stay, it holds wherever it stands in the chain, and on an entity without a <see cref="VersionAttribute"/> it changes nothing.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="EntityWriteExtensions.IgnoreVersion{T}(UpdateCommand{T})" path="/remarks/para[1]"/>
    /// <para>
    /// <b>Taking over an item without a version.</b> Such an item reads as version 0, and a checked put at 0 fails with <see cref="ConditionalCheckFailedException"/>, because 0 says that no item has the key.
    /// <c>orders.Put(order).IgnoreVersion()</c> of the entity as it was read stores version 1, and the next write is an ordinary one. For a table that keeps such items, <see cref="VersionMissing.Allow"/> is the better answer.
    /// </para>
    /// <para>
    /// <b>The last version.</b> An entity at the last version its property holds throws <see cref="OverflowException"/> at <see cref="Table{T}.Put(T)"/>, because it has no next one.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static PutCommand<T> IgnoreVersion<T>(this PutCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        state.IgnoreVersion();
        return new(state, state.Table);
    }

    /// <summary>
    /// Deletes only if the item has this version. If the item has another version, sending throws <see cref="ConditionalCheckFailedException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required for a versioned entity.</b> A key does not carry a version, so a delete by a key of an entity with a <see cref="VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion{T}(DeleteCommand{T})"/>; a delete of an entity takes the version from it, and this checks another one in its place.
    /// <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> repeats code that reads the version again.
    /// </para>
    /// <para>
    /// <b>Version 0</b> stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key, and the delete fails on any item under the key. With
    /// <see cref="VersionMissing.Allow"/> it looks at the version attribute instead, and deletes an item without one or with 0 in it.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="version">The version last read.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or <see cref="long.MaxValue"/>.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no <see cref="VersionAttribute"/>.</exception>
    public static DeleteCommand<T> IfVersion<T>(this DeleteCommand<T> command, long version)
        where T : IDynamoDbEntity<T>
    {
        WriteRules.CheckVersion(version);
        var state = command.Enter();
        WriteRules.IfVersion(state, version);
        return new(state, state.Table);
    }

    /// <summary>Lets the delete go out without the check.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static DeleteCommand<T> IgnoreVersion<T>(this DeleteCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        state.IgnoreVersion();
        return new(state, state.Table);
    }
}
