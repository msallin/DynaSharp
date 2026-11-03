namespace DynaSharp.Testing.Fake;

/// <summary>
/// A document path of an expression, with its placeholders resolved: <c>#pr.#5star[1]</c> with <c>#pr</c> for <c>ProductReviews</c> and <c>#5star</c> for <c>FiveStar</c> is
/// <c>ProductReviews</c>, <c>FiveStar</c>, <c>[1]</c>. The first step is always a name, the attribute of the item.
/// </summary>
/// <param name="segments">The steps, at least one, the first a name.</param>
internal sealed class DocumentPath(IReadOnlyList<PathSegment> segments)
{
    /// <summary>Gets the steps.</summary>
    public IReadOnlyList<PathSegment> Segments { get; } = segments;

    /// <summary>Gets the attribute of the item the path starts at.</summary>
    public string Root => Segments[0].Name!;

    /// <summary>
    /// Throws for two paths of one expression that touch each other, as DynamoDB does for an update and a projection: one is the other or lies inside it (overlap), or one takes a
    /// step as a map entry where the other takes it as a list element (conflict).
    /// </summary>
    /// <param name="paths">The paths, in the order of the expression.</param>
    /// <param name="member">The member of the request, which the message names.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>: <c>Two document paths overlap with each other; must remove or rewrite one of these paths; path one: [a], path two: [a, b]</c>.</exception>
    public static void ThrowIfOverlapping(IReadOnlyList<DocumentPath> paths, string member)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            for (var j = i + 1; j < paths.Count; j++)
            {
                var relation = Relate(paths[i], paths[j]);
                if (relation is not null)
                {
                    throw FakeError.Validation(
                        $"Invalid {member}: Two document paths {relation} with each other; must remove or rewrite one of these paths; path one: {paths[i]}, path two: {paths[j]}");
                }
            }
        }
    }

    /// <summary>Finds the value the path leads to.</summary>
    /// <param name="item">The item, or <see langword="null"/> where there is none.</param>
    /// <param name="value">The value, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> if every step exists: a name in a map, an index inside a list.</returns>
    public bool TryResolve(IReadOnlyDictionary<string, AttributeValue>? item, out AttributeValue value)
    {
        value = default;
        if (item is null || !item.TryGetValue(Root, out var current))
        {
            return false;
        }

        for (var i = 1; i < Segments.Count; i++)
        {
            if (!TryStep(current, Segments[i], out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    /// <summary>Gets the path as DynamoDB prints it in a message: <c>[a, b, [0]]</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => "[" + string.Join(", ", Segments) + "]";

    private static bool TryStep(AttributeValue container, PathSegment segment, out AttributeValue value)
    {
        value = default;
        if (segment.Name is { } name)
        {
            return container.Type == AttributeType.Map && container.AsMap().TryGetValue(name, out value);
        }

        if (container.Type != AttributeType.List || segment.Index >= container.AsList().Count)
        {
            return false;
        }

        value = container.AsList()[segment.Index];
        return true;
    }

    // a and a.b overlap, a.b and a[0] conflict, a.b and a.c are apart.
    private static string? Relate(DocumentPath first, DocumentPath second)
    {
        var common = Math.Min(first.Segments.Count, second.Segments.Count);
        for (var i = 0; i < common; i++)
        {
            var one = first.Segments[i];
            var two = second.Segments[i];
            if (one.IsIndex != two.IsIndex)
            {
                return "conflict";
            }

            if (one != two)
            {
                return null;
            }
        }

        return "overlap";
    }
}
