using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace DynaSharp.Serialization;

/// <summary>
/// Walks one value of plain JSON and writes it as one attribute value, checking on the way what DynamoDB would refuse: a number beyond its limits, more than 32 levels, an empty name, a name
/// twice in an object, and text that is not text. Each failure names its place in the JSON, such as <c>$.lines[2].weight</c>, and never a value.
/// </summary>
/// <remarks>
/// The names of the open objects are kept, unescaped, one after the other in a pooled buffer, with a hash each, so a name is checked against those of its object with a vectorized search of
/// the hashes, and the path of a failure is built from them. A name with a hash seen before is compared byte by byte.
/// </remarks>
internal ref struct JsonValueWriter
{
    private const int FirstNamesLength = 256;

    private const int FirstEntryCount = 16;

    // What a name of a path is made of when it needs no quotes, after a first character that is a letter or an underscore.
    private static readonly SearchValues<char> PlainNameCharacters = SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");

    private readonly DynamoDbWriter _writer;
    private readonly Span<Frame> _frames;
    private readonly string _paramName;
    private Utf8JsonReader _json;
    private byte[] _names;
    private int _namesLength;

    // Per name: its hash, and where it starts and ends in _names.
    private int[] _hashes;
    private int[] _ends;
    private int _entries;
    private int _level;

    /// <summary>Initializes a new instance of the <see cref="JsonValueWriter"/> struct.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="utf8Json">The JSON.</param>
    /// <param name="options">The settings of the reader.</param>
    /// <param name="frames">Room for <see cref="JsonBridge.MaxLevels"/> open arrays and objects.</param>
    /// <param name="paramName">The parameter that holds the JSON.</param>
    public JsonValueWriter(DynamoDbWriter writer, ReadOnlySpan<byte> utf8Json, JsonReaderOptions options, Span<Frame> frames, string paramName)
    {
        _writer = writer;
        _frames = frames;
        _paramName = paramName;
        _json = new Utf8JsonReader(utf8Json, options);
        _names = ArrayPool<byte>.Shared.Rent(FirstNamesLength);
        _hashes = ArrayPool<int>.Shared.Rent(FirstEntryCount);
        _ends = ArrayPool<int>.Shared.Rent(FirstEntryCount);
    }

    /// <summary>Writes the value the JSON holds, which must be all of it.</summary>
    public void WriteDocument()
    {
        // With the whole input at hand, Read throws for no value, a value cut short and anything after it, so its result is true here and false at the end.
        _ = _json.Read();
        WriteValue();
        _ = _json.Read();
    }

    /// <summary>Gives the buffers back to the pool.</summary>
    public readonly void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_names);
        ArrayPool<int>.Shared.Return(_hashes);
        ArrayPool<int>.Shared.Return(_ends);
    }

    private static void AppendName(StringBuilder path, ReadOnlySpan<byte> name)
    {
        var text = Encoding.UTF8.GetString(name);
        if (text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && !text.AsSpan().ContainsAnyExcept(PlainNameCharacters))
        {
            path.Append('.').Append(text);
        }
        else
        {
            path.Append("['").Append(text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)).Append("']");
        }
    }

    // FNV-1a: the same in every process, so a test can give two names one hash ("costarring" and "liquid") and reach the comparison of the bytes.
    private static int Hash(ReadOnlySpan<byte> name)
    {
        var hash = 2166136261u;
        foreach (var value in name)
        {
            hash = unchecked((hash ^ value) * 16777619u);
        }

        return unchecked((int)hash);
    }

    private static int NextIndexOf(ReadOnlySpan<int> hashes, int after, int hash)
    {
        var next = hashes[(after + 1)..].IndexOf(hash);
        return next < 0 ? -1 : after + 1 + next;
    }

    private static T[] Grow<T>(T[] array, int used, int needed)
    {
        var larger = ArrayPool<T>.Shared.Rent(Math.Max(needed, array.Length * 2));
        array.AsSpan(0, used).CopyTo(larger);
        ArrayPool<T>.Shared.Return(array);
        return larger;
    }

    private void WriteValue()
    {
        if (_level == JsonBridge.MaxLevels)
        {
            throw Unstorable($"The JSON value at {Path()} is nested deeper than the {JsonBridge.MaxLevels} levels that DynamoDB allows.");
        }

        switch (_json.TokenType)
        {
            case JsonTokenType.StartObject:
                WriteObject();
                break;
            case JsonTokenType.StartArray:
                WriteArray();
                break;
            case JsonTokenType.String:
                _writer.WriteString(ReadText(out _));
                break;
            case JsonTokenType.Number:
                WriteNumber();
                break;
            case JsonTokenType.True or JsonTokenType.False:
                _writer.WriteBool(_json.TokenType == JsonTokenType.True);
                break;
            default:
                _writer.WriteNull();
                break;
        }
    }

    private void WriteObject()
    {
        _frames[_level++] = new Frame(isObject: true, _entries);
        _writer.WriteStartMap();
        while (_json.Read() && _json.TokenType == JsonTokenType.PropertyName)
        {
            var name = AddName();
            _ = _json.Read();
            if (_json.TokenType != JsonTokenType.Null)
            {
                _writer.WriteAttributeName(name);
                WriteValue();
            }
        }

        _writer.WriteEndMap();
        _entries = _frames[--_level].Position;
        _namesLength = _entries == 0 ? 0 : _ends[_entries - 1];
    }

    private void WriteArray()
    {
        _frames[_level++] = new Frame(isObject: false, 0);
        _writer.WriteStartList();
        while (_json.Read() && _json.TokenType != JsonTokenType.EndArray)
        {
            WriteValue();
            _frames[_level - 1].Position++;
        }

        _writer.WriteEndList();
        _level--;
    }

    private void WriteNumber()
    {
        // A JSON number has no escapes, and the reader has checked its form.
        var text = _json.ValueSpan;
        var limit = DynamoDbNumber.FitsDynamoDb(text) switch
        {
            NumberFit.Fits => null,
            NumberFit.TooManyDigits => "more than the 38 significant digits",
            NumberFit.TooLarge => "a magnitude of 1E+126 or more, beyond the 9.9999999999999999999999999999999999999E+125",
            _ => "a magnitude below the 1E-130",
        };
        if (limit is not null)
        {
            throw Unstorable($"The JSON number at {Path()} has {limit} that DynamoDB stores.");
        }

        _writer.WriteNumber(text);
    }

    // The name of an object goes after the names of the objects around it and stays there until its object ends. The position of the frame is where the names of its object begin,
    // and its current entry is the name before this one until the name is checked, which a path must leave out.
    private ReadOnlySpan<byte> AddName()
    {
        ref var frame = ref _frames[_level - 1];
        frame.Current = -1;
        var name = ReadText(out var start);
        if (name.IsEmpty)
        {
            throw Unstorable($"A name in the JSON object at {Path()} is empty, and DynamoDB has no empty attribute names.");
        }

        var hash = Hash(name);
        var first = frame.Position;
        var seen = _hashes.AsSpan(first, _entries - first);
        for (var at = seen.IndexOf(hash); at >= 0; at = NextIndexOf(seen, at, hash))
        {
            if (NameAt(first + at).SequenceEqual(name))
            {
                throw Unstorable($"The name at {Path(name)} is in its JSON object twice, and a DynamoDB map holds a name once.");
            }
        }

        if (_entries == _hashes.Length)
        {
            _hashes = Grow(_hashes, _entries, _entries + 1);
            _ends = Grow(_ends, _entries, _entries + 1);
        }

        _hashes[_entries] = hash;
        _ends[_entries] = start + name.Length;
        frame.Current = _entries++;
        _namesLength = start + name.Length;
        return _names.AsSpan(start, name.Length);
    }

    private readonly ReadOnlySpan<byte> NameAt(int entry)
    {
        var start = entry == 0 ? 0 : _ends[entry - 1];
        return _names.AsSpan(start, _ends[entry] - start);
    }

    // The unescaped text of the string or name the reader is on, checked to be text. A name stays in _names, where it starts at start, and a string is overwritten by the next text.
    private ReadOnlySpan<byte> ReadText(out int start)
    {
        start = _namesLength;
        ReadOnlySpan<byte> text;
        if (_json.ValueIsEscaped)
        {
            // Unescaped text is never longer than its escaped form.
            var length = _json.ValueSpan.Length;
            if (start + length > _names.Length)
            {
                _names = Grow(_names, start, start + length);
            }

            try
            {
                text = _names.AsSpan(start, _json.CopyString(_names.AsSpan(start)));
            }
            catch (InvalidOperationException exception)
            {
                throw Unstorable($"{TextAt()} is not valid UTF-16: it holds a lone surrogate, which DynamoDB cannot store.", exception);
            }
        }
        else
        {
            text = _json.ValueSpan;
            if (_json.TokenType == JsonTokenType.PropertyName)
            {
                if (start + text.Length > _names.Length)
                {
                    _names = Grow(_names, start, start + text.Length);
                }

                text.CopyTo(_names.AsSpan(start));
                text = _names.AsSpan(start, text.Length);
            }
        }

        if (!Utf8.IsValid(text))
        {
            throw Unstorable($"{TextAt()} is not valid UTF-8, which DynamoDB cannot store.");
        }

        return text;
    }

    private readonly string TextAt() => _json.TokenType == JsonTokenType.PropertyName ? $"A name in the JSON object at {Path()}" : $"The JSON string at {Path()}";

    private readonly string Path(ReadOnlySpan<byte> lastName = default)
    {
        var path = new StringBuilder("$");
        for (var level = 0; level < _level; level++)
        {
            var frame = _frames[level];
            if (!frame.IsObject)
            {
                path.Append('[').Append(frame.Position.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else if (frame.Current >= 0)
            {
                AppendName(path, NameAt(frame.Current));
            }
        }

        if (!lastName.IsEmpty)
        {
            AppendName(path, lastName);
        }

        return path.ToString();
    }

    private readonly UnstorableJsonException Unstorable(string reason, Exception? inner = null) => new(reason, _paramName, inner);

    /// <summary>An open array or object.</summary>
    internal struct Frame
    {
        public Frame(bool isObject, int position)
        {
            IsObject = isObject;
            Position = position;
            Current = -1;
        }

        /// <summary>Gets a value indicating whether this is an object.</summary>
        public bool IsObject { get; }

        /// <summary>Gets or sets the index of the current element of an array, or the entry where the names of an object begin.</summary>
        public int Position { get; set; }

        /// <summary>Gets or sets the entry of the current name of an object, -1 while there is none.</summary>
        public int Current { get; set; }
    }
}
