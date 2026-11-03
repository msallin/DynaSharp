using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What the write rules keep on a command while it is built, and what ends it. <see cref="WriteRules"/> marks a rule that a clause cannot decide alone, because the clause that fulfils it may still
/// follow, and <see cref="Settle"/> turns the marks into expressions or a throw at the terminal, and at the end of each action of a transaction.
/// </summary>
/// <remarks>
/// This lives in one field of <see cref="CommandState"/> and is reached by reference through <see cref="CommandState.Write"/>, so a rule changes that field: a copy in a variable would change nothing, and nothing makes
/// one. Its default value is a write that marked nothing, which is what <see cref="CommandState.Release"/> puts back, so a new member needs no line there. <c>CommandStateResetTests</c> holds every member to that.
/// </remarks>
internal struct WriteState
{
    /// <summary>Gets or sets the table of a write that has not said yet how it treats the version of its entity, see <see cref="WriteRules"/>.</summary>
    public TableModel? UnsettledVersion { get; set; }

    /// <summary>Gets or sets the version an entity holds, which settles <see cref="UnsettledVersion"/> unless the write says otherwise, see <see cref="WriteRules"/>.</summary>
    public long? EntityVersion { get; set; }

    /// <summary>
    /// Gets or sets the generated key of an update, for an entity with an input that the text of its key does not give back. Every other update reads its key from
    /// <see cref="CommandState.Body"/> when it needs it, see <see cref="CommandState.ReadKey"/>, so this stays empty and costs nothing.
    /// </summary>
    public HeldKey? HeldKey { get; set; }

    /// <summary>Gets or sets a value indicating whether an update stores the inputs of the table key when it settles, see <see cref="WriteRules"/>.</summary>
    public bool StoresKeyInputs { get; set; }

    /// <summary>Gets or sets a value indicating whether an update has yet to write the discriminator of its entity, see <see cref="WriteRules"/>.</summary>
    public bool OwesDiscriminator { get; set; }

    /// <summary>Gets or sets a value indicating whether an update has yet to write the indexes of its entity that have only literals, see <see cref="WriteRules"/>.</summary>
    public bool OwesLiteralKeys { get; set; }

    /// <summary>Gets or sets the entity of a put whose item holds the next version, while <c>IgnoreVersion</c> can still replace that item, see <see cref="WriteRules"/>.</summary>
    public object? PutEntity { get; set; }

    /// <summary>Gets or sets what writes <see cref="PutEntity"/> as it stands.</summary>
    public Action<DynamoDbWriter, object>? WritePutEntity { get; set; }

    /// <summary>Gets or sets a value indicating whether a batch writes without looking at versions.</summary>
    public bool VersionIgnored { get; set; }

    /// <summary>Gets or sets a value indicating whether the write in progress already says which version it expects, which cannot be taken back.</summary>
    public bool VersionChecked { get; set; }

    /// <summary>Gets or sets a value indicating whether the write in progress asks that no item has its key, by <c>IfNotExists</c> or version 0, so that the condition stands once. A version above 0 contradicts it, see <see cref="WriteRules"/>.</summary>
    public bool ItemMustNotExist { get; set; }

    /// <summary>Gets or sets a value indicating whether the write in progress has <c>IfVersion(0)</c> on an entity that allows an item without a version. The condition waits, because <c>IfNotExists</c> takes its place, see <see cref="WriteRules"/>.</summary>
    public bool VersionMustReadAsZero { get; set; }

    /// <summary>Gets or sets a value indicating whether the write in progress asks that an item has its key, which version 0 contradicts unless the entity allows an item without a version, see <see cref="WriteRules"/>.</summary>
    public bool ItemMustExist { get; set; }

    /// <summary>Gets or sets the version the write in progress expects by <c>IfVersion</c>, or 0 for none, which is no contradiction of <see cref="ItemMustNotExist"/> either.</summary>
    public long ExpectedVersion { get; set; }

    /// <summary>Gets or sets the version the write stores, for its result.</summary>
    public long? StoredVersion { get; set; }

    /// <summary>
    /// Gets or sets the parts of the condition the write sends, for the message of a <see cref="ConditionalCheckFailedException"/>, see <see cref="FailedCondition.Explain"/>. <see cref="Settle"/> leaves it, because the
    /// response comes after it, so it lasts until the state goes back. A transaction sets it as well and does not read it.
    /// </summary>
    public ConditionParts Conditions { get; set; }

    /// <summary>Lets the write go out without looking at the version. A put of an entity then stores the entity as it is, so its item is written again.</summary>
    /// <param name="state">The state this is the write of.</param>
    /// <exception cref="InvalidOperationException">The write already has <c>IfVersion</c>, whose condition stands in the expressions.</exception>
    public void IgnoreVersion(CommandState state)
    {
        if (VersionChecked)
        {
            throw new InvalidOperationException("This write already has IfVersion, which IgnoreVersion cannot take back. Use one of them.");
        }

        UnsettledVersion = null;
        VersionIgnored = true;
        if (PutEntity is { } entity)
        {
            WritePutEntity!(state.RestartItem(), entity);
            StoredVersion = EntityVersion;
        }

        ForgetEntity();
    }

    /// <summary>Ends what a write owes before it goes out. It runs at the terminal of a command and at the end of each action of a transaction.</summary>
    /// <param name="state">The state this is the write of.</param>
    /// <exception cref="InvalidOperationException">The write does not say how it treats a version, it asks for a new item with a version above 0 or, on an entity that does not allow an item without a version, for an item that exists at version 0, it cannot leave a key consistent, or it writes one key of a sparse index without the other.</exception>
    public void Settle(CommandState state)
    {
        var versionChecked = VersionChecked;
        VersionChecked = false;
        var mustNotExist = ItemMustNotExist;
        ItemMustNotExist = false;
        var readsAsZero = VersionMustReadAsZero;
        VersionMustReadAsZero = false;
        var mustExist = ItemMustExist;
        ItemMustExist = false;
        var expected = ExpectedVersion;
        ExpectedVersion = 0;
        if (mustNotExist && expected > 0)
        {
            throw Contradiction(state.Table);
        }

        if (readsAsZero && expected > 0)
        {
            throw VersionAndNoVersion(state.Table);
        }

        if (mustExist && versionChecked && expected == 0 && !readsAsZero)
        {
            throw ExistsAtVersionZero(state.Table);
        }

        if (readsAsZero && !mustNotExist)
        {
            state.Expressions.AppendVersionZero(state.Table.Writes!.VersionAttributeName!);
            Conditions |= ConditionParts.Version;
        }

        if (UnsettledVersion is { } table)
        {
            if (EntityVersion is not { } held)
            {
                throw new InvalidOperationException(
                    $"{table.Writes!.EntityName} has a [Version] attribute, and this write does not say how it treats it. Pass the version last read with IfVersion(n), or write without the check with IgnoreVersion(); a batch cannot carry a condition, so it only has IgnoreVersion().");
            }

            if (mustNotExist && held > 0)
            {
                throw Contradiction(table);
            }

            var allowsMissing = table.Writes!.AllowsMissingVersion;
            if (mustExist && held == 0 && !allowsMissing)
            {
                throw ExistsAtVersionZero(table);
            }

            if (held != 0)
            {
                state.Expressions.AppendVersion(table.Writes.VersionAttributeName!, held);
                Conditions |= ConditionParts.Version;
            }
            else if (!mustNotExist && allowsMissing)
            {
                state.Expressions.AppendVersionZero(table.Writes.VersionAttributeName!);
                Conditions |= ConditionParts.Version;
            }
            else if (!mustNotExist)
            {
                state.Expressions.AppendItemExists(table.PartitionKeyName, exists: false);
                Conditions |= ConditionParts.NewVersion;
            }

            UnsettledVersion = null;
            ForgetEntity();
        }

        if (state.HasKeyInputs)
        {
            state.KeyInputs.Apply(state, state.Table.Writes!);
        }

        if (StoresKeyInputs)
        {
            StoresKeyInputs = false;
            state.KeyInputs.StoreTableInputs(state, state.Table.Writes!);
        }

        // With if_not_exists, as the discriminator, because a key attribute can hold what another type put there, and an update at that key must not rewrite it.
        if (OwesLiteralKeys)
        {
            OwesLiteralKeys = false;
            foreach (var key in state.Table.Writes!.LiteralKeys)
            {
                state.Expressions.SetMissingAttribute<string, StringConverter>(key.AttributeName, key.Value);
            }
        }

        if (OwesDiscriminator)
        {
            OwesDiscriminator = false;
            state.Expressions.SetMissingAttribute<string, StringConverter>(state.Table.DiscriminatorAttributeName!, state.Table.Discriminator!);
        }
    }

    private static InvalidOperationException Contradiction(TableModel table) => new(
        $"This write of {table.Writes!.EntityName} holds a version above 0 and asks that no item has its key, which can never both hold: an item that does not exist has no version. For a first write use version 0, to replace an item leave out IfNotExists, and to write the entity as it is call IgnoreVersion().");

    private static InvalidOperationException VersionAndNoVersion(TableModel table) => new(
        $"This write of {table.Writes!.EntityName} asks for version 0, an item without a version, and for a version above 0, which can never both hold. Keep one IfVersion.");

    private static InvalidOperationException ExistsAtVersionZero(TableModel table) => new(
        $"This write of {table.Writes!.EntityName} holds version 0 and asks that an item has its key, which can never both hold: version 0 means that no item has the key. For a first write leave out IfExists, for an item that exists use the version last read, and to write without looking at the version call IgnoreVersion().");

    // In a transaction the next action starts on the same state, and what this action knew about its entity says nothing about that one.
    private void ForgetEntity()
    {
        EntityVersion = null;
        PutEntity = null;
        WritePutEntity = null;
    }
}
