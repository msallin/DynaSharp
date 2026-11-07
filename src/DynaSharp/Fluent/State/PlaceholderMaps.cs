using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The maps of names and values of one set of expressions, <c>ExpressionAttributeNames</c> and <c>ExpressionAttributeValues</c>, while they are written: each name and value gets a placeholder,
/// <c>#n0</c> or <c>:v0</c>, and one that occurred before gets the placeholder it got then. A path is parsed here, because it is a sequence of names.
/// </summary>
/// <remarks>
/// <para>
/// A candidate is written to the end of its map and compared with what is there. On a match it is taken back, which makes
/// deduplication the same for every type: no comparer, no hash, no box. It is linear, and a request is limited to 4 KB of expression
/// text anyway. Names are compared as encoded text, so one spelling has one form.
/// </para>
/// <para>
/// A map is kept as JSON without its closing brace, so that the next entry can be added and the request takes it as it is. A slice marks where each entry sits in the map: a name between its quotes,
/// a value as its JSON. It is a struct in a field of <see cref="ExpressionWriter"/>, which hands it out by reference, so it is never copied.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Part of a pooled object: Clear(0) gives the arrays back, and the object lives on.")]
internal struct PlaceholderMaps
{
    private const int FirstRentLength = 256;
    private const int FirstSliceCapacity = 16;

    private readonly PooledByteBuffer _names;
    private readonly PooledByteBuffer _values;

    // The writer of values that take the long way: it is clean before each value and after it.
    private readonly Utf8JsonWriter _valueJson;
    private Slice[] _nameSlices;
    private Slice[] _valueSlices;
    private int _nameCount;
    private int _valueCount;

    /// <summary>Initializes a new instance of the <see cref="PlaceholderMaps"/> struct.</summary>
    /// <param name="pool">The pool of the arrays, which are rented when they are first written to.</param>
    public PlaceholderMaps(ArrayPool<byte> pool)
    {
        _names = new PooledByteBuffer(pool, FirstRentLength);
        _values = new PooledByteBuffer(pool, FirstRentLength);
        _valueJson = WireJson.CreateWriter(_values);
        _nameSlices = new Slice[FirstSliceCapacity];
        _valueSlices = new Slice[FirstSliceCapacity];
    }

    /// <summary>Writes a document path as placeholders, reusing the placeholder of a name that occurred before.</summary>
    /// <remarks>
    /// <c>address.city</c> becomes <c>#n0.#n1</c>, <c>lines[2].sku</c> becomes <c>#n2[2].#n3</c>, <c>matrix[0][1]</c> becomes <c>#n4[0][1]</c>. <c>PathSyntax</c> of the analyzer is a copy of this.
    /// </remarks>
    /// <param name="text">The expression text.</param>
    /// <param name="path">The path.</param>
    /// <exception cref="ArgumentException">The path has an empty name or a list index that is not a number.</exception>
    public void AppendPath(PooledByteBuffer text, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var rest = path.AsSpan();
        while (true)
        {
            var stop = rest.IndexOfAny('.', '[');
            var name = stop < 0 ? rest : rest[..stop];
            if (name.IsEmpty)
            {
                throw InvalidPath(path);
            }

            AppendName(text, name);
            if (stop < 0)
            {
                return;
            }

            rest = rest[stop..];
            while (rest[0] == '[')
            {
                var close = rest.IndexOf(']');
                if (close < 2 || rest[1..close].ContainsAnyExceptInRange('0', '9'))
                {
                    throw InvalidPath(path);
                }

                text.AppendAscii(rest[..(close + 1)]);
                rest = rest[(close + 1)..];
                if (rest.IsEmpty)
                {
                    return;
                }
            }

            if (rest[0] != '.')
            {
                throw InvalidPath(path);
            }

            text.Append((byte)'.');
            rest = rest[1..];
        }
    }

    /// <summary>
    /// Writes one attribute name as a placeholder. Unlike a path it is taken as it is: the names of keys and of the discriminator come from the
    /// table or the entity, and a dot is legal in them.
    /// </summary>
    /// <param name="text">The expression text.</param>
    /// <param name="name">The attribute name.</param>
    public void AppendName(PooledByteBuffer text, ReadOnlySpan<char> name)
    {
        var mark = _names.WrittenCount;
        var start = StartName();
        WireJson.AppendEncoded(_names, name);
        EndName(text, mark, start);
    }

    /// <summary>Writes a name that is encoded for a JSON string already as a placeholder, such as a name of a key in a request.</summary>
    /// <param name="text">The expression text.</param>
    /// <param name="encoded">The name, without its quotes.</param>
    public void AppendEncodedName(PooledByteBuffer text, ReadOnlySpan<byte> encoded)
    {
        var mark = _names.WrittenCount;
        var start = StartName();
        _names.Append(encoded);
        EndName(text, mark, start);
    }

    /// <summary>Writes one value through its converter and a placeholder for it. Equal values share a placeholder.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="text">The expression text.</param>
    /// <param name="value">The value.</param>
    public void AppendValue<T, TConverter>(PooledByteBuffer text, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var mark = _values.WrittenCount;
        var start = StartValue();
        TConverter.Write(new DynamoDbWriter(_valueJson, _values), value);
        CommandState.EndWrite(_valueJson);
        EndValue(text, mark, start);
    }

    /// <summary>Writes a value that is JSON already as a placeholder, such as a value of a key in a request.</summary>
    /// <param name="text">The expression text.</param>
    /// <param name="json">The attribute value.</param>
    public void AppendEncodedValue(PooledByteBuffer text, ReadOnlySpan<byte> json)
    {
        var mark = _values.WrittenCount;
        var start = StartValue();
        _values.Append(json);
        EndValue(text, mark, start);
    }

    /// <summary>
    /// Adds the value of <c>:pk</c>, the partition of a fan-out query, as the last value of a copy of its request. It has a placeholder outside the numbering, so that no other value
    /// can come to share it through deduplication: it differs per partition, and they do not.
    /// </summary>
    /// <param name="value">The partition key.</param>
    public readonly void AddPartitionKey(AttributeValue value)
    {
        // ,":pk":{"S":"a"}
        var values = _values;
        values.Append(_valueCount == 0 ? "{\":pk\":"u8 : ",\":pk\":"u8);
        AttributeValueConverter.Write(new DynamoDbWriter(_valueJson, values), value);
        CommandState.EndWrite(_valueJson);
    }

    /// <summary>Finds the placeholder a name has, without giving it one.</summary>
    /// <param name="encoded">The name, encoded for a JSON string and without its quotes.</param>
    /// <returns>The number of its placeholder, or -1 if the name has none.</returns>
    public readonly int IndexOfName(ReadOnlySpan<byte> encoded) => Find(_nameSlices, _nameCount, _names.WrittenSpan, encoded);

    /// <summary>Gets the name of a placeholder.</summary>
    /// <param name="index">The number of the placeholder.</param>
    /// <returns>The attribute name, unescaped.</returns>
    public readonly string NameAt(int index)
    {
        // The slice sits between its quotes, so it is a JSON string that a reader can unescape.
        var reader = new Utf8JsonReader(QuotedNameAt(index));
        reader.Read();
        return reader.GetString()!;
    }

    /// <summary>Gets the name of a placeholder as the map holds it.</summary>
    /// <param name="index">The number of the placeholder.</param>
    /// <returns>The name as a JSON string, with its quotes.</returns>
    public readonly ReadOnlySpan<byte> QuotedNameAt(int index)
    {
        var slice = _nameSlices[index];
        return _names.WrittenSpan.Slice(slice.Offset - 1, slice.Length + 2);
    }

    /// <summary>Gets the value of a placeholder as the map holds it.</summary>
    /// <param name="index">The number of the placeholder.</param>
    /// <returns>The attribute value as JSON.</returns>
    public readonly ReadOnlySpan<byte> ValueAt(int index)
    {
        var slice = _valueSlices[index];
        return _values.WrittenSpan.Slice(slice.Offset, slice.Length);
    }

    /// <summary>Writes the maps as members of the request, each if it holds anything. It changes nothing here, so it can be repeated.</summary>
    /// <param name="body">The request body, with at least one member before this.</param>
    public readonly void WriteMembers(PooledByteBuffer body)
    {
        WriteMap(body, ",\"ExpressionAttributeNames\":"u8, _names);
        WriteMap(body, ",\"ExpressionAttributeValues\":"u8, _values);
    }

    /// <summary>Copies the maps of another set of expressions into these empty ones.</summary>
    /// <param name="other">The maps to copy, which stay as they are.</param>
    public void CopyFrom(in PlaceholderMaps other)
    {
        _names.Append(other._names.WrittenSpan);
        _values.Append(other._values.WrittenSpan);
        for (var i = 0; i < other._nameCount; i++)
        {
            Add(ref _nameSlices, ref _nameCount, other._nameSlices[i]);
        }

        for (var i = 0; i < other._valueCount; i++)
        {
            Add(ref _valueSlices, ref _valueCount, other._valueSlices[i]);
        }
    }

    /// <summary>Empties the maps for the next set of expressions. The arrays stay, unless they grew beyond what an idle object should hold.</summary>
    /// <param name="maxRetainedLength">The longest array a map keeps; 0 gives them all back to the pool.</param>
    public void Clear(int maxRetainedLength)
    {
        _names.Clear(maxRetainedLength);
        _values.Clear(maxRetainedLength);
        _valueJson.Reset();
        _nameCount = 0;
        _valueCount = 0;
    }

    private static int Find(Slice[] slices, int count, ReadOnlySpan<byte> map, ReadOnlySpan<byte> candidate)
    {
        for (var i = 0; i < count; i++)
        {
            var slice = slices[i];
            if (slice.Length == candidate.Length && map.Slice(slice.Offset, slice.Length).SequenceEqual(candidate))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Add(ref Slice[] slices, ref int count, Slice slice)
    {
        if (count == slices.Length)
        {
            Array.Resize(ref slices, count * 2);
        }

        slices[count++] = slice;
    }

    private static void WriteMap(PooledByteBuffer body, ReadOnlySpan<byte> member, PooledByteBuffer map)
    {
        if (map.WrittenCount > 0)
        {
            body.Append(member);
            body.Append(map.WrittenSpan);
            body.Append((byte)'}');
        }
    }

    private static ArgumentException InvalidPath(string path) =>
        new($"The attribute path \"{path}\" is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c.", nameof(path));

    // ,"#n3":"status"
    private readonly int StartName()
    {
        var names = _names;
        names.Append(_nameCount == 0 ? "{\"#n"u8 : ",\"#n"u8);
        names.AppendNumber(_nameCount);
        names.Append("\":\""u8);
        return names.WrittenCount;
    }

    private void EndName(PooledByteBuffer text, int mark, int start)
    {
        var names = _names;
        var written = names.WrittenSpan;
        var index = Find(_nameSlices, _nameCount, written, written[start..]);
        if (index >= 0)
        {
            names.Truncate(mark);
        }
        else
        {
            index = _nameCount;
            Add(ref _nameSlices, ref _nameCount, new Slice(start, names.WrittenCount - start));
            names.Append((byte)'"');
        }

        text.Append("#n"u8);
        text.AppendNumber(index);
    }

    // ,":v3":{"S":"open"}
    private readonly int StartValue()
    {
        var values = _values;
        values.Append(_valueCount == 0 ? "{\":v"u8 : ",\":v"u8);
        values.AppendNumber(_valueCount);
        values.Append("\":"u8);
        return values.WrittenCount;
    }

    // Kept out of line, so that the generic AppendValue stays small enough to be inlined into its callers, which are shared code for a value of a reference type.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EndValue(PooledByteBuffer text, int mark, int start)
    {
        var values = _values;
        var written = values.WrittenSpan;
        var index = Find(_valueSlices, _valueCount, written, written[start..]);
        if (index >= 0)
        {
            values.Truncate(mark);
        }
        else
        {
            index = _valueCount;
            Add(ref _valueSlices, ref _valueCount, new Slice(start, values.WrittenCount - start));
        }

        text.Append(":v"u8);
        text.AppendNumber(index);
    }

    private readonly record struct Slice(int Offset, int Length);
}
