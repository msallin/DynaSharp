using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What the commands that write have in common beyond their bytes: the rules of <see cref="VersionAttribute"/> and of key templates, and the discriminator an update owes. A rule that a
/// clause cannot decide waits for <see cref="WriteState.Settle"/>, because the clause that fulfils it may still follow. The rules of versions and keys start from <see cref="TableModel.Writes"/>, which
/// is <see langword="null"/> for a table without such an entity, so a command on that table costs a null check per clause.
/// </summary>
internal static class WriteRules
{
    /// <summary>
    /// Writes a whole item, and for an entity with a version the next version. The condition on it has to wait for <see cref="WriteState.Settle"/>, because <see cref="WriteState.IgnoreVersion"/> can still take it
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
        state.Write.UnsettledVersion = state.Table;
        state.Write.EntityVersion = held;
        state.Write.StoredVersion = held + 1;
        state.Write.PutEntity = item;
        state.Write.WritePutEntity = PlainWrite<T>.Write;
    }

    /// <summary>Marks a write whose key does not carry a version, so it must say how it treats one before it goes out.</summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectVersion(CommandState state)
    {
        state.Write.UnsettledVersion = state.Table.Writes?.VersionAttributeName is null ? null : state.Table;
        return state;
    }

    /// <summary>
    /// Marks an update. It owes what any write by key owes a version, and because <c>UpdateItem</c> can create the item, the discriminator of the entity, without which a typed read would
    /// not see it, and the literals of an index that no property can bring an item into, without which the item would not be in an index that holds every item. <see cref="WriteState.Settle"/> writes those, because only then
    /// is it known whether the update has an action of its own on the attribute.
    /// </summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectUpdate(CommandState state)
    {
        state.Write.HeldKey = null;
        state.Write.OwesDiscriminator = state.Table.UpdateWritesDiscriminator;
        state.Write.OwesLiteralKeys = state.Table.Writes is { LiteralKeys.Length: > 0 };
        return ExpectVersion(state);
    }

    /// <summary>
    /// Marks an update by a generated key, see <see cref="ExpectUpdate(CommandState)"/>. The text of a key gives its inputs back whenever <see cref="KeyInputChanges"/> asks, so the key is
    /// gone after this, unless an entity can want more than the text holds. Then the state keeps it, which is no box, see <see cref="WriteState.HeldKey"/>, because most of those updates never ask.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="key">The key.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectUpdate<TKey>(CommandState state, TKey key)
        where TKey : IKey
    {
        ExpectUpdate(state);
        if (state.Table.Writes is { HoldsKey: true })
        {
            state.HoldKey(key);
        }

        return state;
    }

    /// <summary>
    /// Marks a write that takes its version from an entity. The condition has to wait for <see cref="WriteState.Settle"/>, because <c>IgnoreVersion</c> can still take it back, or <c>IfVersion</c> replace it, and
    /// what stands in the expressions cannot be taken out. The version is read here, while there is an entity.
    /// </summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="entity">The entity.</param>
    /// <returns>The same state, for a chain.</returns>
    public static CommandState ExpectVersionOf<T>(CommandState state, T entity)
        where T : IDynamoDbEntity<T>
    {
        if (ExpectVersion(state).Write.UnsettledVersion is not null)
        {
            state.Write.EntityVersion = T.ReadVersion(entity);
        }

        return state;
    }

    /// <summary>Marks a write of a batch, which cannot carry the condition a version needs.</summary>
    /// <param name="state">The state of the batch.</param>
    /// <param name="table">The table of the write.</param>
    public static void NoteBatchWrite(CommandState state, TableModel table)
    {
        if (table.Writes?.VersionAttributeName is not null && !state.Write.VersionIgnored)
        {
            state.Write.UnsettledVersion = table;
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

    /// <summary>Asks an update to store the inputs of the table key, which waits for <see cref="WriteState.Settle"/>, because it comes after every action of the update. An entity with nothing to store asks for nothing.</summary>
    /// <param name="state">The state, whose table is the one of the update.</param>
    /// <exception cref="InvalidOperationException">The table has no entity.</exception>
    public static void StoreKeyInputs(CommandState state)
    {
        if (state.Table.Entity is null)
        {
            throw new InvalidOperationException($"This view of the table {state.Table.Name} has no entity, so there are no key inputs to store.");
        }

        state.Write.StoresKeyInputs = state.Table.Writes is { StoredTableInputs.Length: > 0 };
    }

    /// <summary>
    /// Adds the condition on a version, for a write that leaves no item or does not write one. Version 0 stands for an item that does not exist, so it is the condition of <see cref="IfNotExists"/>, on the key: looking at the
    /// version attribute instead would let a write replace an item that has none. An entity that allows such items wants exactly that, and takes an item that holds 0 as well, because both read as 0. Its condition waits for
    /// <see cref="WriteState.Settle"/>, because <c>IfNotExists</c> can still replace it, and what stands in the expressions cannot be taken out.
    /// </summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    /// <param name="version">The version the item must have.</param>
    /// <exception cref="InvalidOperationException">The table has no version.</exception>
    public static void IfVersion(CommandState state, long version)
    {
        var attributeName = VersionAttribute(state);
        if (version != 0)
        {
            state.Expressions.AppendVersion(attributeName, version);
        }
        else if (state.Table.Writes!.AllowsMissingVersion)
        {
            state.Write.VersionMustReadAsZero = true;
        }
        else
        {
            IfNotExists(state);
        }

        state.Write.UnsettledVersion = null;
        state.Write.VersionChecked = true;
        state.Write.ExpectedVersion = version;
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
        state.Write.StoredVersion = next;
    }

    /// <summary>
    /// Adds the condition that no item has the key, once, whichever of <c>IfNotExists</c> and version 0 comes first. It is noted as well, because an entity may hold a version that says the opposite, and
    /// <see cref="WriteState.IgnoreVersion"/> can still take that back.
    /// </summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    public static void IfNotExists(CommandState state)
    {
        if (!state.Write.ItemMustNotExist)
        {
            state.Expressions.AppendItemExists(state.Table.PartitionKeyName, exists: false);
            state.Write.ItemMustNotExist = true;
        }
    }

    /// <summary>Adds the condition that an item has the key. It is noted as well, because version 0 says the opposite, and that is only known at the end of the chain.</summary>
    /// <param name="state">The state, whose table is the one of the write.</param>
    public static void IfExists(CommandState state)
    {
        state.Expressions.AppendItemExists(state.Table.PartitionKeyName, exists: true);
        state.Write.ItemMustExist = true;
    }

    /// <summary>
    /// Sets an attribute by its handle. A null value is a removal, because a converter takes no null, and because a <c>NULL</c> value would be rejected on an index key. That reads the same
    /// way a generated entity does: it writes no attribute for a null property. A value its converter counts as null goes the same way.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="path">The path of the handle.</param>
    /// <param name="value">The value.</param>
    public static void Set<TValue, TConverter>(CommandState state, string path, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        if (value is null || TConverter.IsOmitted(value))
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

    /// <summary>Sets an attribute by its handle if there is a value, and otherwise adds nothing. A value its converter counts as null is none.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="path">The path of the handle.</param>
    /// <param name="value">The value.</param>
    public static void SetIfNotNull<TValue, TConverter>(CommandState state, string path, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        if (value is not null && !TConverter.IsOmitted(value))
        {
            Set<TValue, TConverter>(state, path, value);
        }
    }

    /// <summary>Rejects what a converter counts as null where a value must be given, as a converter rejects null itself, before the command is touched.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="path">The path of the handle.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> counts as null.</exception>
    public static void CheckValue<TValue, TConverter>(string path, TValue value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        if (TConverter.IsOmitted(value))
        {
            throw new ArgumentException($"The converter of the attribute {path} counts this value as null, and SetIfNotExists takes a value.", nameof(value));
        }
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
