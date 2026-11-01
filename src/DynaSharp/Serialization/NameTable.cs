using System.Buffers;
using System.Text;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Hands out one string per distinct attribute name instead of one per occurrence: responses repeat the same few names over and over.
/// </summary>
/// <remarks>
/// <para>
/// Lookups compare the decoded text, so a name hits in any spelling (raw, escaped, non-ASCII), and reading a known name allocates nothing.
/// Attribute names can be chosen by whoever writes the items, so nothing here grows: the number of slots is fixed, longer names are not
/// kept, and a name that finds no free slot among the few it may use replaces the one at its home slot. Colliding names then cost what
/// reading without a table costs, one string each.
/// </para>
/// <para>
/// Slots hold immutable strings and a reference is written atomically, so threads need no lock: the worst a race does is lose an entry,
/// or leave two equal strings around.
/// </para>
/// </remarks>
/// <param name="slots">The number of slots, a power of two of at least <see cref="Probes"/>.</param>
internal sealed class NameTable(int slots)
{
    /// <summary>The number of slots a name may use, its home slot and the next ones, so a few names sharing a home slot all stay cached.</summary>
    internal const int Probes = 4;

    // DynamoDB allows 255 bytes for key attributes and 64 KB for others. Real names stay far below this, and a longer one is just not kept.
    private const int MaxCachedLength = 64;

    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;

    private readonly string?[] _names = new string?[slots];

    /// <summary>Gets the table production code uses, shared by all clients: at most about 1.3 MB, with every slot on a name of full length.</summary>
    public static NameTable Shared { get; } = new(8192);

    /// <summary>Hashes a name, the same for every spelling of it. Not randomized: an attacker who collides names only loses cache entries.</summary>
    /// <param name="name">The decoded name.</param>
    /// <returns>The hash.</returns>
    public static uint Hash(ReadOnlySpan<char> name)
    {
        var hash = FnvOffset;
        foreach (var c in name)
        {
            hash = (hash ^ c) * FnvPrime;
        }

        // FNV-1a leaves names that differ only at the end, like attribute07 and attribute12, a small lattice apart. Shape hashes add up
        // the hashes of names, which would then collapse onto few values, so the finalizer of MurmurHash3 spreads every bit.
        hash = (hash ^ (hash >> 16)) * 0x85EBCA6B;
        hash = (hash ^ (hash >> 13)) * 0xC2B2AE35;
        return hash ^ (hash >> 16);
    }

    /// <summary>Gets the name the JSON reader is positioned on, the cached string when the name is known.</summary>
    /// <param name="json">The JSON reader, positioned on a property name.</param>
    /// <returns>The unescaped name.</returns>
    public string GetName(ref Utf8JsonReader json)
    {
        // Decoding never makes text longer: neither a UTF-8 sequence nor an escape has fewer bytes than the characters it stands for.
        var raw = json.ValueSpan;
        if (raw.Length > MaxCachedLength)
        {
            return json.GetString()!;
        }

        // Nearly every name is plain ASCII, which widens in a fraction of the time the general decoding takes.
        Span<char> decoded = stackalloc char[MaxCachedLength];
        if (json.ValueIsEscaped || Ascii.ToUtf16(raw, decoded, out var length) != OperationStatus.Done)
        {
            length = json.CopyString(decoded);
        }

        return Intern(decoded[..length]);
    }

    private string Intern(ReadOnlySpan<char> name)
    {
        var mask = _names.Length - 1;
        var home = (int)Hash(name) & mask;
        for (var probe = 0; probe < Probes; probe++)
        {
            var slot = (home + probe) & mask;
            var cached = _names[slot];
            if (cached is null)
            {
                return _names[slot] = new string(name);
            }

            if (name.SequenceEqual(cached))
            {
                return cached;
            }
        }

        return _names[home] = new string(name);
    }
}
