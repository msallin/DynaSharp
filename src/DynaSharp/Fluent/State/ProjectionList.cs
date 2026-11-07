using System.Globalization;
using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The <c>ProjectionExpression</c> of a read while it is written: paths as placeholders, separated by <c>, </c>. DynamoDB rejects a projection that names a path twice or two paths that overlap, so
/// a path that is in it already is left out, and a name a typed read needs is added only if no path starts with it.
/// </summary>
/// <remarks>
/// It holds nothing of its own: <see cref="ExpressionWriter.Projection"/> makes one over the clause of the projection, the maps of the writer and its scratch buffer, so it can be had wherever a
/// writer is, and a copy of the state copies the projection with the clause.
/// </remarks>
internal readonly ref struct ProjectionList
{
    private readonly PooledByteBuffer _text;
    private readonly ref PlaceholderMaps _placeholders;
    private readonly PooledByteBuffer _scratch;

    /// <summary>Initializes a new instance of the <see cref="ProjectionList"/> struct.</summary>
    /// <param name="text">The text of the projection.</param>
    /// <param name="placeholders">The maps its names go to.</param>
    /// <param name="scratch">A buffer that holds nothing between calls.</param>
    public ProjectionList(PooledByteBuffer text, ref PlaceholderMaps placeholders, PooledByteBuffer scratch)
    {
        _text = text;
        _placeholders = ref placeholders;
        _scratch = scratch;
    }

    /// <summary>Adds paths. A path that is in the projection already is left out.</summary>
    /// <param name="paths">The paths.</param>
    public void Add(ReadOnlySpan<string> paths)
    {
        var text = _text;
        foreach (var path in paths)
        {
            // A name has one placeholder, so the same path is the same text: it is written, and taken back if an earlier path reads the same.
            var mark = text.WrittenCount;
            if (mark > 0)
            {
                text.Append(", "u8);
            }

            var start = text.WrittenCount;
            _placeholders.AppendPath(text, path);
            if (mark > 0 && HasPath(text.WrittenSpan[..mark], text.WrittenSpan[start..]))
            {
                text.Truncate(mark);
            }
        }
    }

    /// <summary>Adds attributes, see <see cref="Add(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes.</param>
    public void Add(ReadOnlySpan<Attr> attributes)
    {
        foreach (ref readonly var attribute in attributes)
        {
            var path = attribute.Path;
            Add(new ReadOnlySpan<string>(in path));
        }
    }

    /// <summary>
    /// Adds a name the projection has none of yet: what a typed read cannot do without. A path that starts with the name counts, because
    /// DynamoDB rejects two paths that overlap.
    /// </summary>
    /// <param name="name">The attribute name, taken as it is.</param>
    public void AddName(string name)
    {
        var candidate = _scratch;
        candidate.Truncate(0);
        _placeholders.AppendName(candidate, name);
        if (!HasRoot(_text.WrittenSpan, candidate.WrittenSpan))
        {
            _text.Append(", "u8);
            _text.Append(candidate.WrittenSpan);
        }
    }

    /// <summary>Adds names, if there is a projection: a read without one gets every attribute anyway.</summary>
    /// <param name="names">The attribute names, see <see cref="AddName"/>.</param>
    public void Complete(ReadOnlySpan<string> names)
    {
        if (_text.WrittenCount > 0)
        {
            foreach (var name in names)
            {
                AddName(name);
            }
        }
    }

    /// <summary>
    /// Writes the projection with its names in place of the placeholders, as a request would have it, for the span of a read. The projection
    /// names attributes and holds no values.
    /// </summary>
    /// <returns>The paths, or <see langword="null"/> if there is no projection.</returns>
    public string? Resolve()
    {
        // #n0, #n1.#n2[3]
        var rest = _text.WrittenSpan;
        if (rest.IsEmpty)
        {
            return null;
        }

        var paths = new StringBuilder();
        while (true)
        {
            var hash = rest.IndexOf((byte)'#');
            if (hash < 0)
            {
                return paths.Append(Encoding.ASCII.GetString(rest)).ToString();
            }

            paths.Append(Encoding.ASCII.GetString(rest[..hash]));
            rest = rest[(hash + "#n"u8.Length)..];
            var digits = rest.IndexOfAnyExceptInRange((byte)'0', (byte)'9');
            digits = digits < 0 ? rest.Length : digits;
            paths.Append(_placeholders.NameAt(int.Parse(rest[..digits], CultureInfo.InvariantCulture)));
            rest = rest[digits..];
        }
    }

    // Finds whether a projection holds a path.
    // Projection: #n0, #n1.#n2[3]
    // Path #n1.#n2[3]: true. Path #n1: false, it only overlaps.
    private static bool HasPath(ReadOnlySpan<byte> projection, ReadOnlySpan<byte> path)
    {
        while (true)
        {
            var comma = projection.IndexOf((byte)',');
            if ((comma < 0 ? projection : projection[..comma]).SequenceEqual(path))
            {
                return true;
            }

            if (comma < 0)
            {
                return false;
            }

            projection = projection[(comma + ", "u8.Length)..];
        }
    }

    // Finds whether a path of a projection starts with a placeholder.
    // Projection: #n0, #n1.#n2[3]
    // Name #n1:   true, the second path starts with it. Name #n2: false, it is not the start of a path.
    private static bool HasRoot(ReadOnlySpan<byte> projection, ReadOnlySpan<byte> name)
    {
        while (true)
        {
            var comma = projection.IndexOf((byte)',');
            var path = comma < 0 ? projection : projection[..comma];
            var end = path.IndexOfAny((byte)'.', (byte)'[');
            if ((end < 0 ? path : path[..end]).SequenceEqual(name))
            {
                return true;
            }

            if (comma < 0)
            {
                return false;
            }

            projection = projection[(comma + ", "u8.Length)..];
        }
    }
}
