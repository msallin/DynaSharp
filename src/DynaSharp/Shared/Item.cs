using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A DynamoDB item without a schema: attribute names and their values. Everything DynaSharp reads is one of these, and it goes wherever the
/// API takes attributes, since it is an <see cref="IReadOnlyDictionary{TKey, TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Build one with an initializer, <c>new Item { ["pk"] = "user-1", ["age"] = 42 }</c>; values convert implicitly, see
/// <see cref="AttributeValue"/>. The indexer sets or replaces an attribute, <see cref="Add"/> refuses a name that exists, and <see cref="Remove"/> takes one out.
/// </para>
/// <para>
/// Read one with the indexer, which throws for a missing attribute, or with the getters such as <see cref="GetString"/> and
/// <see cref="GetNumber{T}"/>, which return <see langword="null"/> for a missing attribute and for the DynamoDB null value. Use
/// <see cref="ContainsKey"/> and <see cref="AttributeValue.IsNull"/> to tell those two apart. A value of another type throws an exception
/// that names the attribute. Exception messages never contain the value, so they are safe to log. <see cref="ToString"/> is another
/// matter: it prints the item.
/// </para>
/// <para>
/// Attributes have no order in DynamoDB. An item built by hand enumerates in insertion order; a read item enumerates in an order that can
/// differ between items with the same attributes, so never rely on it. <see cref="Equals(Item)"/> ignores the order and is the only
/// sanctioned comparison; the text of <see cref="ToString"/> does not ignore it.
/// </para>
/// <para>
/// Items are mutable, compare by content and change their hash with their content, so do not use them as dictionary keys or in a hash set.
/// Writing into a read item affects that item alone, even though the items of a page share their attribute names. A map inside an item
/// (<see cref="GetMap"/>) is part of an immutable <see cref="AttributeValue"/>, so writing into it throws; copy it with
/// <see cref="Item(IEnumerable{KeyValuePair{string, AttributeValue}})"/>, change the copy and store it with
/// <see cref="AttributeValue.FromMap"/>. Reading from several threads is safe; writing is not, as with a dictionary.
/// </para>
/// <para>
/// Names and values are kept in two arrays, and items read from one response or with the same attributes share the names. A read item
/// therefore costs little more than its values, at any size. An item built by hand is cheap at the usual sizes, but above 32 attributes it
/// costs more than a <see cref="Dictionary{TKey, TValue}"/> of known capacity, since it then has a lookup index of its own. An import job
/// that writes large items can keep using dictionaries.
/// </para>
/// </remarks>
[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(ItemDebugView))]
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "An item is the DynamoDB concept, not a collection with another name.")]
public sealed class Item : IReadOnlyDictionary<string, AttributeValue>, IEquatable<Item>, IDynamoDbDocument<Item>
{
    private const int DefaultCapacity = 8;

    private AttributeValue[] _values;
    private bool _isReadOnly;

    /// <summary>Initializes a new instance of the <see cref="Item"/> class, without attributes.</summary>
    public Item()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Item"/> class, without attributes but with room for them.</summary>
    /// <param name="capacity">The number of attributes to expect. The item grows beyond it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public Item(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Shape = ItemShape.CreatePrivate(capacity);
        _values = new AttributeValue[capacity];
    }

    /// <summary>Initializes a new instance of the <see cref="Item"/> class with a copy of some attributes, in their order.</summary>
    /// <param name="attributes">The attributes, for example another item or a dictionary. The values themselves are immutable and not copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="attributes"/> or a name is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name occurs twice, or a value has <see cref="AttributeType.None"/>.</exception>
    public Item(IEnumerable<KeyValuePair<string, AttributeValue>> attributes)
        : this(0)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        foreach (var (name, value) in attributes)
        {
            Add(name, value);
        }
    }

    // Takes ownership of the array. The caller guarantees that the values are in the order of the shape.
    internal Item(ItemShape shape, AttributeValue[] values)
    {
        Shape = shape;
        _values = values;
        Count = values.Length;
    }

    /// <summary>Gets the number of attributes.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the attribute names. The order carries no meaning for a read item.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            for (var i = 0; i < Count; i++)
            {
                yield return Shape.Names[i];
            }
        }
    }

    /// <summary>Gets the values, in the order of <see cref="Keys"/>.</summary>
    public IEnumerable<AttributeValue> Values
    {
        get
        {
            for (var i = 0; i < Count; i++)
            {
                yield return _values[i];
            }
        }
    }

    /// <summary>Gets the names, which the next item of a page tries first if this is a read item.</summary>
    internal ItemShape Shape { get; private set; }

    /// <summary>Gets or sets an attribute. Setting replaces an existing attribute or adds a new one.</summary>
    /// <param name="name">The attribute name, compared ordinally. DynamoDB attribute names are case sensitive.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">Getting an attribute the item does not have. The message names the attribute.</exception>
    /// <exception cref="ArgumentException">Setting a value that has <see cref="AttributeType.None"/>.</exception>
    /// <exception cref="InvalidOperationException">Setting on a map that is part of an <see cref="AttributeValue"/>.</exception>
    public AttributeValue this[string name]
    {
        get
        {
            var position = Shape.Find(name, Count);
            return position >= 0 ? _values[position] : throw new KeyNotFoundException($"The item has no attribute \"{name}\".");
        }

        set
        {
            ThrowIfNotWritable(value);
            var position = Shape.Find(name, Count);
            if (position >= 0)
            {
                _values[position] = value;
            }
            else
            {
                Append(name, value);
            }
        }
    }

    /// <summary>
    /// Copies a mapped value into an item, through the attributes the value would be stored with. This is how a mapped entity reaches an
    /// API that takes attributes, such as the request API.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A new item with the attributes of <paramref name="value"/>, which also holds what the type adds by itself: its keys and its discriminator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Item From<T>(T value)
        where T : IDynamoDbDocument<T>
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return Convert<T, Item>(value);
    }

    /// <inheritdoc/>
    static void IDynamoDbDocument<Item>.WriteAttributes(DynamoDbWriter writer, Item entity) => entity.WriteAttributes(writer);

    /// <inheritdoc/>
    static Item IDynamoDbDocument<Item>.ReadAttributes(ref DynamoDbReader reader) => AttributeValueConverter.ReadAttributes(ref reader);

    /// <summary>Adds an attribute, so that collection initializers work too: <c>new Item { { "pk", "user-1" } }</c>.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The item has an attribute with this name, or <paramref name="value"/> has <see cref="AttributeType.None"/>.</exception>
    /// <exception cref="InvalidOperationException">This is a map that is part of an <see cref="AttributeValue"/>.</exception>
    public void Add(string name, AttributeValue value)
    {
        if (!TryAdd(name, value))
        {
            throw new ArgumentException($"The attribute name \"{name}\" occurs more than once.", nameof(name));
        }
    }

    /// <summary>Removes an attribute. The others keep their order.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns><see langword="true"/> if the item had the attribute.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is a map that is part of an <see cref="AttributeValue"/>.</exception>
    public bool Remove(string name)
    {
        ThrowIfReadOnly();
        var position = Shape.Find(name, Count);
        if (position < 0)
        {
            return false;
        }

        // The names can be shared with other items and have an index that holds positions, so what stays goes into a shape of its own.
        var left = Count - position - 1;
        var shape = Shape.CopyToPrivate(position, _values.Length);
        Shape.Names.AsSpan(position + 1, left).CopyTo(shape.Names.AsSpan(position));
        Shape = shape;
        _values.AsSpan(position + 1, left).CopyTo(_values.AsSpan(position));
        _values[--Count] = default;
        return true;
    }

    /// <summary>Determines whether the item has an attribute, including one with the DynamoDB null value.</summary>
    /// <param name="key">The attribute name.</param>
    /// <returns><see langword="true"/> if the item has the attribute.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool ContainsKey(string key) => Shape.Find(key, Count) >= 0;

    /// <summary>Gets an attribute if the item has it.</summary>
    /// <param name="key">The attribute name.</param>
    /// <param name="value">The value, or <see langword="default"/> if the item does not have the attribute.</param>
    /// <returns><see langword="true"/> if the item has the attribute.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(string key, out AttributeValue value)
    {
        var position = Shape.Find(key, Count);
        value = position >= 0 ? _values[position] : default;
        return position >= 0;
    }

    /// <summary>Gets a string attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The string, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public string? GetString(string name) => TryGetTyped(name, AttributeType.String, out var value) ? value.AsString() : null;

    /// <summary>Gets a number attribute, parsed with the invariant culture.</summary>
    /// <typeparam name="T">The target number type, for example <see cref="int"/> or <see cref="decimal"/>.</typeparam>
    /// <param name="name">The attribute name.</param>
    /// <returns>
    /// The number, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value. Exponent notation such as
    /// <c>1E+2</c> is accepted, also for integer types.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    /// <exception cref="FormatException">The text cannot be represented as <typeparamref name="T"/>. The message names the attribute, not the text.</exception>
    /// <exception cref="OverflowException">
    /// The number is outside the range of <typeparamref name="T"/>, has a fractional part and <typeparamref name="T"/> is an integer
    /// type, or <typeparamref name="T"/> is <see cref="decimal"/> and cannot hold its exact value, which takes 28 to 29 significant digits at most, where DynamoDB has 38.
    /// A binary floating-point type counts a number that rounds to infinity, and <c>NaN</c>, as out of range; it rounds any other number to the nearest value it holds, and one below
    /// its range reads as 0. The message names the attribute, not the number.
    /// </exception>
    public T? GetNumber<T>(string name)
        where T : struct, INumberBase<T>
    {
        if (!TryGetTyped(name, AttributeType.Number, out var value))
        {
            return null;
        }

        // The original exceptions quote the text, so they stay out of the new ones.
        try
        {
            return value.AsNumber<T>();
        }
        catch (FormatException)
        {
            throw new FormatException($"The number in attribute \"{name}\" cannot be represented in the requested type.");
        }
        catch (OverflowException)
        {
            throw new OverflowException($"The number in attribute \"{name}\" is outside the range of the requested type, or has a fractional part it cannot hold exactly.");
        }
    }

    /// <summary>Gets a boolean attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The boolean, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public bool? GetBool(string name) => TryGetTyped(name, AttributeType.Boolean, out var value) ? value.AsBool() : null;

    /// <summary>Gets a binary attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The bytes, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public ReadOnlyMemory<byte>? GetBinary(string name)
    {
        // Not a conditional expression: null would convert to an empty ReadOnlyMemory through the conversion from arrays.
        if (TryGetTyped(name, AttributeType.Binary, out var value))
        {
            return value.AsBinary();
        }

        return null;
    }

    /// <summary>
    /// Gets a map attribute as a read-only <see cref="Item"/>, for nested access such as <c>item.GetMap("address")?.GetString("city")</c>. It compiles like any item, but writing into it throws
    /// <see cref="InvalidOperationException"/>: copy it with <see cref="Item(IEnumerable{KeyValuePair{string, AttributeValue}})"/>, change the copy and store that.
    /// </summary>
    /// <param name="name">The attribute name. A dot has no special meaning: attribute names may contain dots.</param>
    /// <returns>The map, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public Item? GetMap(string name) => TryGetTyped(name, AttributeType.Map, out var value) ? (Item)value.Value! : null;

    /// <summary>Gets a list attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The elements in order, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public IReadOnlyList<AttributeValue>? GetList(string name) => TryGetTyped(name, AttributeType.List, out var value) ? value.AsList() : null;

    /// <summary>Gets a string set attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The elements, in no meaningful order, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public IReadOnlyList<string>? GetStringSet(string name) => TryGetTyped(name, AttributeType.StringSet, out var value) ? value.AsStringSet() : null;

    /// <summary>Gets a number set attribute as text, which keeps numbers beyond the range or precision of .NET types.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The elements, in no meaningful order, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public IReadOnlyList<string>? GetNumberSet(string name) => TryGetTyped(name, AttributeType.NumberSet, out var value) ? value.AsNumberSet() : null;

    /// <summary>Gets a binary set attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The elements, in no meaningful order, or <see langword="null"/> if the attribute is missing or has the DynamoDB null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The attribute has another type. The message names the attribute.</exception>
    public IReadOnlyList<ReadOnlyMemory<byte>>? GetBinarySet(string name) => TryGetTyped(name, AttributeType.BinarySet, out var value) ? value.AsBinarySet() : null;

    /// <summary>Reads the item as a mapped value, the way a response carrying these attributes is read.</summary>
    /// <typeparam name="T">The type to read.</typeparam>
    /// <returns>The value.</returns>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted, or the item is of another type.</exception>
    public T To<T>()
        where T : IDynamoDbDocument<T> => Convert<Item, T>(this);

    /// <summary>
    /// Gets whether the item is of an entity: its discriminator attribute, <c>_type</c> or the <see cref="DiscriminatorAttribute.AttributeName"/> the type names, holds the type's
    /// discriminator value. It is the test a mixed read sorts items by, for an item that is already read, such as an image of a stream record from a table of several entities:
    /// <c>if (image.Is&lt;Order&gt;()) { var order = image.To&lt;Order&gt;(); }</c>.
    /// </summary>
    /// <remarks>
    /// It looks at that one attribute and reads nothing else, so it costs no allocation, and an item that says it is a <typeparamref name="T"/> can still fail
    /// <see cref="To{T}"/> on another attribute. The comparison is ordinal. An item without the attribute, with the DynamoDB null value there or with a value that is no string is
    /// not of the type, as it is of no type to a mixed read.
    /// </remarks>
    /// <typeparam name="T">The entity. One without a discriminator, with <c>[Discriminator(OptOut = true)]</c>, does not compile, because nothing in its items tells them from others.</typeparam>
    /// <returns><see langword="true"/> if the item holds the discriminator of <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is written by hand and claims a discriminator that its <see cref="DynamoDbEntityInfo"/> does not have.</exception>
    public bool Is<T>()
        where T : IDynamoDbDiscriminated<T> => Fluent.MixedSchema.Of<T>.Schema.FindType(this) == 0;

    /// <summary>Gets an enumerator over the attributes, which does not allocate when used on this type directly.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <inheritdoc/>
    IEnumerator<KeyValuePair<string, AttributeValue>> IEnumerable<KeyValuePair<string, AttributeValue>>.GetEnumerator() => new Enumerator(this);

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    /// <summary>Compares two items by content: the same names with equal values, in any order.</summary>
    /// <param name="other">The other item.</param>
    /// <returns><see langword="true"/> if both have the same content.</returns>
    public bool Equals(Item? other)
    {
        if (other is null || other.Count != Count)
        {
            return false;
        }

        if (ReferenceEquals(other.Shape, Shape))
        {
            return _values.AsSpan(0, Count).SequenceEqual(other._values.AsSpan(0, Count));
        }

        var names = Shape.Names;
        for (var i = 0; i < Count; i++)
        {
            if (!other.TryGetValue(names[i], out var value) || !value.Equals(_values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Item);

    /// <summary>Gets a hash of the content that does not depend on the order, and changes when the item does.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
    {
        var names = Shape.Names;
        var hash = 0;
        for (var i = 0; i < Count; i++)
        {
            hash += HashCode.Combine(names[i], _values[i]);
        }

        return hash;
    }

    /// <summary>Returns the item in DynamoDB JSON, for example <c>{"pk":{"S":"a"},"n":{"N":"1"}}</c>. It contains the data of the item, so do not log it where that is a concern.</summary>
    /// <returns>The DynamoDB JSON text, with U+FFFD for a lone surrogate, which no write takes.</returns>
    public override string ToString()
    {
        using var buffer = new PooledByteBuffer(256);
        using (var json = WireJson.CreateWriter(buffer))
        {
            AttributeValueConverter.WriteItem(DynamoDbWriter.ForDisplay(json), this);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Marks a map that becomes part of an <see cref="AttributeValue"/>, which is immutable and shared when copied.</summary>
    internal void MakeReadOnly() => _isReadOnly = true;

    /// <summary>Writes the attribute names and values, without the surrounding object.</summary>
    /// <param name="writer">The writer, inside an item or a map.</param>
    internal void WriteAttributes(DynamoDbWriter writer)
    {
        var names = Shape.Names;
        for (var i = 0; i < Count; i++)
        {
            writer.WriteAttributeName(names[i]);
            AttributeValueConverter.Write(writer, _values[i]);
        }
    }

    /// <summary>Adds an attribute unless the item has one with this name.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="false"/> if the name exists.</returns>
    internal bool TryAdd(string name, AttributeValue value)
    {
        ThrowIfNotWritable(value);
        if (Shape.Find(name, Count) >= 0)
        {
            return false;
        }

        Append(name, value);
        return true;
    }

    // Both directions go through the wire format, so they cannot drift from what a request writes and a response reads.
    private static TTarget Convert<TSource, TTarget>(TSource value)
        where TSource : IDynamoDbDocument<TSource>
        where TTarget : IDynamoDbDocument<TTarget>
    {
        using var buffer = new PooledByteBuffer(256);
        using (var json = WireJson.CreateWriter(buffer))
        {
            var writer = DynamoDbWriter.ForDisplay(json);
            writer.WriteStartItem();
            TSource.WriteAttributes(writer, value);
            writer.WriteEndItem();
        }

        var reader = new DynamoDbReader(buffer.WrittenSpan);
        reader.ReadStartItem();
        return TTarget.ReadAttributes(ref reader);
    }

    private bool TryGetTyped(string name, AttributeType expected, out AttributeValue value)
    {
        if (!TryGetValue(name, out value) || value.IsNull)
        {
            return false;
        }

        if (value.Type != expected)
        {
            throw new InvalidOperationException($"The attribute \"{name}\" has type {value.Type}, not {expected}.");
        }

        return true;
    }

    private void ThrowIfReadOnly()
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("This map is part of an AttributeValue, which is immutable. Copy it into a new Item and store that.");
        }
    }

    private void ThrowIfNotWritable(AttributeValue value)
    {
        ThrowIfReadOnly();
        if (value.Type == AttributeType.None)
        {
            throw new ArgumentException("A default AttributeValue has no type and cannot be part of an item.", nameof(value));
        }
    }

    private void Append(string name, AttributeValue value)
    {
        // The arrays of a read item are full, so its first new attribute lands here too and moves it off the shape it shares with others.
        if (Count == _values.Length)
        {
            var capacity = Math.Max(Count * 2, DefaultCapacity);
            Array.Resize(ref _values, capacity);
            Shape = Shape.CopyToPrivate(Count, capacity);
        }

        Shape.Append(name, Count);
        _values[Count++] = value;
    }

    /// <summary>Enumerates the attributes of an item.</summary>
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Enumerators are not compared, as with Dictionary<TKey, TValue>.Enumerator.")]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "The conventional place of a struct enumerator, as in Dictionary<TKey, TValue>.")]
    public struct Enumerator : IEnumerator<KeyValuePair<string, AttributeValue>>
    {
        private readonly Item _item;
        private int _position;

        internal Enumerator(Item item)
        {
            _item = item;
            _position = -1;
        }

        /// <summary>Gets the current attribute.</summary>
        public readonly KeyValuePair<string, AttributeValue> Current => new(_item.Shape.Names[_position], _item._values[_position]);

        /// <inheritdoc/>
        readonly object IEnumerator.Current => Current;

        /// <summary>Moves to the next attribute.</summary>
        /// <returns><see langword="false"/> at the end.</returns>
        public bool MoveNext() => ++_position < _item.Count;

        /// <summary>Moves back to before the first attribute.</summary>
        public void Reset() => _position = -1;

        /// <summary>Does nothing: the enumerator holds no resources.</summary>
        public readonly void Dispose()
        {
        }
    }
}
