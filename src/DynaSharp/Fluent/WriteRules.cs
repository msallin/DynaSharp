using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What the commands that write have in common beyond their bytes: the rules of <see cref="VersionAttribute"/> and of key templates, and the discriminator an update owes. A rule that a
/// clause cannot decide waits for <see cref="Settle"/>, because the clause that fulfils it may still follow. The rules of versions and keys start from <see cref="TableModel.Writes"/>, which
/// is <see langword="null"/> for a table without such an entity, so a command on that table costs a null check per clause.
/// </summary>
internal static class WriteRules
{
    /// <summary>
    /// Writes a whole item, and for an entity with a version the next version. The condition on it has to wait for <see cref="Settle"/>, because <see cref="IgnoreVersion"/> can still take it
    /// back, and that also writes the item again, so the entity stays at hand until then. A struct gets a box for that, which is the one thing a put of a version costs.
    /// </summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="item">The item.</param>
    public static void WriteItem<T>(CommandState state, T item)
        where T : IDynamoDbDocument<T>
    {
        var writer = state.StartItem();
        if (state.Table.Writes?.VersionAttributeName is null)
        {
            T.WriteAttributes(writer, item);
            return;
        }

        var held = T.WriteNextVersion(writer, item);
        state.UnsettledVersion = state.Table;
        state.EntityVersion = held;
        state.StoredVersion = held + 1;
        state.PutEntity = item;
        state.WritePutEntity = PlainWrite<T>.Write;
    }

    /// <summary>Marks a write whose key does not carry a version, so it must say how it treats one before it goes out.</summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectVersion(CommandState state)
    {
        state.UnsettledVersion = state.Table.Writes?.VersionAttributeName is null ? null : state.Table;
        return state;
    }

    /// <summary>
    /// Marks an update. It owes what any write by key owes a version, and because <c>UpdateItem</c> can create the item, the discriminator of the entity, without which a typed read would
    /// not see it, and the keys of an index that has only literals, without which the item would not be in an index that holds every item. <see cref="Settle"/> writes those, because only then
    /// is it known whether the update has an action of its own on the attribute.
    /// </summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectUpdate(CommandState state)
    {
        state.OwesDiscriminator = state.Table.UpdateWritesDiscriminator;
        state.OwesLiteralKeys = state.Table.Writes is { LiteralKeys.Length: > 0 };
        return ExpectVersion(state);
    }

    /// <summary>
    /// Marks a write that takes its version from an entity. The condition has to wait for <see cref="Settle"/>, because <c>IgnoreVersion</c> can still take it back, or <c>IfVersion</c> replace it, and
    /// what stands in the expressions cannot be taken out. The version is read here, while there is an entity.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="entity">The entity.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectVersionOf<T>(CommandState state, T entity)
        where T : IDynamoDbEntity<T>
    {
        if (ExpectVersion(state).UnsettledVersion is not null)
        {
            state.EntityVersion = T.ReadVersion(entity);
        }

        return state;
    }

    /// <summary>Marks a write of a batch, which cannot carry the condition a version needs.</summary>
    /// <param name="state">The state of the batch.</param>
    /// <param name="table">The table of the write.</param>
    public static void NoteBatchWrite(CommandState state, TableModel table)
    {
        if (table.Writes?.VersionAttributeName is not null && !state.VersionIgnored)
        {
            state.UnsettledVersion = table;
        }
    }

    /// <summary>Checks a version a caller passed, before the command is touched.</summary>
    /// <param name="version">The version.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or the last there is, which has no next one.</exception>
    public static void CheckVersion(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentOutOfRangeException.ThrowIfEqual(version, long.MaxValue);
    }

    /// <summary>Adds the condition on a version, for a write that leaves no item or does not write one.</summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="version">The version the item must have.</param>
    /// <exception cref="InvalidOperationException">The table has no version.</exception>
    public static void IfVersion(CommandState state, long version)
    {
        state.Expressions.AppendVersion(VersionAttribute(state), version);
        state.UnsettledVersion = null;
        state.VersionChecked = true;
    }

    /// <summary>Adds the condition on a version and sets the next one, for an update.</summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="version">The version the item must have.</param>
    /// <exception cref="InvalidOperationException">The table has no version.</exception>
    public static void AdvanceVersion(CommandState state, long version)
    {
        IfVersion(state, version);
        var next = version + 1;
        state.Expressions.SetAttribute<long, NumberConverter<long>>(VersionAttribute(state), next);
        state.StoredVersion = next;
    }

    /// <summary>Lets the write go out without looking at the version. A put of an entity then stores the entity as it is, so its item is written again.</summary>
    /// <param name="state">The state.</param>
    /// <exception cref="InvalidOperationException">The write already has <c>IfVersion</c>, whose condition stands in the expressions.</exception>
    public static void IgnoreVersion(CommandState state)
    {
        if (state.VersionChecked)
        {
            throw new InvalidOperationException("This write already has IfVersion, which IgnoreVersion cannot take back. Use one of them.");
        }

        state.UnsettledVersion = null;
        state.VersionIgnored = true;
        if (state.PutEntity is { } entity)
        {
            state.WritePutEntity!(state.RestartItem(), entity);
            state.StoredVersion = state.EntityVersion;
        }

        ForgetEntity(state);
    }

    /// <summary>Ends what a write owes before it goes out. It runs at the terminal of a command and at the end of each action of a transaction.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The state, for a chain.</returns>
    /// <exception cref="InvalidOperationException">The write does not say how it treats a version, it cannot leave a key consistent, or it writes one key of a sparse index without the other.</exception>
    public static CommandState Settle(CommandState state)
    {
        state.VersionChecked = false;
        if (state.UnsettledVersion is { } table)
        {
            if (state.EntityVersion is not { } held)
            {
                throw new InvalidOperationException(
                    $"{table.Writes!.EntityName} has a [Version] attribute, and this write does not say how it treats it. Pass the version last read with IfVersion(n), or write without the check with IgnoreVersion(); a batch cannot carry a condition, so it only has IgnoreVersion().");
            }

            state.Expressions.AppendVersion(table.Writes!.VersionAttributeName!, held);
            state.UnsettledVersion = null;
            ForgetEntity(state);
        }

        if (state.HasKeyInputs)
        {
            state.KeyInputs.Apply(state, state.Table.Writes!);
        }

        // With if_not_exists, as the discriminator, because a key attribute can hold what another type put there, and an update at that key must not rewrite it.
        if (state.OwesLiteralKeys)
        {
            state.OwesLiteralKeys = false;
            foreach (var key in state.Table.Writes!.LiteralKeys)
            {
                state.Expressions.SetMissingAttribute<string, StringConverter>(key.AttributeName, key.Value);
            }
        }

        if (state.OwesDiscriminator)
        {
            state.OwesDiscriminator = false;
            state.Expressions.SetMissingAttribute<string, StringConverter>(state.Table.DiscriminatorAttributeName!, state.Table.Discriminator!);
        }

        return state;
    }

    /// <summary>
    /// Sets an attribute by its handle. A null value is a removal, because a converter takes no null, and because a <c>NULL</c> value would be rejected on an index key. That reads the same
    /// way a generated entity does: it writes no attribute for a null property.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="path">The path of the handle.</param>
    /// <param name="value">The value.</param>
    public static void Set<TValue, TConverter>(CommandState state, string path, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        if (value is null)
        {
            Remove(state, path);
            return;
        }

        if (state.Table.Writes?.IsKeyInput(path) == true)
        {
            state.KeyInputs.Add(path, KeyInputChange.Set, value);
        }

        state.Expressions.Set<TValue, TConverter>(path, value);
    }

    /// <summary>Removes an attribute by its handle.</summary>
    /// <param name="state">The state.</param>
    /// <param name="path">The path of the handle.</param>
    public static void Remove(CommandState state, string path)
    {
        Note(state, path, KeyInputChange.Removed);
        state.Expressions.Remove(path);
    }

    /// <summary>Notes a change of an attribute by its handle whose value only DynamoDB will know.</summary>
    /// <param name="state">The state.</param>
    /// <param name="path">The path of the handle.</param>
    public static void NoteUnknownValue(CommandState state, string path) => Note(state, path, KeyInputChange.Unknown);

    private static void Note(CommandState state, string path, KeyInputChange change)
    {
        if (state.Table.Writes?.IsKeyInput(path) == true)
        {
            state.KeyInputs.Add(path, change);
        }
    }

    // In a transaction the next action starts on the same state, and what this action knew about its entity says nothing about that one.
    private static void ForgetEntity(CommandState state)
    {
        state.EntityVersion = null;
        state.PutEntity = null;
        state.WritePutEntity = null;
    }

    private static string VersionAttribute(CommandState state) => state.Table.Writes?.VersionAttributeName
        ?? throw new InvalidOperationException($"This view of the table {state.Table.Name} has no [Version] attribute, so there is no version to check.");

    /// <summary>The plain write of a type as a delegate, made once per type: a step of a transaction does not know the type of its entity, and a put allocates nothing for this.</summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    private static class PlainWrite<T>
        where T : IDynamoDbDocument<T>
    {
        public static readonly Action<DynamoDbWriter, object> Write = static (writer, entity) => T.WriteAttributes(writer, (T)entity);
    }
}
