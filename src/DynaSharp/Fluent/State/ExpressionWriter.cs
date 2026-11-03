using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DynaSharp.Expressions;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The expressions of one request while they are written: the text of each clause and the maps of names and values, which one fluent call
/// feeds at the same time although they are separate JSON members. Everything is UTF-8 that is already encoded for a JSON string, so
/// putting the members together at the end is a copy.
/// </summary>
/// <remarks>
/// <para>
/// A candidate is written to the end of its map and compared with what is there. On a match it is taken back, which makes
/// deduplication the same for every type: no comparer, no hash, no box. It is linear, and a request is limited to 4 KB of expression
/// text anyway. Names are compared as encoded text, so one spelling has one form.
/// </para>
/// <para>A transaction writes several sets of expressions with one writer: <see cref="WriteMembers"/> and <see cref="Clear"/> for each action.</para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A pooled object: Release gives the arrays back, and the object lives on.")]
internal sealed class ExpressionWriter
{
    /// <summary>
    /// The keywords of a condition, which count in any case. They repeat <c>ExpressionParser.Keywords</c> of the generator, which ships as an analyzer that this assembly cannot
    /// reference. <c>ExpressionWriterTests</c> keeps the two in step and names where they differ on purpose.
    /// </summary>
    internal static readonly string[] ConditionKeywords = ["AND", "OR", "NOT", "BETWEEN", "IN"];

    /// <summary>
    /// The functions of a condition, which count in this case only. They repeat <c>ExpressionFunction.All</c> of the generator, for the same reason as <see cref="ConditionKeywords"/>
    /// and under the same test. The functions of an update are left out: this list serves the check of a condition.
    /// </summary>
    internal static readonly string[] ConditionFunctions = ["size", "attribute_exists", "attribute_not_exists", "attribute_type", "begins_with", "contains"];

    private const int FirstRentLength = 256;
    private const int FirstSliceCapacity = 16;

    private readonly PooledByteBuffer[] _clauses;
    private readonly PooledByteBuffer _names;
    private readonly PooledByteBuffer _values;
    private readonly PooledByteBuffer _pending;
    private Slice[] _nameSlices = new Slice[FirstSliceCapacity];
    private Slice[] _valueSlices = new Slice[FirstSliceCapacity];
    private int _nameCount;
    private int _valueCount;

    // The clauses, one bit each, that are a chain of conditions already: (a) AND (b).
    private int _chains;

    /// <summary>Initializes a new instance of the <see cref="ExpressionWriter"/> class.</summary>
    /// <param name="pool">The pool of the arrays, which are rented when they are first written to.</param>
    public ExpressionWriter(ArrayPool<byte> pool)
    {
        _names = new PooledByteBuffer(pool, FirstRentLength);
        _values = new PooledByteBuffer(pool, FirstRentLength);
        _pending = new PooledByteBuffer(pool, FirstRentLength);
        _clauses = new PooledByteBuffer[(int)Clause.Projection + 1];
        for (var i = 0; i < _clauses.Length; i++)
        {
            _clauses[i] = new PooledByteBuffer(pool, FirstRentLength);
        }

        ValueJson = WireJson.CreateWriter(_values);
    }

    // The writer of values that take the long way: it is clean before each value and after it.
    private Utf8JsonWriter ValueJson { get; }

    /// <summary>Gets the text of a clause.</summary>
    /// <param name="clause">The clause.</param>
    /// <returns>The buffer.</returns>
    public PooledByteBuffer this[Clause clause] => _clauses[(int)clause];

    /// <summary>Writes a document path as placeholders, reusing the placeholder of a name that occurred before.</summary>
    /// <remarks>
    /// <c>address.city</c> becomes <c>#n0.#n1</c>, <c>lines[2].sku</c> becomes <c>#n2[2].#n3</c>, <c>matrix[0][1]</c> becomes <c>#n4[0][1]</c>.
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
    public void AppendAttributeName(PooledByteBuffer text, string name) => AppendName(text, name);

    /// <summary>Adds <c>attribute_exists</c> or <c>attribute_not_exists</c> to the condition, which on the partition key says that an item exists, and on a version that an item has none.</summary>
    /// <param name="attributeName">The attribute name, of the partition key or of a version.</param>
    /// <param name="exists">Whether the attribute must exist.</param>
    public void AppendItemExists(string attributeName, bool exists)
    {
        var text = StartPending();
        text.Append(exists ? "attribute_exists("u8 : "attribute_not_exists("u8);
        AppendAttributeName(text, attributeName);
        text.Append((byte)')');
        CommitPending(Clause.Condition);
    }

    /// <summary>Adds <c>name = :old</c> to the condition. Version 0 never comes here: it stands for an item that does not exist, or for what reads as 0, see <see cref="AppendVersionZero"/> and <see cref="WriteRules"/>.</summary>
    /// <param name="attributeName">The attribute of the version.</param>
    /// <param name="version">The version the item must have.</param>
    public void AppendVersion(string attributeName, long version)
    {
        var text = StartPending();
        AppendAttributeName(text, attributeName);
        text.Append(" = "u8);
        AppendValue<long, NumberConverter<long>>(text, version);
        CommitPending(Clause.Condition);
    }

    /// <summary>
    /// Adds <c>(attribute_not_exists(name) OR name = :zero)</c> to the condition, for an entity that allows an item without a version: what such an item reads as 0 is either that item, one that holds a 0 of its own, or no
    /// item at all. It is one group, so it needs none around it beside another condition.
    /// </summary>
    /// <param name="attributeName">The attribute of the version.</param>
    public void AppendVersionZero(string attributeName)
    {
        var text = StartPending();
        text.Append("(attribute_not_exists("u8);
        AppendAttributeName(text, attributeName);
        text.Append(") OR "u8);
        AppendAttributeName(text, attributeName);
        text.Append(" = "u8);
        AppendValue<long, NumberConverter<long>>(text, 0);
        text.Append((byte)')');
        CommitPending(Clause.Condition);
    }

    /// <summary>
    /// Adds <c>name = value</c> to the key condition for every attribute of a key, which is how a query asks for the one item under it. The key is JSON already, and an attribute is a name
    /// and a value in the form a map keeps them in, so they are copied, and a key costs no string here either.
    /// </summary>
    /// <param name="key">The key, as a JSON object.</param>
    public void AppendKeyCondition(ReadOnlySpan<byte> key)
    {
        // Key:  {"pk":{"S":"a"},"sk":{"S":"b"}}
        // Text: #n0 = :v0 AND #n1 = :v1
        var text = this[Clause.KeyCondition];
        var reader = new Utf8JsonReader(key);
        reader.Read();
        reader.Read();
        while (reader.TokenType == JsonTokenType.PropertyName)
        {
            if (text.WrittenCount > 0)
            {
                text.Append(" AND "u8);
            }

            AppendEncodedName(text, reader.ValueSpan);
            text.Append(" = "u8);
            reader.Read();
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            AppendEncodedValue(text, key[start..(int)reader.BytesConsumed]);
            reader.Read();
        }
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
        TConverter.Write(new DynamoDbWriter(ValueJson, _values), value);
        CommandState.EndWrite(ValueJson);
        EndValue(text, mark, start);
    }

    /// <summary>
    /// Adds the value of <c>:pk</c>, the partition of a fan-out query, as the last value of a copy of its request. It has a placeholder outside the numbering, so that no other value
    /// can come to share it through deduplication: it differs per partition, and they do not.
    /// </summary>
    /// <param name="value">The partition key.</param>
    public void AddPartitionKey(AttributeValue value)
    {
        // ,":pk":{"S":"a"}
        var values = _values;
        values.Append(_valueCount == 0 ? "{\":pk\":"u8 : ",\":pk\":"u8);
        AttributeValueConverter.Write(new DynamoDbWriter(ValueJson, values), value);
        CommandState.EndWrite(ValueJson);
    }

    /// <summary>Clears the segment an expression lands in first. An interpolated expression needs it, because its handler runs before the method it belongs to.</summary>
    /// <returns>The segment.</returns>
    public PooledByteBuffer StartPending()
    {
        _pending.Truncate(0);
        return _pending;
    }

    /// <summary>Adds a condition to a clause. A second condition means both must hold.</summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="clause">The clause.</param>
    /// <param name="condition">The condition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    public void AppendCondition<TCondition>(Clause clause, TCondition condition)
        where TCondition : ICondition
    {
        Argument.ThrowIfNull(condition);

        var sink = new ExpressionSink(this, StartPending());
        condition.Render(ref sink);
        CommitPending(clause);
    }

    /// <summary>Adds what is pending to a clause. A second condition means both must hold.</summary>
    /// <param name="clause">The clause.</param>
    /// <exception cref="ArgumentException">The expression names no attribute, so a name in a hole is missing its <c>Attr(...)</c>.</exception>
    public void CommitPending(Clause clause)
    {
        if (!NamesAnAttribute(_pending.WrittenSpan))
        {
            throw new ArgumentException("The expression names no attribute, so it compares values only and DynamoDB would accept it as a constant. A string in a hole is a value: write a name as Attr(\"name\").");
        }

        var text = this[clause];
        if (text.WrittenCount == 0)
        {
            text.Append(_pending.WrittenSpan);
        }
        else
        {
            // (a) AND (b), so that neither side can change what the other means. The first condition goes without, and that is most of them.
            // A side that is one group already stays as it is, because DynamoDB rejects redundant parentheses. A chain goes on as it is: (a) AND (b) AND (c).
            var chain = 1 << (int)clause;
            if ((_chains & chain) == 0)
            {
                _chains |= chain;
                if (!IsOneGroup(text.WrittenSpan))
                {
                    text.Prepend((byte)'(');
                    text.Append((byte)')');
                }
            }

            text.Append(" AND "u8);
            var pending = _pending.WrittenSpan;
            if (IsOneGroup(pending))
            {
                text.Append(pending);
            }
            else
            {
                text.Append((byte)'(');
                text.Append(pending);
                text.Append((byte)')');
            }
        }
    }

    /// <summary>Starts or continues the <c>SET</c> clause with what is pending, a raw action.</summary>
    public void CommitPendingSet() => StartAction(Clause.Set, "SET "u8).Append(_pending.WrittenSpan);

    /// <summary>Adds <c>path = value</c> to the <c>SET</c> clause.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="path">The path.</param>
    /// <param name="value">The value.</param>
    public void Set<T, TConverter>(string path, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var text = StartAction(Clause.Set, "SET "u8);
        AppendPath(text, path);
        text.Append(" = "u8);
        AppendValue<T, TConverter>(text, value);
    }

    /// <summary>Adds <c>name = value</c> to the <c>SET</c> clause, for a name that comes from an entity and is taken as it is.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="value">The value.</param>
    public void SetAttribute<T, TConverter>(string attributeName, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var text = StartAction(Clause.Set, "SET "u8);
        AppendAttributeName(text, attributeName);
        text.Append(" = "u8);
        AppendValue<T, TConverter>(text, value);
    }

    /// <summary>
    /// Adds <c>name = source</c> to the <c>SET</c> clause, which copies an attribute of the item, for names that come from an entity and are taken as they are. An action of the update on the attribute takes
    /// precedence, as in <see cref="SetMissingAttribute"/>, which also keeps two indexes that share the attribute from copying it twice.
    /// </summary>
    /// <param name="attributeName">The attribute that is written.</param>
    /// <param name="sourceName">The attribute of the item whose value it takes.</param>
    public void CopyAttribute(string attributeName, string sourceName)
    {
        if (!HasActionOnAttribute(attributeName))
        {
            var text = StartAction(Clause.Set, "SET "u8);
            AppendAttributeName(text, attributeName);
            text.Append(" = "u8);
            AppendAttributeName(text, sourceName);
        }
    }

    /// <summary>
    /// Adds <c>name = if_not_exists(name, value)</c> to the <c>SET</c> clause, unless an action of the update already changes the attribute, because DynamoDB rejects two actions on one path.
    /// A name that only a condition or the right side of an action mentions is no such action. It runs last, so that it sees them all.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="value">The value.</param>
    public void SetMissingAttribute<T, TConverter>(string attributeName, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        if (!HasActionOnAttribute(attributeName))
        {
            var text = StartAction(Clause.Set, "SET "u8);
            AppendAttributeName(text, attributeName);
            text.Append(" = if_not_exists("u8);
            AppendAttributeName(text, attributeName);
            text.Append(", "u8);
            AppendValue<T, TConverter>(text, value);
            text.Append((byte)')');
        }
    }

    /// <summary>Adds <c>path = if_not_exists(path, value)</c> to the <c>SET</c> clause.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="path">The path.</param>
    /// <param name="value">The value.</param>
    public void SetIfNotExists<T, TConverter>(string path, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var text = StartAction(Clause.Set, "SET "u8);
        AppendPath(text, path);
        text.Append(" = if_not_exists("u8);
        AppendPath(text, path);
        text.Append(", "u8);
        AppendValue<T, TConverter>(text, value);
        text.Append((byte)')');
    }

    /// <summary>Adds <c>path value</c> to the <c>ADD</c> clause.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="path">The path.</param>
    /// <param name="value">The value.</param>
    public void Add<T, TConverter>(string path, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var text = StartAction(Clause.Add, "ADD "u8);
        AppendPath(text, path);
        text.Append((byte)' ');
        AppendValue<T, TConverter>(text, value);
    }

    /// <summary>Adds <c>path value</c> to the <c>DELETE</c> clause.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="path">The path.</param>
    /// <param name="value">The value.</param>
    public void Delete<T, TConverter>(string path, T value)
        where TConverter : IDynamoDbConverter<T>
    {
        var text = StartAction(Clause.Delete, "DELETE "u8);
        AppendPath(text, path);
        text.Append((byte)' ');
        AppendValue<T, TConverter>(text, value);
    }

    /// <summary>Adds a path to the <c>REMOVE</c> clause.</summary>
    /// <param name="path">The path.</param>
    public void Remove(string path) => AppendPath(StartAction(Clause.Remove, "REMOVE "u8), path);

    /// <summary>Adds a name to the <c>REMOVE</c> clause, for a name that comes from an entity and is taken as it is.</summary>
    /// <param name="attributeName">The attribute name.</param>
    public void RemoveAttribute(string attributeName) => AppendAttributeName(StartAction(Clause.Remove, "REMOVE "u8), attributeName);

    /// <summary>Adds paths to the projection. A path that is in it already is left out, because DynamoDB rejects a projection that names a path twice.</summary>
    /// <param name="paths">The paths.</param>
    public void Project(ReadOnlySpan<string> paths)
    {
        var text = this[Clause.Projection];
        foreach (var path in paths)
        {
            // A name has one placeholder, so the same path is the same text: it is written, and taken back if an earlier path reads the same.
            var mark = text.WrittenCount;
            if (mark > 0)
            {
                text.Append(", "u8);
            }

            var start = text.WrittenCount;
            AppendPath(text, path);
            if (mark > 0 && HasPath(text.WrittenSpan[..mark], text.WrittenSpan[start..]))
            {
                text.Truncate(mark);
            }
        }
    }

    /// <summary>Adds attributes to the projection.</summary>
    /// <param name="attributes">The attributes.</param>
    public void Project(ReadOnlySpan<Attr> attributes)
    {
        foreach (ref readonly var attribute in attributes)
        {
            var path = attribute.Path;
            Project(new ReadOnlySpan<string>(in path));
        }
    }

    /// <summary>
    /// Adds a name to a projection that has none of it yet: what a typed read cannot do without. A path that starts with the name counts, because
    /// DynamoDB rejects two paths that overlap.
    /// </summary>
    /// <param name="name">The attribute name, taken as it is.</param>
    public void AddToProjection(string name)
    {
        var text = this[Clause.Projection];
        var candidate = StartPending();
        AppendAttributeName(candidate, name);
        if (!HasRoot(text.WrittenSpan, candidate.WrittenSpan))
        {
            text.Append(", "u8);
            text.Append(candidate.WrittenSpan);
        }
    }

    /// <summary>Adds names to the projection, if there is one: a read without a projection gets every attribute anyway.</summary>
    /// <param name="names">The attribute names, see <see cref="AddToProjection"/>.</param>
    public void CompleteProjection(ReadOnlySpan<string> names)
    {
        if (this[Clause.Projection].WrittenCount > 0)
        {
            foreach (var name in names)
            {
                AddToProjection(name);
            }
        }
    }

    /// <summary>Copies everything another writer holds into this empty one.</summary>
    /// <param name="other">The writer to copy, which stays as it is.</param>
    public void CopyFrom(ExpressionWriter other)
    {
        _names.Append(other._names.WrittenSpan);
        _values.Append(other._values.WrittenSpan);
        _chains = other._chains;
        for (var i = 0; i < _clauses.Length; i++)
        {
            _clauses[i].Append(other._clauses[i].WrittenSpan);
        }

        for (var i = 0; i < other._nameCount; i++)
        {
            Add(ref _nameSlices, ref _nameCount, other._nameSlices[i]);
        }

        for (var i = 0; i < other._valueCount; i++)
        {
            Add(ref _valueSlices, ref _valueCount, other._valueSlices[i]);
        }
    }

    /// <summary>Writes the expressions as members of the request. It changes nothing here, so it can be repeated.</summary>
    /// <param name="body">The request body, with at least one member before this.</param>
    public void WriteMembers(PooledByteBuffer body)
    {
        WriteUpdateExpression(body);
        WriteClause(body, ",\"ConditionExpression\":\""u8, Clause.Condition);
        WriteClause(body, ",\"KeyConditionExpression\":\""u8, Clause.KeyCondition);
        WriteClause(body, ",\"FilterExpression\":\""u8, Clause.Filter);
        WriteClause(body, ",\"ProjectionExpression\":\""u8, Clause.Projection);
        WriteMap(body, ",\"ExpressionAttributeNames\":"u8, _names);
        WriteMap(body, ",\"ExpressionAttributeValues\":"u8, _values);
    }

    /// <summary>
    /// Writes the projection with its names in place of the placeholders, as a request would have it, for the span of a read. The projection
    /// names attributes and holds no values.
    /// </summary>
    /// <returns>The paths, or <see langword="null"/> if there is no projection.</returns>
    public string? ResolveProjection()
    {
        // #n0, #n1.#n2[3]
        var rest = this[Clause.Projection].WrittenSpan;
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
            paths.Append(NameAt(int.Parse(rest[..digits], CultureInfo.InvariantCulture)));
            rest = rest[digits..];
        }
    }

    /// <summary>
    /// Writes the key condition with each name and value in place of its placeholder, for the signature of a continuation token, which must hold for this condition and no other, whatever
    /// the numbering. A name keeps its quotes and a value is JSON, so neither runs into what follows. <c>:pk</c>, the partition of a fan-out, has no value here and stays as it is.
    /// </summary>
    /// <param name="destination">The buffer.</param>
    public void WriteResolvedKeyCondition(PooledByteBuffer destination)
    {
        // Text:     #n0 = :v0 AND begins_with(#n1, :v1)
        // Written:  #"pk" = :{"S":"a"} AND begins_with(#"sk", :{"S":"b"})
        var rest = this[Clause.KeyCondition].WrittenSpan;
        while (true)
        {
            var marker = rest.IndexOfAny((byte)'#', (byte)':');
            if (marker < 0)
            {
                destination.Append(rest);
                return;
            }

            var isName = rest[marker] == (byte)'#';
            destination.Append(rest[..(marker + 1)]);
            rest = rest[(marker + 1)..];
            if (rest[0] == (byte)'p')
            {
                continue;
            }

            rest = rest[1..];
            var digits = rest.IndexOfAnyExceptInRange((byte)'0', (byte)'9');
            digits = digits < 0 ? rest.Length : digits;
            var index = int.Parse(rest[..digits], CultureInfo.InvariantCulture);
            if (isName)
            {
                var slice = _nameSlices[index];
                destination.Append(_names.WrittenSpan.Slice(slice.Offset - 1, slice.Length + 2));
            }
            else
            {
                var slice = _valueSlices[index];
                destination.Append(_values.WrittenSpan.Slice(slice.Offset, slice.Length));
            }

            rest = rest[digits..];
        }
    }

    /// <summary>Empties all segments for the next set of expressions. The arrays stay, unless they grew beyond what an idle object should hold.</summary>
    /// <param name="maxRetainedLength">The longest array a segment keeps.</param>
    public void Clear(int maxRetainedLength)
    {
        _names.Clear(maxRetainedLength);
        _values.Clear(maxRetainedLength);
        ValueJson.Reset();
        _pending.Clear(maxRetainedLength);
        foreach (var clause in _clauses)
        {
            clause.Clear(maxRetainedLength);
        }

        _nameCount = 0;
        _valueCount = 0;
        _chains = 0;
    }

    /// <summary>Gives all arrays back to the pool.</summary>
    public void Release()
    {
        Clear(maxRetainedLength: 0);
    }

    // The lexer of the generator repeats this for text a compiler sees, and ExpressionWriterTests keeps the two in step. Unlike there, a byte from 0x80 counts here: the
    // text is UTF-8, so that is part of a name that is not ASCII, and it is for DynamoDB to reject that name, not for this check to call it no name at all.
    internal static bool IsWordByte(byte value) => value >= 0x80 || value == (byte)'_' || char.IsAsciiLetterOrDigit((char)value);

    // A generated name is a #n placeholder and only a name is. Without one, what is left to look for is a name in the literal text of an expression.
    private static bool NamesAnAttribute(ReadOnlySpan<byte> text)
    {
        if (text.Contains((byte)'#'))
        {
            return true;
        }

        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordByte(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && IsWordByte(text[i]))
            {
                i++;
            }

            var isPlaceholder = start > 0 && text[start - 1] == (byte)':';
            if (!isPlaceholder && !IsExpressionWord(text[start..i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExpressionWord(ReadOnlySpan<byte> word)
    {
        foreach (var keyword in ConditionKeywords)
        {
            if (Ascii.EqualsIgnoreCase(word, keyword))
            {
                return true;
            }
        }

        foreach (var function in ConditionFunctions)
        {
            if (Ascii.Equals(word, function))
            {
                return true;
            }
        }

        return false;
    }

    private static int Find(Slice[] slices, int count, PooledByteBuffer map, int start)
    {
        var written = map.WrittenSpan;
        var candidate = written[start..];
        for (var i = 0; i < count; i++)
        {
            var slice = slices[i];
            if (slice.Length == candidate.Length && written.Slice(slice.Offset, slice.Length).SequenceEqual(candidate))
            {
                return i;
            }
        }

        return -1;
    }

    // Finds whether an action of a clause changes a name, which is what its path starts with. Raw actions can spell a name out, and anything after the path of an action only reads.
    // Clause: SET #n0.#n1 = list_append(#n2, :v0), total = :v1
    // Paths:  #n0, total. #n1 and #n2 are not looked at.
    private static bool HasActionOn(ReadOnlySpan<byte> clause, ReadOnlySpan<byte> placeholder, ReadOnlySpan<byte> name)
    {
        var depth = 0;
        var startsAction = true;
        var i = clause.IndexOf((byte)' ') + 1;
        while (i < clause.Length)
        {
            var value = clause[i];
            if (startsAction && (value == (byte)'#' || IsWordByte(value)))
            {
                var start = i++;
                while (i < clause.Length && IsWordByte(clause[i]))
                {
                    i++;
                }

                var path = clause[start..i];
                if (path.SequenceEqual(placeholder) || path.SequenceEqual(name))
                {
                    return true;
                }

                startsAction = false;
                continue;
            }

            depth += value == (byte)'(' ? 1 : value == (byte)')' ? -1 : 0;
            startsAction |= depth == 0 && value == (byte)',';
            i++;
        }

        return false;
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

    private static void Add(ref Slice[] slices, ref int count, Slice slice)
    {
        if (count == slices.Length)
        {
            Array.Resize(ref slices, count * 2);
        }

        slices[count++] = slice;
    }

    // A map is kept without its closing brace, so that the next name or value can be added.
    private static void WriteMap(PooledByteBuffer body, ReadOnlySpan<byte> member, PooledByteBuffer map)
    {
        if (map.WrittenCount > 0)
        {
            body.Append(member);
            body.Append(map.WrittenSpan);
            body.Append((byte)'}');
        }
    }

    // True for (a AND b), false for (a) AND (b) and for size(a) < 1. Names and values are placeholders here, so every parenthesis is structure.
    private static bool IsOneGroup(ReadOnlySpan<byte> text)
    {
        var depth = 0;
        for (var i = 0; i + 1 < text.Length; i++)
        {
            depth += text[i] == (byte)'(' ? 1 : text[i] == (byte)')' ? -1 : 0;
            if (depth == 0)
            {
                return false;
            }
        }

        return text.EndsWith(")"u8);
    }

    private static ArgumentException InvalidPath(string path) =>
        new($"The attribute path \"{path}\" is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c.", nameof(path));

    private void WriteClause(PooledByteBuffer body, ReadOnlySpan<byte> member, Clause clause)
    {
        var text = this[clause];
        if (text.WrittenCount > 0)
        {
            body.Append(member);
            body.Append(text.WrittenSpan);
            body.Append((byte)'"');
        }
    }

    private void WriteUpdateExpression(PooledByteBuffer body)
    {
        var first = true;
        for (var clause = Clause.Set; clause <= Clause.Delete; clause++)
        {
            var text = this[clause];
            if (text.WrittenCount > 0)
            {
                body.Append(first ? ",\"UpdateExpression\":\""u8 : " "u8);
                body.Append(text.WrittenSpan);
                first = false;
            }
        }

        if (!first)
        {
            body.Append((byte)'"');
        }
    }

    private PooledByteBuffer StartAction(Clause clause, ReadOnlySpan<byte> keyword)
    {
        var text = this[clause];
        text.Append(text.WrittenCount == 0 ? keyword : ", "u8);
        return text;
    }

    private bool HasActionOnAttribute(string attributeName)
    {
        // The candidate is written to the end of the map and taken back, as in AppendName.
        var names = _names;
        var mark = names.WrittenCount;
        WireJson.AppendEncoded(names, attributeName);
        var placeholder = StartPending();
        var index = Find(_nameSlices, _nameCount, names, mark);
        if (index >= 0)
        {
            placeholder.Append("#n"u8);
            placeholder.AppendNumber(index);
        }

        var isChanged = false;
        for (var clause = Clause.Set; clause <= Clause.Delete; clause++)
        {
            isChanged |= HasActionOn(this[clause].WrittenSpan, placeholder.WrittenSpan, names.WrittenSpan[mark..]);
        }

        names.Truncate(mark);
        return isChanged;
    }

    private void AppendName(PooledByteBuffer text, ReadOnlySpan<char> name)
    {
        var mark = _names.WrittenCount;
        var start = StartName();
        WireJson.AppendEncoded(_names, name);
        EndName(text, mark, start);
    }

    private void AppendEncodedName(PooledByteBuffer text, ReadOnlySpan<byte> encoded)
    {
        var mark = _names.WrittenCount;
        var start = StartName();
        _names.Append(encoded);
        EndName(text, mark, start);
    }

    // ,"#n3":"status"
    private int StartName()
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
        var index = Find(_nameSlices, _nameCount, names, start);
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

    private void AppendEncodedValue(PooledByteBuffer text, ReadOnlySpan<byte> json)
    {
        var mark = _values.WrittenCount;
        var start = StartValue();
        _values.Append(json);
        EndValue(text, mark, start);
    }

    // ,":v3":{"S":"open"}
    private int StartValue()
    {
        var values = _values;
        values.Append(_valueCount == 0 ? "{\":v"u8 : ",\":v"u8);
        values.AppendNumber(_valueCount);
        values.Append("\":"u8);
        return values.WrittenCount;
    }

    private void EndValue(PooledByteBuffer text, int mark, int start)
    {
        var values = _values;
        var index = Find(_valueSlices, _valueCount, values, start);
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

    // The slice sits between its quotes, so it is a JSON string that a reader can unescape.
    private string NameAt(int index)
    {
        var slice = _nameSlices[index];
        var reader = new Utf8JsonReader(_names.WrittenSpan.Slice(slice.Offset - 1, slice.Length + 2));
        reader.Read();
        return reader.GetString()!;
    }

    private readonly record struct Slice(int Offset, int Length);
}
