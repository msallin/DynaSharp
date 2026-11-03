using System.Buffers.Binary;

namespace DynaSharp;

/// <summary>
/// The bounds of the version-7 <see cref="Guid"/> values of one millisecond, for a range of time over a key of <see cref="Guid.CreateVersion7(DateTimeOffset)"/> ids.
/// </summary>
/// <remarks>
/// <para>
/// A version-7 <see cref="Guid"/> starts with the milliseconds since the Unix epoch, in 48 bits, big-endian, and ends with random bits, so the lowercase text that DynaSharp stores for a
/// <see cref="Guid"/>, in a key and in an attribute, sorts by time: <c>019a4a2e-5c00-7…</c> of one millisecond before <c>019a4a2e-5c01-7…</c> of the next. Within one millisecond the random bits
/// decide. <see cref="FirstVersion7(DateTimeOffset)"/> and <see cref="LastVersion7(DateTimeOffset)"/> are the lowest and highest such value of a millisecond, so every id that
/// <see cref="Guid.CreateVersion7(DateTimeOffset)"/> makes in it lies between them, both included.
/// </para>
/// <para>
/// They go where the sort key takes a <see cref="Guid"/>. With <c>[SortKey("{Id}")]</c>, <c>Keys.Sk.Between(Guid.FirstVersion7(from), Guid.LastVersion7(to))</c> reads the ids from the millisecond of
/// <c>from</c> to that of <c>to</c>, both whole, <c>Keys.Sk.Ge(Guid.FirstVersion7(from))</c> reads from <c>from</c> on, and <c>Keys.Sk.Lt(Guid.FirstVersion7(to))</c> stops before <c>to</c>. A
/// <see cref="Guid.NewGuid"/>, version 4, is random from its first bit and has no such order.
/// </para>
/// </remarks>
public static class GuidExtensions
{
    // Byte 6 starts with the version and byte 8 with the variant, 0b10, which Guid.CreateVersion7 sets. Everything after the time and outside those bits is random.
    private const byte LowestVersionByte = 0x70;

    private const byte HighestVersionByte = 0x7F;

    private const byte LowestVariantByte = 0x80;

    private const byte HighestVariantByte = 0xBF;

    extension(Guid)
    {
        /// <summary>Returns the lowest version-7 <see cref="Guid"/> of the millisecond that holds an instant: its time, and every random bit 0.</summary>
        /// <param name="at">The instant. Its offset does not matter, and what it holds below a millisecond is dropped, as <see cref="Guid.CreateVersion7(DateTimeOffset)"/> drops it.</param>
        /// <returns>The value, such as <c>019a4a2e-5c00-7000-8000-000000000000</c>, which sorts below every other version-7 value of that millisecond and above every value of the one before.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="at"/> is before the Unix epoch, which a version-7 value cannot hold.</exception>
        public static Guid FirstVersion7(DateTimeOffset at) => Version7(at, LowestVersionByte, 0x00, LowestVariantByte, 0x00);

        /// <summary>Returns the highest version-7 <see cref="Guid"/> of the millisecond that holds an instant: its time, and every random bit 1.</summary>
        /// <param name="at">The instant. Its offset does not matter, and what it holds below a millisecond is dropped, as <see cref="Guid.CreateVersion7(DateTimeOffset)"/> drops it.</param>
        /// <returns>The value, such as <c>019a4a2e-5c00-7fff-bfff-ffffffffffff</c>, which sorts above every other version-7 value of that millisecond and below every value of the next.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="at"/> is before the Unix epoch, which a version-7 value cannot hold.</exception>
        public static Guid LastVersion7(DateTimeOffset at) => Version7(at, HighestVersionByte, 0xFF, HighestVariantByte, 0xFF);
    }

    private static Guid Version7(DateTimeOffset at, byte version, byte afterVersion, byte variant, byte afterVariant)
    {
        var milliseconds = at.ToUnixTimeMilliseconds();
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds, nameof(at));

        // The 48 bits of time are the first six bytes: the milliseconds shifted to the top of a big-endian long. DateTimeOffset.MaxValue needs 48 bits, so nothing is lost.
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes, milliseconds << 16);
        bytes[6] = version;
        bytes[7] = afterVersion;
        bytes[8] = variant;
        bytes[9..].Fill(afterVariant);
        return new Guid(bytes, bigEndian: true);
    }
}
