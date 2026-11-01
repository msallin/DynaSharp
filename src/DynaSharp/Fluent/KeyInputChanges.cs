using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// What an update did by handle to properties that feed keys, kept until its terminal, when it is known which inputs of a template came together. A value is
/// held boxed, because the handles of one update have different types and the template wants them all at once. That cost falls on an update of a key input and on
/// nothing else: no other command ever makes this object, and the object stays with its pooled state.
/// </summary>
internal sealed class KeyInputChanges
{
    private readonly List<Change> _changes = [];

    private object?[] _values = new object?[4];

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
    }

    /// <summary>
    /// Leaves the keys of the changed inputs consistent, in the expressions of the update: writes the derived attribute of a template that has all its inputs, and
    /// removes the attributes of a sparse index that lost one. An update that writes one key of a sparse index has to produce the other one as well, and a key of literals, of any index, comes and
    /// goes with the key beside it. An index with nothing but literals has no such key, and <see cref="WriteRules"/> writes it. It runs once, at the terminal of the update.
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
                else if (Touches(template) || FollowsSibling(template, writes))
                {
                    state.Expressions.SetAttribute<string, Serialization.StringConverter>(template.AttributeName, Derive(template, format, writes));
                }
            }
        }

        // After the derived attributes: a key that only some of its inputs reached has thrown by now, with what is missing.
        foreach (var index in writes.Indexes)
        {
            if (index is { Sparse: true, SortKeyName: { } sortKey } && !HasKeyWith(index, KeyInputChange.Removed, writes))
            {
                RequireSibling(index, index.PartitionKeyName, sortKey, writes);
                RequireSibling(index, sortKey, index.PartitionKeyName, writes);
            }
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

    // A key of literals has no input of its own, so an update writes it when it sets the key beside it.
    private bool FollowsSibling(DynamoDbKeyTemplate template, EntityWrites writes)
    {
        if (template.Inputs.Count == 0)
        {
            foreach (var index in writes.Indexes)
            {
                if (IsKeyOf(index, template) && HasKeyWith(index, KeyInputChange.Set, writes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RequireSibling(DynamoDbIndexInfo index, string attributeName, string siblingName, EntityWrites writes)
    {
        if (TemplateOf(attributeName, writes) is { } template && Has(template, KeyInputChange.Set) && !Produces(siblingName, writes))
        {
            throw new InvalidOperationException(
                $"This update of {writes.EntityName} sets {ListOf(template, static kind => kind == KeyInputChange.Set)}, of the key template {template.Text} of the sparse index {index.IndexName}, and does not produce {siblingName}, the other key of that index, so the item would silently stay out of it. Set what that key is made of in the same update. A key an update cannot write, such as one made of a key of the table, comes with a Put, which writes every key.");
        }
    }

    // What every item has counts as produced, and so does a key of literals, which comes with the key beside it. An input of the table key is one that no update writes.
    private bool Produces(string attributeName, EntityWrites writes)
    {
        if (writes.IsTableKey(attributeName))
        {
            return true;
        }

        if (TemplateOf(attributeName, writes) is not { } template)
        {
            return false;
        }

        foreach (var input in template.Inputs)
        {
            if (Find(input).Kind != KeyInputChange.Set || writes.IsTableKey(input.AttributeName))
            {
                return false;
            }
        }

        return true;
    }

    private string Derive(DynamoDbKeyTemplate template, Func<object?[], string> format, EntityWrites writes)
    {
        var inputs = template.Inputs;
        if (inputs.Count > _values.Length)
        {
            _values = new object?[inputs.Count];
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            if (Find(inputs[i]).Kind != KeyInputChange.Set)
            {
                throw new InvalidOperationException(
                    $"This update of {writes.EntityName} sets {ListOf(template, static kind => kind == KeyInputChange.Set)}, and not {ListOf(template, static kind => kind == KeyInputChange.None)}, of the key template {template.Text}, so the attribute {template.AttributeName} cannot be written and the index would go stale. Set all of them, or use a raw path, which is not checked.");
            }

            _values[i] = Find(inputs[i]).Value;
        }

        return format(_values);
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

    private Change Find(DynamoDbKeyInput input)
    {
        foreach (var change in _changes)
        {
            if (change.AttributeName == input.AttributeName)
            {
                return change;
            }
        }

        return default;
    }

    private readonly record struct Change(string AttributeName, KeyInputChange Kind, object? Value);
}
