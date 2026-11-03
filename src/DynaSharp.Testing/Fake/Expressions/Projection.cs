namespace DynaSharp.Testing.Fake;

/// <summary>
/// Takes the paths of a projection out of an item, as a <c>ProjectionExpression</c> does: the attributes named, nested entries inside their maps, and list elements inside their lists in the
/// order of their indexes. Nothing else comes along, not even the key, and a path that leads nowhere gives nothing.
/// <code>
/// Item:   {"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}},"l":{"L":[{"S":"x"},{"S":"y"},{"S":"z"}]},"k":{"S":"key"}}
/// Paths:  a.b, l[2], l[0], missing
/// Result: {"a":{"M":{"b":{"N":"1"}}},"l":{"L":[{"S":"x"},{"S":"z"}]}}
/// </code>
/// </summary>
internal static class Projection
{
    /// <summary>Projects an item.</summary>
    /// <param name="item">The item.</param>
    /// <param name="paths">The paths. One inside another takes the outer one whole.</param>
    /// <returns>A new item.</returns>
    public static Item Apply(IReadOnlyDictionary<string, AttributeValue> item, IReadOnlyList<DocumentPath> paths)
    {
        var root = new Selection();
        foreach (var path in paths)
        {
            root.Add(path.Segments);
        }

        return root.Names is { } names ? ProjectMap(item, names) : [];
    }

    private static Item ProjectMap(IReadOnlyDictionary<string, AttributeValue> map, Dictionary<string, Selection> names)
    {
        var result = new Item();
        foreach (var (name, selection) in names)
        {
            if (map.TryGetValue(name, out var value) && Project(value, selection) is { } projected)
            {
                result.Add(name, projected);
            }
        }

        return result;
    }

    private static AttributeValue? Project(AttributeValue value, Selection selection)
    {
        if (selection.Whole)
        {
            return value;
        }

        if (selection.Names is { } names)
        {
            var map = value.Type == AttributeType.Map ? ProjectMap(value.AsMap(), names) : [];
            return map.Count == 0 ? (AttributeValue?)null : AttributeValue.FromMap(map);
        }

        var list = value.Type == AttributeType.List ? value.AsList() : [];
        var elements = new List<AttributeValue>();
        foreach (var (index, element) in selection.Indexes!)
        {
            if (index < list.Count && Project(list[index], element) is { } projected)
            {
                elements.Add(projected);
            }
        }

        return elements.Count == 0 ? (AttributeValue?)null : AttributeValue.FromList(elements);
    }

    // The paths as a tree: a node takes its value whole, or the entries of a map by name, or the elements of a list by index. The parser rejects paths that would want both.
    private sealed class Selection
    {
        public bool Whole { get; private set; }

        public Dictionary<string, Selection>? Names { get; private set; }

        public SortedDictionary<int, Selection>? Indexes { get; private set; }

        public void Add(IReadOnlyList<PathSegment> segments)
        {
            var node = this;
            foreach (var segment in segments)
            {
                if (node.Whole)
                {
                    return;
                }

                node = segment.Name is { } name ? Child(node.Names ??= new(StringComparer.Ordinal), name) : Child(node.Indexes ??= [], segment.Index);
            }

            node.Whole = true;
            node.Names = null;
            node.Indexes = null;
        }

        private static Selection Child<TKey>(IDictionary<TKey, Selection> children, TKey key)
        {
            if (!children.TryGetValue(key, out var child))
            {
                child = new Selection();
                children.Add(key, child);
            }

            return child;
        }
    }
}
