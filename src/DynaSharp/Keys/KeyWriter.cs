using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace DynaSharp.Keys;

/// <summary>
/// Writes a key as UTF-8 into a caller-provided buffer, validating every value and encoding it so that ordinal byte order equals
/// value order. The encoded form is an on-disk contract: generated key builders call these methods, and changing an encoding is a
/// data migration.
/// </summary>
/// <remarks>
/// <para>
/// Create it over a stack buffer and pass it by <see langword="ref"/>; it grows when a value does not fit, and <see cref="Dispose"/> releases what it took.
/// Always dispose it (<c>try</c>/<c>finally</c>) and never copy it: a copy would release the same memory twice.
/// </para>
/// <para>
/// Every <c>placeholder</c> argument is the name of the template placeholder being written. It is only used for the
/// <see cref="KeyFormatException"/> of a bad value, so it costs a constant string argument on the success path. The exception
/// names the placeholder, never the value.
/// </para>
/// <para>
/// Every <c>delimiters</c> argument holds the ASCII bytes adjacent to the placeholder in the template, the last byte of the literal
/// before it and the first byte of the literal after it. A value that contains one throws, for the last placeholder of a template
/// as well. Pass an empty span for a template that is a single placeholder, and for format <c>raw</c>, whose value may hold them. Values of a fixed, known alphabet (Guid,
/// timestamps, fixed-width numbers) take no delimiters: the generator checks them against the template at compile time.
/// </para>
/// <para>
/// A value that throws leaves the writer as it was before the call, except for bytes beyond <see cref="WrittenSpan"/>, which are
/// scratch and never exposed.
/// </para>
/// <para>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct KeyWriter
{
    // Room asked from the pool at least when a custom type formats itself.
    private const int MinimumFormattableSpace = 64;

    // A key is at most 2048 bytes; a custom type that needs more than this to format itself is not a key part.
    private const int MaximumFormattableSpace = 64 * 1024;

    private Span<byte> _buffer;
    private ArrayPool<byte>? _pool;
    private byte[]? _rented;
    private int _length;

    // False in a default writer, which throws, as a writer does unless it was asked not to.
    private bool _marksInvalid;

    /// <summary>Initializes a new instance of the <see cref="KeyWriter"/> struct that borrows long keys from <see cref="ArrayPool{T}.Shared"/>.</summary>
    /// <param name="buffer">The initial buffer, usually <c>stackalloc byte[n]</c>. It is not cleared and may hold anything; only what the writer wrote is ever exposed.</param>
    public KeyWriter(Span<byte> buffer)
        : this(buffer, ArrayPool<byte>.Shared)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyWriter"/> struct that can check values without an exception. With <paramref name="throwOnInvalid"/> at
    /// <see langword="false"/> a value that a write would throw <see cref="KeyFormatException"/> for turns <see cref="IsInvalid"/> true instead, and the write leaves the writer as it was.
    /// It is one code path with the writer that throws, which is what keeps <c>TryOf</c> and <c>Format</c> of a key builder from disagreeing.
    /// </summary>
    /// <param name="buffer">The initial buffer, usually <c>stackalloc byte[n]</c>.</param>
    /// <param name="throwOnInvalid">Whether a value that cannot be part of a key throws.</param>
    public KeyWriter(Span<byte> buffer, bool throwOnInvalid)
        : this(buffer, ArrayPool<byte>.Shared)
    {
        _marksInvalid = !throwOnInvalid;
    }

    // The pool is a seam for tests, which count what is rented and returned.
    internal KeyWriter(Span<byte> buffer, ArrayPool<byte> pool)
    {
        _buffer = buffer;
        _pool = pool;
        _rented = null;
        _length = 0;
    }

    /// <summary>Gets a value indicating whether a value could not be part of a key. Only a writer that does not throw gets to report that.</summary>
    public bool IsInvalid { readonly get; private set; }

    /// <summary>Gets the bytes written so far. Valid until the next write, which may move the buffer, and until <see cref="Dispose"/>.</summary>
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[.._length];

    /// <summary>Appends a literal of the template. It is not validated: the generator checks literals at compile time.</summary>
    /// <param name="utf8Literal">The printable ASCII bytes of the literal.</param>
    public void WriteLiteral(scoped ReadOnlySpan<byte> utf8Literal)
    {
        utf8Literal.CopyTo(Reserve(utf8Literal.Length));
        _length += utf8Literal.Length;
    }

    /// <summary>
    /// Appends U+10FFFF as <c>F4 8F BF BF</c>, the highest UTF-8 sequence. After a prefix it makes the upper bound of an inclusive
    /// <c>BETWEEN</c> over whole fields: <c>BETWEEN Prefix(a) AND UpperBound(b)</c>. It is above every key under the prefix because a
    /// value may not contain U+10FFFF.
    /// </summary>
    public void WriteUpperBound() => WriteLiteral([0xF4, 0x8F, 0xBF, 0xBF]);

    /// <summary>Appends a string as UTF-8, validated: not null or empty, well-formed UTF-16, no control character, no delimiter, no U+10FFFF. Sorts by UTF-8 bytes, that is by code point, not by culture.</summary>
    /// <param name="value">The value. Null counts as empty.</param>
    /// <param name="delimiters">The delimiters of the template.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The value is not a valid segment. Lone surrogates are rejected, not replaced by U+FFFD, because two different strings would then share a key.</exception>
    public void WriteString(string value, scoped ReadOnlySpan<byte> delimiters, string placeholder)
    {
        if (string.IsNullOrEmpty(value))
        {
            Reject(placeholder, SegmentProblem.Empty);
        }
        else if (TryEncode(value, out var written))
        {
            Commit(written, delimiters, placeholder);
        }
        else
        {
            Reject(placeholder, SegmentProblem.NotWellFormed);
        }
    }

    /// <summary>Appends text that is already UTF-8, for example an enum member name, with the same validation as <see cref="WriteString"/>.</summary>
    /// <param name="utf8Value">The value.</param>
    /// <param name="delimiters">The delimiters of the template.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The value is not a valid segment.</exception>
    public void WriteUtf8Value(scoped ReadOnlySpan<byte> utf8Value, scoped ReadOnlySpan<byte> delimiters, string placeholder) => Append(utf8Value, delimiters, placeholder);

    /// <summary>Appends the name of an enum member. A value that is no member, for example a cast from a number, has no name, and <paramref name="utf8Name"/> is empty.</summary>
    /// <param name="utf8Name">The name as UTF-8, or an empty span if the value has none.</param>
    /// <param name="delimiters">The delimiters of the placeholder.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The value has no name, or the name is not a valid segment.</exception>
    public void WriteEnumName(scoped ReadOnlySpan<byte> utf8Name, scoped ReadOnlySpan<byte> delimiters, string placeholder)
    {
        if (utf8Name.IsEmpty)
        {
            Reject(placeholder, "The value is not a member of its enum, so it has no name in a key.");
        }
        else
        {
            Append(utf8Name, delimiters, placeholder);
        }
    }

    /// <summary>Appends a <see cref="Guid"/> as 36 lowercase hexadecimal digits with hyphens (format <c>D</c>).</summary>
    /// <param name="value">The value. Sorts by creation time for version 7, otherwise in no useful order.</param>
    public void WriteGuid(Guid value)
    {
        _ = value.TryFormat(Reserve(KeyText.GuidText), out _, "D");
        _length += KeyText.GuidText;
    }

    /// <summary>Appends an integer in plain digits, without padding and with a leading minus when negative. Sorts numerically only in a fixed format.</summary>
    /// <param name="value">The value.</param>
    /// <param name="delimiters">The delimiters of the template; a minus sign is one when the template has a hyphen next to the placeholder.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The text contains a delimiter.</exception>
    public void WriteInteger(long value, scoped ReadOnlySpan<byte> delimiters, string placeholder) => WriteNumber(value, delimiters, placeholder);

    /// <inheritdoc cref="WriteInteger(long, ReadOnlySpan{byte}, string)"/>
    public void WriteInteger(ulong value, scoped ReadOnlySpan<byte> delimiters, string placeholder) => WriteNumber(value, delimiters, placeholder);

    /// <summary>Appends a non-negative integer zero-padded to <paramref name="width"/> digits (format <c>d&lt;w&gt;</c>).</summary>
    /// <param name="value">The value.</param>
    /// <param name="width">The number of digits.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The value is negative or has more than <paramref name="width"/> digits.</exception>
    public void WriteDigits(long value, int width, string placeholder)
    {
        if (value < 0)
        {
            Reject(placeholder, "The value is negative, which a zero-padded format cannot order.");
        }
        else
        {
            WriteDigits((ulong)value, width, placeholder);
        }
    }

    /// <inheritdoc cref="WriteDigits(long, int, string)"/>
    public void WriteDigits(ulong value, int width, string placeholder)
    {
        Span<byte> digits = stackalloc byte[KeyText.MaxIntegerText];
        _ = value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        if (length > width)
        {
            Reject(placeholder, "The value has more digits than the width of its format.");
            return;
        }

        var destination = Reserve(width);
        destination[..(width - length)].Fill((byte)'0');
        digits[..length].CopyTo(destination[(width - length)..]);
        _length += width;
    }

    /// <summary>
    /// Appends a decimal in its plain form: trailing zeros of the fraction dropped and negative zero as <c>0</c>, because
    /// <c>1.0m</c> and <c>1m</c> are equal and must be one key. Sorts numerically only in a fixed format.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="delimiters">The delimiters of the template; the point and the minus sign are delimiters when the template has them next to the placeholder.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The text contains a delimiter.</exception>
    public void WriteDecimal(decimal value, scoped ReadOnlySpan<byte> delimiters, string placeholder)
    {
        Span<byte> scratch = stackalloc byte[KeyText.MaxDecimalText];
        Append(KeyText.PlainDecimal(value, scratch), delimiters, placeholder);
    }

    /// <summary>
    /// Appends a signed fixed-point number (format <c>n&lt;i&gt;.&lt;f&gt;</c>, or <c>n&lt;i&gt;</c> with no fraction digits) whose byte order is its numeric order:
    /// a sign letter, <c>P</c> for zero and above and <c>N</c> below zero, then <paramref name="integerDigits"/> integer digits and
    /// <paramref name="fractionDigits"/> fraction digits around a literal point. Negative values use the nine's complement of every digit, so larger
    /// magnitudes sort first. With 4 and 2: <c>12.5</c> is <c>P0012.50</c>, <c>-12.5</c> is <c>N9987.49</c> and <c>0</c> is <c>P0000.00</c>.
    /// </summary>
    /// <param name="value">The value. Negative zero is zero and written as <c>P</c>; <c>12.500</c> fits two fraction digits.</param>
    /// <param name="integerDigits">The number of integer digits, at least 1.</param>
    /// <param name="fractionDigits">The number of fraction digits, at least 0, with <c>integerDigits + fractionDigits &lt;= 28</c>.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">The format is not valid.</exception>
    /// <exception cref="KeyFormatException">The value has more fraction digits than the format (nothing rounds silently) or is out of its range.</exception>
    public void WriteFixedPoint(decimal value, int integerDigits, int fractionDigits, string placeholder)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(integerDigits, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(fractionDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)integerDigits + fractionDigits, (long)KeyText.MaxFixedPointDigits, nameof(fractionDigits));

        // -0.00m equals 0m, so negative zero is P. The all-nines N form could only stand for it, and is never written.
        var negative = value < 0m;
        var magnitude = Math.Abs(value);
        if (decimal.Round(magnitude, fractionDigits) != magnitude)
        {
            Reject(placeholder, "The value has more fraction digits than its format.");
            return;
        }

        Span<char> format = ['F', (char)('0' + (fractionDigits / 10)), (char)('0' + (fractionDigits % 10))];
        Span<byte> text = stackalloc byte[KeyText.MaxFixedPointText];
        _ = magnitude.TryFormat(text, out var textLength, format, CultureInfo.InvariantCulture);

        var integerLength = fractionDigits == 0 ? textLength : textLength - fractionDigits - 1;
        if (integerLength > integerDigits)
        {
            Reject(placeholder, "The value is out of the range of its format.");
            return;
        }

        var total = 1 + integerDigits + (fractionDigits == 0 ? 0 : 1 + fractionDigits);
        var destination = Reserve(total);
        var padding = integerDigits - integerLength;
        destination[0] = negative ? (byte)'N' : (byte)'P';
        destination.Slice(1, padding).Fill((byte)'0');
        text[..textLength].CopyTo(destination[(1 + padding)..]);
        if (negative)
        {
            foreach (ref var digit in destination[1..])
            {
                // Nine's complement: larger magnitudes sort first.
                if (digit != (byte)'.')
                {
                    digit = (byte)('9' - (digit - '0'));
                }
            }
        }

        _length += total;
    }

    /// <summary>Appends an instant in UTC with the fixed width of <paramref name="format"/>. Finer precision is truncated, which is how time buckets are expressed. The offset is not kept.</summary>
    /// <param name="value">The instant.</param>
    /// <param name="format">The format.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    public void WriteTimestamp(DateTimeOffset value, KeyTimeFormat format) => WriteUtc(value.UtcDateTime, format);

    /// <summary>
    /// Appends a time in UTC with the fixed width of <paramref name="format"/>. A <see cref="DateTimeKind.Local"/> value is
    /// converted to UTC; <see cref="DateTimeKind.Unspecified"/> is rejected because it names no instant. Only <see cref="KeyTimeFormat.Ticks"/> keeps the value exactly.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <param name="format">The format.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    /// <exception cref="KeyFormatException">The kind of <paramref name="value"/> is <see cref="DateTimeKind.Unspecified"/>.</exception>
    public void WriteTimestamp(DateTime value, KeyTimeFormat format, string placeholder)
    {
        if (value.Kind == DateTimeKind.Unspecified)
        {
            Reject(placeholder, "The kind of the time is unspecified; use UTC or local time.");
        }
        else
        {
            WriteUtc(value.ToUniversalTime(), format);
        }
    }

    /// <summary>Appends a date as <c>yyyy-MM-dd</c>, 10 bytes.</summary>
    /// <param name="value">The date.</param>
    public void WriteDate(DateOnly value) => WriteUtc(value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), KeyTimeFormat.Day);

    /// <summary>
    /// Appends the invariant UTF-8 text of a custom type, with the same validation as <see cref="WriteString"/>, which also catches
    /// a type that formats itself with a delimiter. Pair it with <c>IUtf8SpanParsable&lt;T&gt;</c> for reading.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="delimiters">The delimiters of the template.</param>
    /// <param name="placeholder">The name of the placeholder, for the exception.</param>
    /// <exception cref="KeyFormatException">The formatted text is not a valid segment, or longer than 64 KB.</exception>
    public void WriteFormattable<T>(T value, scoped ReadOnlySpan<byte> delimiters, string placeholder)
        where T : IUtf8SpanFormattable
    {
        var space = Math.Max(_buffer.Length - _length, MinimumFormattableSpace);
        int written;
        while (!value.TryFormat(_buffer[_length..], out written, default, CultureInfo.InvariantCulture))
        {
            if (space >= MaximumFormattableSpace)
            {
                Reject(placeholder, "The formatted text is longer than the limit of 65536 bytes.");
                return;
            }

            space = Math.Min(space * 2, MaximumFormattableSpace);
            Grow(space);
        }

        Commit(written, delimiters, placeholder);
    }

    /// <summary>
    /// Appends a whole text as UTF-8 for a <see cref="KeyReader"/>, without any of the validation of a value, because the reader does that. It lets
    /// a parser take a <see cref="string"/> without an array: <c>x</c> parses only if formatting the result gives <c>x</c>, and ill-formed text never does.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="false"/> if the text is <see langword="null"/> or not well-formed UTF-16, and then nothing is appended.</returns>
    public bool TryWriteText(string? text)
    {
        if (text is null || !TryEncode(text, out var written))
        {
            return false;
        }

        _length += written;
        return true;
    }

    /// <summary>Gets the key written so far as text.</summary>
    /// <returns>The key.</returns>
    public override readonly string ToString() => Encoding.UTF8.GetString(WrittenSpan);

    /// <summary>Releases what the writer holds and empties it. Safe to call twice; the writer must not be used afterwards.</summary>
    public void Dispose()
    {
        if (_rented is not null)
        {
            // Not cleared, like every pooled buffer: architecture.md, decision 18.
            _pool!.Return(_rented);
        }

        this = default;
    }

    private void WriteNumber<T>(T value, scoped ReadOnlySpan<byte> delimiters, string placeholder)
        where T : IUtf8SpanFormattable
    {
        Span<byte> text = stackalloc byte[KeyText.MaxIntegerText];

        // Locals, not a property call among the arguments: the coverage tool counts a hidden branch for that on a generic type.
        var invariant = CultureInfo.InvariantCulture;
        ReadOnlySpan<char> format = default;
        _ = value.TryFormat(text, out var length, format, invariant);
        Append(text[..length], delimiters, placeholder);
    }

    private void WriteUtc(DateTime utc, KeyTimeFormat format)
    {
        var width = KeyText.TimestampWidth(format);
        KeyText.FormatTimestamp(utc, format, Reserve(width));
        _length += width;
    }

    // Encodes after the written part without advancing. Ill-formed text fails rather than turning into U+FFFD.
    private bool TryEncode(string text, out int written)
    {
        var status = Utf8.FromUtf16(text, _buffer[_length..], out _, out written, replaceInvalidSequences: false);
        if (status == OperationStatus.DestinationTooSmall)
        {
            // The exact size, so that this happens at most once. Ill-formed text is counted with replacement characters, which is enough.
            Grow(Encoding.UTF8.GetByteCount(text));
            status = Utf8.FromUtf16(text, _buffer[_length..], out _, out written, replaceInvalidSequences: false);
        }

        return status == OperationStatus.Done;
    }

    // Validates text that is not in the buffer yet, then appends it.
    private void Append(scoped ReadOnlySpan<byte> text, scoped ReadOnlySpan<byte> delimiters, string placeholder)
    {
        var problem = KeyText.Inspect(text, delimiters);
        if (problem != SegmentProblem.None)
        {
            Reject(placeholder, problem);
            return;
        }

        text.CopyTo(Reserve(text.Length));
        _length += text.Length;
    }

    // Validates text that a formatter has written after the written part, then keeps it.
    private void Commit(int written, scoped ReadOnlySpan<byte> delimiters, string placeholder)
    {
        var problem = KeyText.Inspect(_buffer.Slice(_length, written), delimiters);
        if (problem != SegmentProblem.None)
        {
            Reject(placeholder, problem);
            return;
        }

        _length += written;
    }

    // The one place a value is turned away, so that a writer that throws and one that does not reject the same values. Nothing is built for one that does not throw.
    private void Reject(string placeholder, SegmentProblem problem)
    {
        if (!_marksInvalid)
        {
            throw KeyText.Fail(placeholder, problem);
        }

        IsInvalid = true;
    }

    private void Reject(string placeholder, string reason)
    {
        if (!_marksInvalid)
        {
            throw KeyText.Fail(placeholder, reason);
        }

        IsInvalid = true;
    }

    // Returns the next count bytes after the written part without advancing; callers write all of them before they advance.
    private Span<byte> Reserve(int count)
    {
        if (count > _buffer.Length - _length)
        {
            Grow(count);
        }

        return _buffer.Slice(_length, count);
    }

    // Makes room for count more bytes, at least doubling so that growth is amortized.
    private void Grow(int count)
    {
        var required = checked(_length + count);
        var size = Math.Max(required, (int)Math.Min(2L * _buffer.Length, Array.MaxLength));
        _pool ??= ArrayPool<byte>.Shared;
        var next = _pool.Rent(size);
        _buffer[.._length].CopyTo(next);
        if (_rented is not null)
        {
            _pool.Return(_rented);
        }

        _rented = next;
        _buffer = next;
    }
}
