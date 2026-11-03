namespace DynaSharp.Testing.Fake;

/// <summary>
/// Applies the actions of an <c>UpdateExpression</c> to an item, with the rules of the Developer Guide, "Update expressions". Every value of <c>SET</c> is read from the item as it was
/// before the update, so <c>REMOVE a SET b = a</c> moves <c>a</c> to <c>b</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every step of a path but the last must exist: <c>SET a.b = :v</c> without <c>a</c> is a <c>ValidationException</c>, so is a step into a value of the wrong kind. A list index
/// past the end appends to the list for <c>SET</c> and <c>ADD</c>, and does nothing for <c>REMOVE</c> and <c>DELETE</c>.</item>
/// <item><c>ADD</c> to a missing path writes the value; to a number it adds, with all 38 digits; to a set of its type it adds the elements. <c>DELETE</c> takes elements out of a set
/// of its type, and an empty set goes. Any other type at the path is a <c>ValidationException</c>.</item>
/// <item>Several <c>REMOVE</c> of one list remove the elements of the indexes the list had before the update.</item>
/// <item>The key attributes cannot be changed. An update that creates the item starts from the key.</item>
/// </list>
/// <c>UPDATED_OLD</c> and <c>UPDATED_NEW</c> are the paths of the actions, projected from the item before and after: <c>SET a.b = :v</c> returns <c>{"a":{"M":{"b":...}}}</c>, not the
/// whole of <c>a</c>. A removed path has nothing after the update, and an appended element is returned at the index it got.
/// </remarks>
internal static class UpdateApplier
{
    private const string IncorrectType = "An operand in the update expression has an incorrect data type";

    /// <summary>Applies the actions.</summary>
    /// <param name="actions">The actions, which the parser checked not to overlap.</param>
    /// <param name="current">The item, or <see langword="null"/> where there is none.</param>
    /// <param name="key">The key of the request.</param>
    /// <returns>The new item, and the updated paths before and after.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an update DynamoDB rejects on this item.</exception>
    public static (Item NewImage, Item UpdatedOld, Item UpdatedNew) Apply(IReadOnlyList<UpdateAction> actions, Item? current, Item key)
    {
        foreach (var action in actions)
        {
            if (key.ContainsKey(action.Path.Root))
            {
                throw FakeError.Validation($"One or more parameter values were invalid: Cannot update attribute {action.Path.Root}. This attribute is part of the key");
            }
        }

        var original = current ?? key;
        var image = new Item(original);
        var updated = new List<DocumentPath>();

        // The values first, all on the item as it was; then the changes, the removals last and from the highest index down, so that each index still means what it meant.
        var changes = actions.Where(static action => action is not UpdateAction.Remove)
            .Select(action => (action.Path, Change: ChangeOf(action, original)))
            .ToList();
        var removals = actions.OfType<UpdateAction.Remove>().Select(static action => action.Path).OrderByDescending(static path => path, PathOrder.Instance);
        foreach (var (path, change) in changes)
        {
            var effective = Modify(image, path, change);
            if (effective.Count == path.Segments.Count)
            {
                updated.Add(new DocumentPath(effective));
            }
        }

        foreach (var path in removals)
        {
            Modify(image, path, static _ => null);
        }

        var before = current is null ? [] : Projection.Apply(current, [.. actions.Select(static action => action.Path)]);
        return (image, before, Projection.Apply(image, updated));
    }

    private static Func<AttributeValue?, AttributeValue?> ChangeOf(UpdateAction action, Item original)
    {
        switch (action)
        {
            case UpdateAction.Set set:
                var value = Evaluate(set.Value, original);
                return _ => value;
            case UpdateAction.Add add:
                return old => Add(old, add.Value);
            default:
                var delete = (UpdateAction.Delete)action;
                return old => Delete(old, delete.Value);
        }
    }

    private static AttributeValue Evaluate(Operand operand, Item item) => operand switch
    {
        Operand.Attribute attribute => attribute.Path.TryResolve(item, out var value)
            ? value
            : throw FakeError.Validation("The provided expression refers to an attribute that does not exist in the item"),
        Operand.Constant constant => constant.Value,
        Operand.IfNotExists ifNotExists => ifNotExists.Path.TryResolve(item, out var existing) ? existing : Evaluate(ifNotExists.Fallback, item),
        Operand.ListAppend append => ListAppend(Evaluate(append.First, item), Evaluate(append.Second, item)),
        _ => Sum((Operand.Sum)operand, item),
    };

    private static AttributeValue ListAppend(AttributeValue first, AttributeValue second) =>
        first.Type == AttributeType.List && second.Type == AttributeType.List
            ? AttributeValue.FromList(first.AsList().Concat(second.AsList()))
            : throw FakeError.Validation(IncorrectType);

    private static AttributeValue Sum(Operand.Sum sum, Item item)
    {
        var left = Evaluate(sum.Left, item);
        var right = Evaluate(sum.Right, item);
        if (left.Type != AttributeType.Number || right.Type != AttributeType.Number)
        {
            throw FakeError.Validation(IncorrectType);
        }

        var x = DecimalNumber.Parse(left.AsNumber());
        var y = DecimalNumber.Parse(right.AsNumber());
        return AttributeValue.FromNumber((sum.Subtract ? x.Subtract(y) : x.Add(y)).ToString());
    }

    private static AttributeValue Add(AttributeValue? old, AttributeValue value)
    {
        if (old is not { } existing)
        {
            return value;
        }

        if (existing.Type != value.Type)
        {
            throw FakeError.Validation(IncorrectType);
        }

        if (value.Type == AttributeType.Number)
        {
            return AttributeValue.FromNumber(DecimalNumber.Parse(existing.AsNumber()).Add(DecimalNumber.Parse(value.AsNumber())).ToString());
        }

        var elements = AttributeSets.Elements(existing).ToList();
        elements.AddRange(AttributeSets.Elements(value).Where(element => !elements.Any(known => AttributeComparer.AreEqual(known, element))));
        return AttributeSets.Create(value.Type, elements);
    }

    private static AttributeValue? Delete(AttributeValue? old, AttributeValue value)
    {
        if (old is not { } existing)
        {
            return null;
        }

        if (existing.Type != value.Type)
        {
            throw FakeError.Validation(IncorrectType);
        }

        var taken = AttributeSets.Elements(value).ToList();
        var left = AttributeSets.Elements(existing).Where(element => !taken.Any(gone => AttributeComparer.AreEqual(gone, element))).ToList();
        return left.Count == 0 ? (AttributeValue?)null : AttributeSets.Create(value.Type, left);
    }

    // Changes the value at the path, which the change gets as it is (null where there is none) and gives back as it shall be (null to remove it). Returns the path the change
    // took, which differs from the given one for an element appended past the end of a list, and is shorter for a change that did nothing there.
    private static List<PathSegment> Modify(Item image, DocumentPath path, Func<AttributeValue?, AttributeValue?> change)
    {
        var effective = new List<PathSegment> { path.Segments[0] };
        var existing = image.TryGetValue(path.Root, out var found) ? found : (AttributeValue?)null;
        var result = path.Segments.Count == 1 ? change(existing) : Modify(existing ?? throw InvalidPath(), path.Segments, 1, change, effective);
        if (result is { } value)
        {
            image[path.Root] = value;
        }
        else
        {
            image.Remove(path.Root);
        }

        return effective;
    }

    private static AttributeValue Modify(AttributeValue container, IReadOnlyList<PathSegment> segments, int position, Func<AttributeValue?, AttributeValue?> change, List<PathSegment> effective)
    {
        var segment = segments[position];
        var last = position == segments.Count - 1;
        if (segment.Name is { } name)
        {
            if (container.Type != AttributeType.Map)
            {
                throw InvalidPath();
            }

            effective.Add(segment);
            var map = new Item(container.AsMap());
            var child = map.TryGetValue(name, out var found) ? found : (AttributeValue?)null;
            var result = last ? change(child) : Modify(child ?? throw InvalidPath(), segments, position + 1, change, effective);
            if (result is { } value)
            {
                map[name] = value;
            }
            else
            {
                map.Remove(name);
            }

            return AttributeValue.FromMap(map);
        }

        if (container.Type != AttributeType.List)
        {
            throw InvalidPath();
        }

        var list = container.AsList().ToList();
        if (segment.Index < list.Count)
        {
            effective.Add(segment);
            var result = last ? change(list[segment.Index]) : Modify(list[segment.Index], segments, position + 1, change, effective);
            if (result is { } value)
            {
                list[segment.Index] = value;
            }
            else
            {
                list.RemoveAt(segment.Index);
            }
        }
        else if (!last)
        {
            throw InvalidPath();
        }
        else if (change(null) is { } appended)
        {
            effective.Add(PathSegment.At(list.Count));
            list.Add(appended);
        }

        return AttributeValue.FromList(list);
    }

    private static FakeError InvalidPath() => FakeError.Validation("The document path provided in the update expression is invalid for update");

    // Orders paths step by step, an index by its number, so that the removals of one list run from its highest index down.
    private sealed class PathOrder : IComparer<DocumentPath>
    {
        public static readonly PathOrder Instance = new();

        public int Compare(DocumentPath? x, DocumentPath? y)
        {
            var common = Math.Min(x!.Segments.Count, y!.Segments.Count);
            for (var i = 0; i < common; i++)
            {
                var one = x.Segments[i];
                var two = y.Segments[i];
                var order = one.IsIndex && two.IsIndex ? one.Index.CompareTo(two.Index) : string.CompareOrdinal(one.Name, two.Name);
                if (order != 0)
                {
                    return order;
                }
            }

            return x.Segments.Count.CompareTo(y.Segments.Count);
        }
    }
}
