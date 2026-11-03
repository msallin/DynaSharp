namespace DynaSharp.Testing.Fake;

/// <summary>
/// Orders and compares attribute values by the rules of DynamoDB: a string by its UTF-8 bytes, unsigned, a binary by its bytes, unsigned, a number by its value with all its digits.
/// The instance is the comparer of the stores, which also puts two NULL values level, for the empty sort slot of a table without a sort key.
/// </summary>
/// <remarks>
/// A condition that compares values of different types, or a list, a map or a set with <c>&lt;</c>, is false and no error in DynamoDB, so <see cref="TryCompare"/> answers
/// <see langword="false"/> for those and the caller decides. <see cref="AreEqual"/> is the <c>=</c> of a condition: structural for lists and maps, without order for sets,
/// and by value for numbers, so <c>1</c> equals <c>1.0</c>.
/// </remarks>
internal sealed class AttributeComparer : IComparer<AttributeValue>
{
    /// <summary>The comparer of the stores.</summary>
    public static readonly AttributeComparer Instance = new();

    private AttributeComparer()
    {
    }

    /// <summary>Orders two values of one scalar type, as <c>&lt;</c>, <c>BETWEEN</c> and a sort key do.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <param name="order">Less than zero, zero or more than zero, or 0 when there is no order.</param>
    /// <returns><see langword="false"/> when the types differ or are not S, N or B.</returns>
    public static bool TryCompare(AttributeValue x, AttributeValue y, out int order)
    {
        order = 0;
        if (x.Type != y.Type)
        {
            return false;
        }

        switch (x.Type)
        {
            case AttributeType.String:
                order = CompareStrings(x.AsString(), y.AsString());
                return true;
            case AttributeType.Number:
                order = DecimalNumber.Parse(x.AsNumber()).CompareTo(DecimalNumber.Parse(y.AsNumber()));
                return true;
            case AttributeType.Binary:
                order = x.AsBinary().Span.SequenceCompareTo(y.AsBinary().Span);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Tells whether two values are equal as the <c>=</c> of a condition sees them.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <returns><see langword="true"/> if both have the same type and the same content.</returns>
    public static bool AreEqual(AttributeValue x, AttributeValue y)
    {
        if (x.Type != y.Type)
        {
            return false;
        }

        return x.Type switch
        {
            AttributeType.String or AttributeType.Number or AttributeType.Binary => Instance.Compare(x, y) == 0,
            AttributeType.Boolean => x.AsBool() == y.AsBool(),
            AttributeType.Null => true,
            AttributeType.List => ListsEqual(x.AsList(), y.AsList()),
            AttributeType.Map => MapsEqual(x.AsMap(), y.AsMap()),
            AttributeType.StringSet => SetsEqual(x.AsStringSet(), y.AsStringSet(), static (a, b) => string.Equals(a, b, StringComparison.Ordinal)),
            AttributeType.NumberSet => SetsEqual(x.AsNumberSet(), y.AsNumberSet(), static (a, b) => DecimalNumber.Parse(a) == DecimalNumber.Parse(b)),
            AttributeType.BinarySet => SetsEqual(x.AsBinarySet(), y.AsBinarySet(), static (a, b) => a.Span.SequenceEqual(b.Span)),
            _ => false,
        };
    }

    /// <summary>
    /// Orders two strings by their UTF-8 bytes, which is the order of their code points. UTF-16 code units have that order too, except that a surrogate, which only a
    /// code point above U+FFFF takes, sorts below U+E000 to U+FFFF; moving the two ranges past each other repairs that, without encoding either string.
    /// </summary>
    /// <param name="x">The first string.</param>
    /// <param name="y">The second string.</param>
    /// <returns>Less than zero, zero or more than zero.</returns>
    public static int CompareStrings(string x, string y)
    {
        var length = Math.Min(x.Length, y.Length);
        for (var i = 0; i < length; i++)
        {
            if (x[i] != y[i])
            {
                return InCodePointOrder(x[i]).CompareTo(InCodePointOrder(y[i]));
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    /// <summary>Compares two keys of one store: two values of one scalar type, or two NULL values.</summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The second value.</param>
    /// <returns>Less than zero, zero or more than zero.</returns>
    /// <exception cref="InvalidOperationException">The values cannot be ordered, which is a bug of the fake: a store holds keys of one declared type.</exception>
    public int Compare(AttributeValue x, AttributeValue y)
    {
        if (x.IsNull && y.IsNull)
        {
            return 0;
        }

        return TryCompare(x, y, out var order)
            ? order
            : throw new InvalidOperationException($"A store of the fake compared a {AttributeRules.Descriptor(x.Type)} with a {AttributeRules.Descriptor(y.Type)}, which have no order.");
    }

    // U+D800-U+DFFF moves up above U+FFFF's place and U+E000-U+FFFF down into the gap: "" < "😀" (U+1F600), as in UTF-8.
    private static int InCodePointOrder(char c) => c switch
    {
        >= '' => c - 0x800,
        >= '\uD800' => c + 0x2000,
        _ => c,
    };

    private static bool ListsEqual(IReadOnlyList<AttributeValue> x, IReadOnlyList<AttributeValue> y)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        for (var i = 0; i < x.Count; i++)
        {
            if (!AreEqual(x[i], y[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MapsEqual(IReadOnlyDictionary<string, AttributeValue> x, IReadOnlyDictionary<string, AttributeValue> y)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        foreach (var (name, value) in x)
        {
            if (!y.TryGetValue(name, out var other) || !AreEqual(value, other))
            {
                return false;
            }
        }

        return true;
    }

    // A set has no duplicates, so the same count and every element found means the same set.
    private static bool SetsEqual<T>(IReadOnlyList<T> x, IReadOnlyList<T> y, Func<T, T, bool> equal)
    {
        if (x.Count != y.Count)
        {
            return false;
        }

        foreach (var element in x)
        {
            var found = false;
            foreach (var candidate in y)
            {
                found |= equal(element, candidate);
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
