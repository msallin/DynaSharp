using System.Diagnostics;
using System.Numerics;
using System.Text;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A single DynamoDB attribute value: string, number, binary, boolean, null, list, map or set.
/// </summary>
/// <remarks>
/// <para>
/// This is the representation used by the low-level API (for example <see cref="GetItemRequest.Key"/>) and by <see cref="Item"/>.
/// Entities mapped by the source generator are serialized directly and never go through it. Strings, <see cref="int"/>,
/// <see cref="long"/>, <see cref="decimal"/>, <see cref="double"/>, <see cref="bool"/> and <see cref="byte"/>[] convert implicitly,
/// so <c>["pk"] = "user-1", ["age"] = 42, ["active"] = true</c> works in an item initializer. Other number types go through
/// <see cref="FromNumber{T}(T)"/>; the other kinds (lists, maps, sets, null) have factory methods. A <see cref="char"/> becomes a string, not
/// a number.
/// </para>
/// <para>
/// Numbers are kept as text, exactly as DynamoDB transfers them, so no precision is lost: DynamoDB numbers have up
/// to 38 significant digits, more than <see cref="decimal"/> or <see cref="double"/> can hold. Equality of numbers is
/// therefore textual: <c>1</c> and <c>1.0</c> are different values here, although DynamoDB treats them as equal.
/// </para>
/// <para>
/// Values are immutable: factory methods copy mutable inputs. The collections returned by <see cref="AsList"/>,
/// <see cref="AsMap"/> and the set accessors are the stored ones, exposed read-only without a copy; callers must not
/// cast them to mutable types and modify them. Equality is structural (deep) for lists and maps; sets compare without
/// regard to order. A <see langword="default"/> instance has <see cref="AttributeType.None"/> and cannot be sent to
/// DynamoDB.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct AttributeValue : IEquatable<AttributeValue>
{
    // Booleans share two boxes, so creating or reading one never allocates.
    private static readonly object BoxedTrue = true;
    private static readonly object BoxedFalse = false;

    private AttributeValue(AttributeType type, object? value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Gets the DynamoDB null value.</summary>
    public static AttributeValue Null => new(AttributeType.Null, null);

    /// <summary>Gets the data type of this value.</summary>
    public AttributeType Type { get; }

    /// <summary>Gets a value indicating whether this is the DynamoDB null value (<c>{"NULL": true}</c>).</summary>
    public bool IsNull => Type == AttributeType.Null;

    /// <summary>
    /// Gets the stored object. String and Number: <see cref="string"/>. Binary: <see cref="byte"/>[]. Boolean: a
    /// shared box. Null: <see langword="null"/>. List: <see cref="AttributeValue"/>[]. Map:
    /// <see cref="Item"/>, read-only. StringSet and NumberSet: <see cref="string"/>[]. BinarySet:
    /// <see cref="ReadOnlyMemory{T}"/>[].
    /// </summary>
    internal object? Value { get; }

    /// <summary>Converts a string to a string value, see <see cref="FromString(string)"/>.</summary>
    /// <param name="value">The string.</param>
    public static implicit operator AttributeValue(string value) => FromString(value);

    // The named alternates of the conversions below are FromNumber and FromBinary, which take every number type; a method per CLR type
    // (FromInt32, FromDouble, ...) would only repeat them.
#pragma warning disable CA2225
    /// <summary>Converts an integer to a number value, see <see cref="FromNumber{T}(T)"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator AttributeValue(int value) => FromNumber(value);

    /// <summary>Converts an integer to a number value, see <see cref="FromNumber{T}(T)"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator AttributeValue(long value) => FromNumber(value);

    /// <summary>Converts a decimal to a number value, see <see cref="FromNumber{T}(T)"/>. The text keeps the scale: <c>1.50m</c> becomes <c>1.50</c>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator AttributeValue(decimal value) => FromNumber(value);

    /// <summary>Converts a double to a number value, see <see cref="FromNumber{T}(T)"/>.</summary>
    /// <param name="value">The number, which must be finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite; DynamoDB cannot store either.</exception>
    public static implicit operator AttributeValue(double value) => FromNumber(value);

    /// <summary>Converts a boolean to a boolean value, see <see cref="FromBool"/>.</summary>
    /// <param name="value">The boolean.</param>
    public static implicit operator AttributeValue(bool value) => FromBool(value);

    /// <summary>Converts bytes to a binary value, see <see cref="FromBinary"/>. The bytes are copied.</summary>
    /// <param name="value">The bytes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator AttributeValue(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return FromBinary(value);
    }

    /// <summary>Converts a character to a string value. Declared because a <see cref="char"/> would otherwise widen to <see cref="int"/>, so <c>'a'</c> would become the number 97.</summary>
    /// <param name="value">The character.</param>
    public static implicit operator AttributeValue(char value) => FromString(value.ToString());
#pragma warning restore CA2225

    /// <summary>Compares two values structurally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if both are equal.</returns>
    public static bool operator ==(AttributeValue left, AttributeValue right) => left.Equals(right);

    /// <summary>Compares two values structurally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if both differ.</returns>
    public static bool operator !=(AttributeValue left, AttributeValue right) => !left.Equals(right);

    /// <summary>Creates a string value.</summary>
    /// <param name="value">The string. Empty strings are allowed except in key attributes, which DynamoDB validates.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static AttributeValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(AttributeType.String, value);
    }

    /// <summary>Creates a number value from its textual representation, sent to DynamoDB unchanged.</summary>
    /// <param name="value">The number as text, for example <c>"42"</c> or <c>"-1.5E-10"</c>. DynamoDB validates the format.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public static AttributeValue FromNumber(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new(AttributeType.Number, value);
    }

    /// <summary>
    /// Creates a number value, formatted with the invariant culture. A <see cref="char"/> counts as a number type in .NET but not here: it becomes
    /// a string value, as its implicit conversion does.
    /// </summary>
    /// <typeparam name="T">Any .NET number type, for example <see cref="int"/>, <see cref="decimal"/> or <see cref="double"/>.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite; DynamoDB cannot store either.</exception>
    public static AttributeValue FromNumber<T>(T value)
        where T : INumberBase<T>
    {
        if (typeof(T) == typeof(char))
        {
            return FromString(DynamoDbNumber.Format(value));
        }

        DynamoDbNumber.ThrowIfNotFinite(value);
        return new(AttributeType.Number, DynamoDbNumber.Format(value));
    }

    /// <summary>Creates a binary value. The bytes are copied.</summary>
    /// <param name="value">The bytes.</param>
    /// <returns>The value.</returns>
    public static AttributeValue FromBinary(ReadOnlySpan<byte> value) => new(AttributeType.Binary, value.ToArray());

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The value.</returns>
    public static AttributeValue FromBool(bool value) => new(AttributeType.Boolean, value ? BoxedTrue : BoxedFalse);

    /// <summary>Creates a list value. The sequence is copied; elements may have different types.</summary>
    /// <param name="values">The elements, possibly none.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An element has <see cref="AttributeType.None"/>.</exception>
    public static AttributeValue FromList(IEnumerable<AttributeValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        AttributeValue[] elements = [.. values];
        foreach (var element in elements)
        {
            ThrowIfNone(element, nameof(values));
        }

        return new(AttributeType.List, elements);
    }

    /// <summary>Creates a map value. The entries are copied.</summary>
    /// <param name="values">The entries, possibly none.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or a key is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is duplicated, or a value has <see cref="AttributeType.None"/>.</exception>
    public static AttributeValue FromMap(IEnumerable<KeyValuePair<string, AttributeValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _ = values.TryGetNonEnumeratedCount(out var count);
        var entries = new Item(count);
        foreach (var (key, value) in values)
        {
            ThrowIfNone(value, nameof(values));
            if (!entries.TryAdd(key, value))
            {
                throw new ArgumentException($"The attribute name \"{key}\" occurs more than once.", nameof(values));
            }
        }

        return WrapMap(entries);
    }

    /// <summary>Creates a string set. The sequence is copied.</summary>
    /// <param name="values">The strings. DynamoDB requires a set to be non-empty and free of duplicates.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or an element is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains duplicates.</exception>
    public static AttributeValue FromStringSet(IEnumerable<string> values) =>
        new(AttributeType.StringSet, CopyStringSet(values, allowEmptyElements: true));

    /// <summary>Creates a number set from textual numbers, sent to DynamoDB unchanged. The sequence is copied.</summary>
    /// <param name="values">The numbers as text. DynamoDB requires a set to be non-empty and free of duplicates.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or an element is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty, contains an empty element, or contains textual duplicates.</exception>
    public static AttributeValue FromNumberSet(IEnumerable<string> values) =>
        new(AttributeType.NumberSet, CopyStringSet(values, allowEmptyElements: false));

    /// <summary>Creates a binary set. Sequence and bytes are copied.</summary>
    /// <param name="values">The binary values. DynamoDB requires a set to be non-empty and free of duplicates.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or an element is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains duplicates by content.</exception>
    public static AttributeValue FromBinarySet(IEnumerable<byte[]> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var elements = new List<ReadOnlyMemory<byte>>();
        foreach (var element in values)
        {
            ArgumentNullException.ThrowIfNull(element, nameof(values));
            elements.Add(element.AsSpan().ToArray());
        }

        var copy = elements.ToArray();
        ThrowIfInvalidSet(copy, BinaryContentComparer.Instance, nameof(values));
        return new(AttributeType.BinarySet, copy);
    }

    /// <summary>Gets the string of a <see cref="AttributeType.String"/> value.</summary>
    /// <returns>The string.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public string AsString() => Get<string>(AttributeType.String);

    /// <summary>Gets the text of a <see cref="AttributeType.Number"/> value, as transferred.</summary>
    /// <returns>The number as text.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public string AsNumber() => Get<string>(AttributeType.Number);

    /// <summary>Parses a <see cref="AttributeType.Number"/> value with the invariant culture.</summary>
    /// <typeparam name="T">The target number type.</typeparam>
    /// <returns>The parsed number. Exponent notation such as <c>1E+2</c> is accepted, also for integer types.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    /// <exception cref="FormatException">The text cannot be represented as <typeparamref name="T"/>.</exception>
    /// <exception cref="OverflowException">
    /// The number is outside the range of <typeparamref name="T"/>, has a fractional part and <typeparamref name="T"/> is an integer type, or <typeparamref name="T"/> is
    /// <see cref="decimal"/> and cannot hold its exact value, which takes 28 to 29 significant digits at most, where DynamoDB has 38. A binary floating-point type counts a number
    /// that rounds to infinity, and <c>NaN</c>, as out of range; it rounds any other number to the nearest value it holds, and one below its range reads as 0.
    /// </exception>
    public T AsNumber<T>()
        where T : INumberBase<T>
        => DynamoDbNumber.Parse<T>(AsNumber());

    /// <summary>Gets the bytes of a <see cref="AttributeType.Binary"/> value.</summary>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public ReadOnlyMemory<byte> AsBinary() => Get<byte[]>(AttributeType.Binary);

    /// <summary>Gets the boolean of a <see cref="AttributeType.Boolean"/> value.</summary>
    /// <returns>The boolean.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public bool AsBool() => (bool)Get<object>(AttributeType.Boolean);

    /// <summary>Gets the elements of a <see cref="AttributeType.List"/> value.</summary>
    /// <returns>The elements in order.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public IReadOnlyList<AttributeValue> AsList() => Get<AttributeValue[]>(AttributeType.List);

    /// <summary>Gets the entries of a <see cref="AttributeType.Map"/> value.</summary>
    /// <returns>The entries.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public IReadOnlyDictionary<string, AttributeValue> AsMap() => Get<Item>(AttributeType.Map);

    /// <summary>Gets the elements of a <see cref="AttributeType.StringSet"/> value.</summary>
    /// <returns>The elements. The order carries no meaning.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public IReadOnlyList<string> AsStringSet() => Get<string[]>(AttributeType.StringSet);

    /// <summary>Gets the elements of a <see cref="AttributeType.NumberSet"/> value as text.</summary>
    /// <returns>The elements. The order carries no meaning.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public IReadOnlyList<string> AsNumberSet() => Get<string[]>(AttributeType.NumberSet);

    /// <summary>Gets the elements of a <see cref="AttributeType.BinarySet"/> value.</summary>
    /// <returns>The elements. The order carries no meaning.</returns>
    /// <exception cref="InvalidOperationException">The value has another type.</exception>
    public IReadOnlyList<ReadOnlyMemory<byte>> AsBinarySet() => Get<ReadOnlyMemory<byte>[]>(AttributeType.BinarySet);

    /// <inheritdoc/>
    public bool Equals(AttributeValue other) => Type == other.Type && Type switch
    {
        AttributeType.String or AttributeType.Number or AttributeType.Boolean => Value!.Equals(other.Value),
        AttributeType.Binary => ((byte[])Value!).AsSpan().SequenceEqual((byte[])other.Value!),
        AttributeType.List => ((AttributeValue[])Value!).AsSpan().SequenceEqual((AttributeValue[])other.Value!),
        AttributeType.Map => ((Item)Value!).Equals((Item)other.Value!),
        AttributeType.StringSet or AttributeType.NumberSet => SetEquals((string[])Value!, (string[])other.Value!, StringComparer.Ordinal),
        AttributeType.BinarySet => SetEquals((ReadOnlyMemory<byte>[])Value!, (ReadOnlyMemory<byte>[])other.Value!, BinaryContentComparer.Instance),
        _ => true,
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AttributeValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Type, Type switch
    {
        AttributeType.String or AttributeType.Number or AttributeType.Boolean => Value!.GetHashCode(),
        AttributeType.Binary => BinaryContentComparer.Instance.GetHashCode((byte[])Value!),
        AttributeType.List => ListHash((AttributeValue[])Value!),
        AttributeType.Map => Value!.GetHashCode(),
        AttributeType.StringSet or AttributeType.NumberSet => SetHash((string[])Value!, StringComparer.Ordinal),
        AttributeType.BinarySet => SetHash((ReadOnlyMemory<byte>[])Value!, BinaryContentComparer.Instance),
        _ => 0,
    });

    /// <summary>Returns the value in DynamoDB JSON, for example <c>{"S":"abc"}</c>. Intended for debugging; the debugger shows this text, without quotes.</summary>
    /// <returns>The DynamoDB JSON text, with U+FFFD for a lone surrogate, which no write takes, or an empty string for a <see langword="default"/> instance.</returns>
    public override string ToString()
    {
        if (Type == AttributeType.None)
        {
            return string.Empty;
        }

        using var buffer = new PooledByteBuffer(256);
        using (var json = WireJson.CreateWriter(buffer))
        {
            AttributeValueConverter.Write(DynamoDbWriter.ForDisplay(json), this);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The Wrap* factories take ownership of what the reader produced: no copy, no validation.
    internal static AttributeValue WrapNumber(string text) => new(AttributeType.Number, text);

    internal static AttributeValue WrapBinary(byte[] bytes) => new(AttributeType.Binary, bytes);

    internal static AttributeValue WrapList(AttributeValue[] elements) => new(AttributeType.List, elements);

    // The Item becomes part of an immutable value, so it stops accepting writes.
    internal static AttributeValue WrapMap(Item entries)
    {
        entries.MakeReadOnly();
        return new(AttributeType.Map, entries);
    }

    internal static AttributeValue WrapStringSet(string[] elements) => new(AttributeType.StringSet, elements);

    internal static AttributeValue WrapNumberSet(string[] elements) => new(AttributeType.NumberSet, elements);

    internal static AttributeValue WrapBinarySet(ReadOnlyMemory<byte>[] elements) => new(AttributeType.BinarySet, elements);

    private static void ThrowIfNone(AttributeValue value, string paramName)
    {
        if (value.Type == AttributeType.None)
        {
            throw new ArgumentException("A default AttributeValue has no type and cannot be part of a list or map.", paramName);
        }
    }

    private static string[] CopyStringSet(IEnumerable<string> values, bool allowEmptyElements)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] elements = [.. values];
        foreach (var element in elements)
        {
            ArgumentNullException.ThrowIfNull(element, nameof(values));
            if (!allowEmptyElements && element.Length == 0)
            {
                throw new ArgumentException("A number cannot be empty.", nameof(values));
            }
        }

        ThrowIfInvalidSet(elements, StringComparer.Ordinal, nameof(values));
        return elements;
    }

    private static void ThrowIfInvalidSet<T>(T[] elements, IEqualityComparer<T> comparer, string paramName)
    {
        if (elements.Length == 0)
        {
            throw new ArgumentException("A DynamoDB set must contain at least one element.", paramName);
        }

        if (new HashSet<T>(elements, comparer).Count != elements.Length)
        {
            throw new ArgumentException("A DynamoDB set cannot contain duplicates.", paramName);
        }
    }

    // Both sets are free of duplicates (validated on creation), so equal counts and containment mean equal sets.
    private static bool SetEquals<T>(T[] left, T[] right, IEqualityComparer<T> comparer) =>
        left.Length == right.Length && new HashSet<T>(left, comparer).SetEquals(right);

    private static int ListHash(AttributeValue[] elements)
    {
        var hash = default(HashCode);
        foreach (var element in elements)
        {
            hash.Add(element);
        }

        return hash.ToHashCode();
    }

    // A set has no order, so its hash adds up the element hashes, which does not depend on order.
    private static int SetHash<T>(T[] elements, IEqualityComparer<T> comparer)
    {
        var hash = 0;
        foreach (var element in elements)
        {
            hash += comparer.GetHashCode(element!);
        }

        return hash;
    }

    private T Get<T>(AttributeType expected)
        where T : class
        => Type == expected
            ? (T)Value!
            : throw new InvalidOperationException($"The attribute value has type {Type}, not {expected}.");

    private sealed class BinaryContentComparer : IEqualityComparer<ReadOnlyMemory<byte>>
    {
        public static readonly BinaryContentComparer Instance = new();

        public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) => x.Span.SequenceEqual(y.Span);

        public int GetHashCode(ReadOnlyMemory<byte> obj)
        {
            var hash = default(HashCode);
            hash.AddBytes(obj.Span);
            return hash.ToHashCode();
        }
    }
}
