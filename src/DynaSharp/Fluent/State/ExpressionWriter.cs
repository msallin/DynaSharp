using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Expressions;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The expressions of one request while they are written: the text of each clause, which one fluent call feeds together with the maps of names and values, although they are separate JSON members.
/// Everything is UTF-8 that is already encoded for a JSON string, so putting the members together at the end is a copy.
/// </summary>
/// <remarks>
/// <para>
/// This type assembles clauses: it joins conditions, starts and continues the actions of an update and finds whether an action changes an attribute already. The placeholders are
/// <see cref="PlaceholderMaps"/>, the projection is <see cref="ProjectionList"/>, and the check that an expression names an attribute is <see cref="ExpressionWords"/>.
/// </para>
/// <para>A transaction writes several sets of expressions with one writer: <see cref="WriteMembers"/> and <see cref="Clear"/> for each action.</para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A pooled object: Release gives the arrays back, and the object lives on.")]
internal sealed class ExpressionWriter
{
    private const int FirstRentLength = 256;

    private readonly PooledByteBuffer[] _clauses;
    private readonly PooledByteBuffer _pending;
    private PlaceholderMaps _placeholders;

    // The clauses, one bit each, that are a chain of conditions already: (a) AND (b).
    private int _chains;

    /// <summary>Initializes a new instance of the <see cref="ExpressionWriter"/> class.</summary>
    /// <param name="pool">The pool of the arrays, which are rented when they are first written to.</param>
    public ExpressionWriter(ArrayPool<byte> pool)
    {
        _placeholders = new PlaceholderMaps(pool);
        _pending = new PooledByteBuffer(pool, FirstRentLength);
        _clauses = new PooledByteBuffer[(int)Clause.Projection + 1];
        for (var i = 0; i < _clauses.Length; i++)
        {
            _clauses[i] = new PooledByteBuffer(pool, FirstRentLength);
        }
    }

    /// <summary>Gets the maps of names and values, by reference, for a part of an expression that a caller writes itself.</summary>
    public ref PlaceholderMaps Placeholders => ref _placeholders;

    /// <summary>Gets the projection, over the clause of the projection and the maps of this writer.</summary>
    public ProjectionList Projection => new(this[Clause.Projection], ref _placeholders, _pending);

    /// <summary>Gets the text of a clause.</summary>
    /// <param name="clause">The clause.</param>
    /// <returns>The buffer.</returns>
    public PooledByteBuffer this[Clause clause] => _clauses[(int)clause];

    /// <summary>Adds <c>attribute_exists</c> or <c>attribute_not_exists</c> to the condition, which on the partition key says that an item exists, and on a version that an item has none.</summary>
    /// <param name="attributeName">The attribute name, of the partition key or of a version.</param>
    /// <param name="exists">Whether the attribute must exist.</param>
    public void AppendItemExists(string attributeName, bool exists)
    {
        var text = StartPending();
        text.Append(exists ? "attribute_exists("u8 : "attribute_not_exists("u8);
        _placeholders.AppendName(text, attributeName);
        text.Append((byte)')');
        CommitPending(Clause.Condition);
    }

    /// <summary>Adds <c>name = :old</c> to the condition. Version 0 never comes here: it stands for an item that does not exist, or for what reads as 0, see <see cref="AppendVersionZero"/> and <see cref="WriteRules"/>.</summary>
    /// <param name="attributeName">The attribute of the version.</param>
    /// <param name="version">The version the item must have.</param>
    public void AppendVersion(string attributeName, long version)
    {
        var text = StartPending();
        _placeholders.AppendName(text, attributeName);
        text.Append(" = "u8);
        _placeholders.AppendValue<long, NumberConverter<long>>(text, version);
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
        _placeholders.AppendName(text, attributeName);
        text.Append(") OR "u8);
        _placeholders.AppendName(text, attributeName);
        text.Append(" = "u8);
        _placeholders.AppendValue<long, NumberConverter<long>>(text, 0);
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

            _placeholders.AppendEncodedName(text, reader.ValueSpan);
            text.Append(" = "u8);
            reader.Read();
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            _placeholders.AppendEncodedValue(text, key[start..(int)reader.BytesConsumed]);
            reader.Read();
        }
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
        if (!ExpressionWords.NamesAnAttribute(_pending.WrittenSpan))
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
        _placeholders.AppendPath(text, path);
        text.Append(" = "u8);
        _placeholders.AppendValue<T, TConverter>(text, value);
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
        _placeholders.AppendName(text, attributeName);
        text.Append(" = "u8);
        _placeholders.AppendValue<T, TConverter>(text, value);
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
            _placeholders.AppendName(text, attributeName);
            text.Append(" = "u8);
            _placeholders.AppendName(text, sourceName);
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
            _placeholders.AppendName(text, attributeName);
            text.Append(" = if_not_exists("u8);
            _placeholders.AppendName(text, attributeName);
            text.Append(", "u8);
            _placeholders.AppendValue<T, TConverter>(text, value);
            text.Append((byte)')');
        }
    }

    /// <summary>
    /// Adds <c>name = if_not_exists(name, :zero) + :one</c> to the <c>SET</c> clause, the next version of an update that does not know the one it advances from: an item without a version reads as 0, so it
    /// gets 1, as a first checked write stores. An action of the update on the attribute takes precedence, as in <see cref="SetMissingAttribute"/>. It runs last, so that it sees them all.
    /// </summary>
    /// <param name="attributeName">The attribute of the version.</param>
    public void AdvanceMissingVersion(string attributeName)
    {
        if (!HasActionOnAttribute(attributeName))
        {
            var text = StartAction(Clause.Set, "SET "u8);
            _placeholders.AppendName(text, attributeName);
            text.Append(" = if_not_exists("u8);
            _placeholders.AppendName(text, attributeName);
            text.Append(", "u8);
            _placeholders.AppendValue<long, NumberConverter<long>>(text, 0);
            text.Append(") + "u8);
            _placeholders.AppendValue<long, NumberConverter<long>>(text, 1);
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
        _placeholders.AppendPath(text, path);
        text.Append(" = if_not_exists("u8);
        _placeholders.AppendPath(text, path);
        text.Append(", "u8);
        _placeholders.AppendValue<T, TConverter>(text, value);
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
        _placeholders.AppendPath(text, path);
        text.Append((byte)' ');
        _placeholders.AppendValue<T, TConverter>(text, value);
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
        _placeholders.AppendPath(text, path);
        text.Append((byte)' ');
        _placeholders.AppendValue<T, TConverter>(text, value);
    }

    /// <summary>Adds a path to the <c>REMOVE</c> clause.</summary>
    /// <param name="path">The path.</param>
    public void Remove(string path) => _placeholders.AppendPath(StartAction(Clause.Remove, "REMOVE "u8), path);

    /// <summary>Adds a name to the <c>REMOVE</c> clause, for a name that comes from an entity and is taken as it is.</summary>
    /// <param name="attributeName">The attribute name.</param>
    public void RemoveAttribute(string attributeName) => _placeholders.AppendName(StartAction(Clause.Remove, "REMOVE "u8), attributeName);

    /// <summary>Copies everything another writer holds into this empty one.</summary>
    /// <param name="other">The writer to copy, which stays as it is.</param>
    public void CopyFrom(ExpressionWriter other)
    {
        _placeholders.CopyFrom(in other._placeholders);
        _chains = other._chains;
        for (var i = 0; i < _clauses.Length; i++)
        {
            _clauses[i].Append(other._clauses[i].WrittenSpan);
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
        _placeholders.WriteMembers(body);
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
            destination.Append(isName ? _placeholders.QuotedNameAt(index) : _placeholders.ValueAt(index));
            rest = rest[digits..];
        }
    }

    /// <summary>Empties all segments for the next set of expressions. The arrays stay, unless they grew beyond what an idle object should hold.</summary>
    /// <param name="maxRetainedLength">The longest array a segment keeps.</param>
    public void Clear(int maxRetainedLength)
    {
        _placeholders.Clear(maxRetainedLength);
        _pending.Clear(maxRetainedLength);
        foreach (var clause in _clauses)
        {
            clause.Clear(maxRetainedLength);
        }

        _chains = 0;
    }

    /// <summary>Gives all arrays back to the pool.</summary>
    public void Release()
    {
        Clear(maxRetainedLength: 0);
    }

    // Finds whether an action of a clause changes a name, which is what its path starts with. Raw actions can spell a name out, and anything after the path of an action only reads.
    // A path that starts with # is a placeholder and never a name spelled out, so an attribute named "#n0" is not the one that placeholder stands for.
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
            if (startsAction && (value == (byte)'#' || ExpressionWords.IsWordByte(value)))
            {
                var start = i++;
                while (i < clause.Length && ExpressionWords.IsWordByte(clause[i]))
                {
                    i++;
                }

                var path = clause[start..i];
                if (path.SequenceEqual(placeholder) || (value != (byte)'#' && path.SequenceEqual(name)))
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
        // The scratch holds the name as a map holds it, then its placeholder if it has one: status, then #n3.
        var scratch = StartPending();
        WireJson.AppendEncoded(scratch, attributeName);
        var nameLength = scratch.WrittenCount;
        var index = _placeholders.IndexOfName(scratch.WrittenSpan);
        if (index >= 0)
        {
            scratch.Append("#n"u8);
            scratch.AppendNumber(index);
        }

        var name = scratch.WrittenSpan[..nameLength];
        var placeholder = scratch.WrittenSpan[nameLength..];
        var isChanged = false;
        for (var clause = Clause.Set; clause <= Clause.Delete; clause++)
        {
            isChanged |= HasActionOn(this[clause].WrittenSpan, placeholder, name);
        }

        return isChanged;
    }
}
