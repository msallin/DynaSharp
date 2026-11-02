using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What an update did by handle to properties that feed keys, kept until its terminal, when it is known which inputs of a template came together. A value is
/// held boxed, because the handles of one update have different types and the template wants them all at once. That cost falls on an update of a key input and on
/// nothing else: no other command ever makes this object, and the object stays with its pooled state.
/// </summary>
/// <remarks>
/// An input of the table key is not a change: the update was given that key, so its value is known, and an index key that repeats it can follow without a read. Nothing is kept for that while
/// the update is built. Here, and only for an update that writes such an index key or asks to store the inputs, the key is read back from the request, or asked of a generated key the state holds when the
/// text cannot give the value back, see <see cref="DynamoDbKeyTemplate.LosesValues"/>.
/// </remarks>
internal sealed class KeyInputChanges
{
    private const string PutOrRawPath = "Use a Put of the changed entity, or a raw path, which is not checked.";

    private readonly List<Change> _changes = [];

    private object?[] _values = new object?[4];

    // The key as the request holds it, read when the first input of it is asked for.
    private Item? _key;

    /// <summary>Gets the number of changes.</summary>
    public int Count => _changes.Count;

    /// <summary>Notes a change of an input.</summary>
    /// <param name="attributeName">The attribute of the input.</param>
    /// <param name="kind">What the update does to it.</param>
    /// <param name="value">The value it sets, for <see cref="KeyInputChange.Set"/>.</param>
    public void Add(string attributeName, KeyInputChange kind, object? value = null) => _changes.Add(new Change(attributeName, kind, value));

    /// <summary>Empties the list for the next update.</summary>
    public void Clear()
    {
        _changes.Clear();
        Array.Clear(_values);
        _key = null;
    }

    /// <summary>
    /// Leaves the keys of the changed inputs consistent, in the expressions of the update: writes the derived attribute of a template that has all its inputs, and
    /// removes the attributes of a sparse index that lost one. An update that writes one key of a sparse index has to produce the other one as well, and a key of literals, of any index, comes and
    /// goes with the key beside it, as does one made only of inputs of the table key. An index with nothing but literals has no such key, and <see cref="WriteRules"/> writes it. It runs once, at the
    /// terminal of the update.
    /// </summary>
    /// <param name="state">The state of the update.</param>
    /// <param name="writes">What the entity says about its keys.</param>
    /// <exception cref="InvalidOperationException">The update cannot leave a key consistent. See the messages.</exception>
    public void Apply(CommandState state, EntityWrites writes)
    {
        foreach (var template in writes.Templates)
        {
            Check(template, writes);
        }

        foreach (var template in writes.Templates)
        {
            if (template.Format is { } format)
            {
                if (LeavesEveryIndex(template, writes))
                {
                    state.Expressions.RemoveAttribute(template.AttributeName);
                }
                else if (Touches(template) || FollowsSibling(template, state, writes))
                {
                    state.Expressions.SetAttribute<string, StringConverter>(template.AttributeName, Derive(template, format, state, writes));
                }
            }
        }

        // After the derived attributes: a key that only some of its inputs reached has thrown by now, with what is missing.
        foreach (var index in writes.Indexes)
        {
            if (index is { Sparse: true, SortKeyName: { } sortKey } && !HasKeyWith(index, KeyInputChange.Removed, writes))
            {
                RequireSibling(index, index.PartitionKeyName, sortKey, state, writes);
                RequireSibling(index, sortKey, index.PartitionKeyName, state, writes);
            }
        }

        Clear();
    }

    /// <summary>
    /// Writes the attribute of every input of the table key, with <c>if_not_exists</c>, so that the update can create what a <c>Put</c> writes and an item that exists keeps what it has. An action of the update on the
    /// attribute takes precedence. It runs once, at the terminal of an update that asked, after <see cref="Apply"/>.
    /// </summary>
    /// <param name="state">The state of the update.</param>
    /// <param name="writes">What the entity says about its keys.</param>
    /// <exception cref="InvalidOperationException">The key does not fit its template, or a key given as strings does not hold all of a value. See the messages.</exception>
    public void StoreTableInputs(CommandState state, EntityWrites writes)
    {
        foreach (var input in writes.StoredTableInputs)
        {
            state.Expressions.SetMissingAttribute<AttributeValue, AttributeValueConverter>(input.AttributeName!, input.ToAttributeValue!(ValueToStore(input, state, writes)));
        }

        Clear();
    }

    private static bool IsKeyOf(DynamoDbIndexInfo index, DynamoDbKeyTemplate template) =>
        index.PartitionKeyName == template.AttributeName || index.SortKeyName == template.AttributeName;

    private static DynamoDbKeyTemplate? TemplateOf(string attributeName, EntityWrites writes)
    {
        foreach (var template in writes.Templates)
        {
            if (template.AttributeName == attributeName)
            {
                return template;
            }
        }

        return null;
    }

    private static int PositionOf(DynamoDbKeyTemplate template, DynamoDbKeyInput input)
    {
        var inputs = template.Inputs;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].PropertyName == input.PropertyName)
            {
                return i;
            }
        }

        return -1;
    }

    // The next error must not forbid what a message asks for: no handle reaches a property only a key holds, an input of the table key cannot change, and a value set on a computed
    // property would be the getter's to contradict on the next Put.
    private static bool CanSet(IEnumerable<DynamoDbKeyInput> inputs, EntityWrites writes) =>
        inputs.All(input => input is { AttributeName: not null, ComputedFromAttributes: null } && !writes.IsTableKeyInput(input));

    private void Check(DynamoDbKeyTemplate template, EntityWrites writes)
    {
        if (!Touches(template))
        {
            return;
        }

        if (template.IsTableKey)
        {
            throw new InvalidOperationException(
                $"This update of {writes.EntityName} changes {ListOf(template, static kind => kind != KeyInputChange.None)}, which the key template {template.Text} is made of. DynamoDB cannot update a key of a table: delete the item and put a new one.");
        }

        foreach (var input in template.Inputs)
        {
            if (ChangedDependencyOf(input) is { } dependency)
            {
                throw new InvalidOperationException(
                    $"This update of {writes.EntityName} changes {dependency}, which {input.PropertyName} of the key template {template.Text} is computed from, so the key cannot follow. {PutOrRawPath}");
            }

            // The key could follow here, and that is what is wrong with it: it would follow the value of the update, and the next Put writes what the getter says.
            if (input.ComputedFromAttributes is not null && Find(input).Kind != KeyInputChange.None)
            {
                throw new InvalidOperationException(
                    $"This update of {writes.EntityName} changes {input.PropertyName}, which its getter computes for the key template {template.Text}, so the key would follow the update and not the getter. {PutOrRawPath}");
            }
        }

        if (Has(template, KeyInputChange.Unknown))
        {
            throw new InvalidOperationException(
                $"This update of {writes.EntityName} changes {ListOf(template, static kind => kind == KeyInputChange.Unknown)} by Add or SetIfNotExists, so the value is not known here and the key template {template.Text} cannot follow. Use Set, or a raw path, which is not checked.");
        }

        if (Has(template, KeyInputChange.Removed))
        {
            foreach (var index in writes.Indexes)
            {
                if (IsKeyOf(index, template) && !index.Sparse)
                {
                    throw new InvalidOperationException(
                        $"This update of {writes.EntityName} removes {ListOf(template, static kind => kind == KeyInputChange.Removed)}, which the key template {template.Text} of the index {index.IndexName} needs, and that index is not sparse. Use a raw path, which is not checked, if the item should stay in it with a key that no longer fits.");
                }
            }
        }
    }

    // An attribute several indexes share stays while one of them keeps the item.
    private bool LeavesEveryIndex(DynamoDbKeyTemplate template, EntityWrites writes)
    {
        var leaves = false;
        foreach (var index in writes.Indexes)
        {
            if (IsKeyOf(index, template))
            {
                if (!HasKeyWith(index, KeyInputChange.Removed, writes))
                {
                    return false;
                }

                leaves = true;
            }
        }

        return leaves;
    }

    private bool HasKeyWith(DynamoDbIndexInfo index, KeyInputChange kind, EntityWrites writes)
    {
        foreach (var template in writes.Templates)
        {
            if (IsKeyOf(index, template) && Has(template, kind))
            {
                return true;
            }
        }

        return false;
    }

    // A key of literals and one made only of inputs of the table key have nothing an update could set, so it writes them when it sets the key beside them. A value that cannot
    // be known leaves such a key alone: on an index that is not sparse a Put has written it already, and a sparse one answers in RequireSibling.
    private bool FollowsSibling(DynamoDbKeyTemplate template, CommandState state, EntityWrites writes)
    {
        foreach (var input in template.Inputs)
        {
            if (!writes.IsTableKeyInput(input))
            {
                return false;
            }
        }

        foreach (var index in writes.Indexes)
        {
            if (IsKeyOf(index, template) && HasKeyWith(index, KeyInputChange.Set, writes))
            {
                return !template.Inputs.Any(input => ValueOf(input, state, writes) is null);
            }
        }

        return false;
    }

    private void RequireSibling(DynamoDbIndexInfo index, string attributeName, string siblingName, CommandState state, EntityWrites writes)
    {
        if (TemplateOf(attributeName, writes) is { } template && Has(template, KeyInputChange.Set) && !Produces(siblingName, state, writes))
        {
            var missing = TemplateOf(siblingName, writes) is { } sibling ? Missing(sibling, state, writes) : null;
            var remedy = missing is not null && CanSet(missing, writes) ? "Set what that key is made of in the same update." : PutOrRawPath;
            throw new InvalidOperationException(
                $"This update of {writes.EntityName} sets {ListOf(template, static kind => kind == KeyInputChange.Set)}, of the key template {template.Text} of the sparse index {index.IndexName}, and does not produce {siblingName}, the other key of that index, so the item would silently stay out of it. {remedy}");
        }
    }

    // What every item has counts as produced, and so does a key whose inputs are all known, one of literals among them, which comes with the key beside it.
    private bool Produces(string attributeName, CommandState state, EntityWrites writes)
    {
        if (writes.IsTableKey(attributeName))
        {
            return true;
        }

        return TemplateOf(attributeName, writes) is { } template && Missing(template, state, writes).Count == 0;
    }

    private string Derive(DynamoDbKeyTemplate template, Func<object?[], string> format, CommandState state, EntityWrites writes)
    {
        var inputs = template.Inputs;
        if (inputs.Count > _values.Length)
        {
            _values = new object?[inputs.Count];
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            _values[i] = ValueOf(inputs[i], state, writes) ?? throw Stale(template, state, writes);
        }

        return format(_values);
    }

    private InvalidOperationException Stale(DynamoDbKeyTemplate template, CommandState state, EntityWrites writes)
    {
        var missing = Missing(template, state, writes);
        var remedy = CanSet(missing, writes) ? "Set all of them, or use a raw path, which is not checked." : PutOrRawPath;
        return new InvalidOperationException(
            $"This update of {writes.EntityName} sets {ListOf(template, static kind => kind == KeyInputChange.Set)}, and not {string.Join(", ", missing.Select(static input => input.PropertyName))}, of the key template {template.Text}, so the attribute {template.AttributeName} cannot be written and the index would go stale. {remedy}");
    }

    private List<DynamoDbKeyInput> Missing(DynamoDbKeyTemplate template, CommandState state, EntityWrites writes) =>
        [.. template.Inputs.Where(input => ValueOf(input, state, writes) is null)];

    // Null is no value of an input, because a key takes none and a set to null is a removal.
    private object? ValueOf(DynamoDbKeyInput input, CommandState state, EntityWrites writes)
    {
        var change = Find(input);
        if (change.Kind == KeyInputChange.Set)
        {
            return change.Value;
        }

        return change.Kind == KeyInputChange.None && writes.IsTableKeyInput(input) ? ValueInKey(input, state, writes) : null;
    }

    private object? ValueInKey(DynamoDbKeyInput input, CommandState state, EntityWrites writes)
    {
        if (state.HeldKey?.ValueOf(input.PropertyName) is { } held)
        {
            return held;
        }

        _key ??= state.ReadKey();
        foreach (var template in writes.Templates)
        {
            if (template.Parse is { } parse
                && PositionOf(template, input) is >= 0 and var position
                && _key.TryGetValue(template.AttributeName, out var key)
                && parse(key)?[position] is { } value)
            {
                return value;
            }
        }

        return null;
    }

    // A value that a template cuts off is good enough for an index key of the same format, and for an attribute it would be a lie.
    private object ValueToStore(DynamoDbKeyInput input, CommandState state, EntityWrites writes)
    {
        if (state.HeldKey?.ValueOf(input.PropertyName) is { } held)
        {
            return held;
        }

        _key ??= state.ReadKey();
        DynamoDbKeyTemplate? cutBy = null;
        foreach (var template in writes.Templates)
        {
            if (template.Parse is { } parse && PositionOf(template, input) is >= 0 and var position)
            {
                var values = (_key.TryGetValue(template.AttributeName, out var key) ? parse(key) : null)
                    ?? throw new InvalidOperationException($"The key does not fit the key template {template.Text} of {writes.EntityName}, so StoreKeyInputs cannot read its inputs.");

                if (!template.Inputs[position].IsCutOff)
                {
                    return values[position]!;
                }

                cutBy = template;
            }
        }

        throw new InvalidOperationException($"StoreKeyInputs cannot restore {input.PropertyName} from the text of the key template {cutBy!.Text}, which cuts it off. Pass a generated key (Keys.Of), or put the entity.");
    }

    private bool Touches(DynamoDbKeyTemplate template)
    {
        foreach (var input in template.Inputs)
        {
            if (Find(input).Kind != KeyInputChange.None)
            {
                return true;
            }
        }

        return false;
    }

    private bool Has(DynamoDbKeyTemplate template, KeyInputChange kind)
    {
        foreach (var input in template.Inputs)
        {
            if (Find(input).Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private string ListOf(DynamoDbKeyTemplate template, Func<KeyInputChange, bool> filter) =>
        string.Join(", ", template.Inputs.Where(input => filter(Find(input).Kind)).Select(static input => input.PropertyName));

    // A computed input is what its getter makes of its dependencies, so a change of one of them is a change of the input to something only the getter could tell.
    private Change Find(DynamoDbKeyInput input)
    {
        if (ChangedDependencyOf(input) is not null)
        {
            return new Change(string.Empty, KeyInputChange.Unknown, null);
        }

        foreach (var change in _changes)
        {
            if (change.AttributeName == input.AttributeName)
            {
                return change;
            }
        }

        return default;
    }

    // The list is an array, and this runs for every input of every template an update of a key input looks at, so it stays away from the enumerator of the interface.
    private string? ChangedDependencyOf(DynamoDbKeyInput input)
    {
        if (input.ComputedFromAttributes is { } dependencies)
        {
            for (var i = 0; i < dependencies.Count; i++)
            {
                foreach (var change in _changes)
                {
                    if (EntityWrites.IsAtOrInside(change.AttributeName, dependencies[i]))
                    {
                        return dependencies[i];
                    }
                }
            }
        }

        return null;
    }

    private readonly record struct Change(string AttributeName, KeyInputChange Kind, object? Value);
}
