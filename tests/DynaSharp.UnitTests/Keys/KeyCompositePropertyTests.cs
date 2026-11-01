using System.Diagnostics.CodeAnalysis;
using System.Text;
using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

/// <summary>
/// Properties of composite keys: whatever values are written into a template, two different tuples never give the same key, and a
/// prefix that ends on a delimiter matches exactly the keys of its value. The candidates are every string up to a length over an
/// alphabet that contains the delimiter and the letters of the template's own literals, so injection attempts are in there.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes the samples reproducible; nothing depends on its unpredictability.")]
public sealed class KeyCompositePropertyTests
{
    private const string Name = "Value";

    private static readonly string[] Candidates = AllStrings(["#", "U", "1"], 5);

    [Test]
    public async Task WriteString_TwoPlaceholdersOfAScopedTemplate_NeverGiveTheSameKeyForDifferentValues()
    {
        // Arrange
        var keys = new Dictionary<string, (string Tenant, string Id)>();
        var collisions = 0;

        // Act
        foreach (var tenant in Candidates)
        {
            foreach (var id in Candidates)
            {
                var key = TenantUser(tenant, id);
                if (key is not null && !keys.TryAdd(key, (tenant, id)))
                {
                    collisions++;
                }
            }
        }

        // Assert
        await Assert.That(collisions).IsEqualTo(0);
    }

    [Test]
    public async Task WriteString_ValuesWithTheDelimiter_AreRejectedAndNoOthers()
    {
        // Arrange
        var valid = Candidates.Where(c => !c.Contains('#', StringComparison.Ordinal)).ToList();
        var accepted = 0;

        // Act
        foreach (var tenant in Candidates)
        {
            foreach (var id in Candidates)
            {
                accepted += TenantUser(tenant, id) is null ? 0 : 1;
            }
        }

        // Assert
        await Assert.That(accepted).IsEqualTo(valid.Count * valid.Count);
    }

    [Test]
    public async Task WriteString_TemplatesOfSeveralEntityTypesOnOneAttribute_NeverGiveTheSameKey()
    {
        // Arrange (USER#{Id}, USER#{Id}#ORDER#{Order} and USER#{Id}#{Name} share the attribute)
        var keys = new HashSet<string>();
        var collisions = 0;

        // Act
        foreach (var id in Candidates)
        {
            var user = User(id);
            collisions += user is not null && !keys.Add(user) ? 1 : 0;
            foreach (var other in Candidates)
            {
                var order = UserOrder(id, other);
                var named = UserNamed(id, other);
                collisions += order is not null && !keys.Add(order) ? 1 : 0;
                collisions += named is not null && !keys.Add(named) ? 1 : 0;
            }
        }

        // Assert
        await Assert.That((collisions, keys.Count > Candidates.Length)).IsEqualTo((0, true));
    }

    [Test]
    public async Task Prefix_ValueFollowedByTheDelimiter_MatchesExactlyTheKeysOfThatValue()
    {
        // Arrange
        var valid = Candidates.Where(c => !c.Contains('#', StringComparison.Ordinal)).ToList();
        var mismatches = 0;

        // Act
        foreach (var owner in valid)
        {
            var prefix = $"USER#{owner}#";
            foreach (var id in valid)
            {
                var key = UserOrder(id, "1")!;
                mismatches += key.StartsWith(prefix, StringComparison.Ordinal) == (id == owner) ? 0 : 1;
            }
        }

        // Assert
        await Assert.That(mismatches).IsEqualTo(0);
    }

    [Test]
    public async Task WriteUpperBound_AnyKeyThatCanBeWrittenUnderThePrefix_SortsBetweenThePrefixAndTheBound()
    {
        // Arrange (values may contain U+10FFFF in the alphabet; the writer rejects exactly those, which is what makes the bound sound)
        var random = new Random(KeySamples.Seed);
        int[] alphabet = [0x20, 0x41, 0x7A, 0xFC, 0x20AC, 0xFFFF, 0x10000, 0x10FFFE, 0x10FFFF];
        var prefix = Encoding.UTF8.GetBytes("ORDER#2026#");
        var bound = UpperBound(prefix);
        var misplaced = 0;
        var wronglyAccepted = 0;
        var accepted = 0;

        // Act
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            var value = KeySamples.Text(random, alphabet, 6);
            var key = Key((ref KeyWriter writer) =>
            {
                writer.WriteLiteral(prefix);
                writer.WriteString(value, "#"u8, Name);
            });
            var hasHighestScalar = value.Contains(char.ConvertFromUtf32(0x10FFFF), StringComparison.Ordinal);
            var rejected = key is null;
            wronglyAccepted += rejected == hasHighestScalar ? 0 : 1;
            if (key is not null)
            {
                accepted++;
                var bytes = Encoding.UTF8.GetBytes(key);
                misplaced += KeySamples.Compare(prefix, bytes) < 0 && KeySamples.Compare(bytes, bound) < 0 ? 0 : 1;
            }
        }

        // Assert
        await Assert.That((misplaced, wronglyAccepted, accepted is > 0 and < KeySamples.Samples)).IsEqualTo((0, 0, true));
    }

    [Test]
    public async Task WriteString_ValueThatStartsWithTheHighestScalar_IsRejected()
    {
        // Arrange (a longer key that starts with U+10FFFF would sort above prefix + U+10FFFF)
        var value = char.ConvertFromUtf32(0x10FFFF) + "a";

        // Act
        var key = Key((ref KeyWriter writer) => writer.WriteString(value, "#"u8, Name));

        // Assert
        await Assert.That(key).IsNull();
    }

    private static string[] AllStrings(string[] alphabet, int maxLength)
    {
        var all = new List<string>();
        var level = alphabet.ToList();
        for (var length = 1; length <= maxLength; length++)
        {
            all.AddRange(level);
            level = [.. level.SelectMany(prefix => alphabet.Select(letter => prefix + letter))];
        }

        return [.. all];
    }

    private static byte[] UpperBound(byte[] prefix) => KeyVectorRunner.Write(
        (ref KeyWriter writer) =>
        {
            writer.WriteLiteral(prefix);
            writer.WriteUpperBound();
        },
        8);

    private static byte[] Concat(byte[] first, byte[] second) => [.. first, .. second];

    private static string? TenantUser(string tenant, string id) => Key((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("TENANT#"u8);
        writer.WriteString(tenant, "#"u8, Name);
        writer.WriteLiteral("#USER#"u8);
        writer.WriteString(id, "#"u8, Name);
    });

    private static string? User(string id) => Key((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("USER#"u8);
        writer.WriteString(id, "#"u8, Name);
    });

    private static string? UserOrder(string id, string order) => Key((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("USER#"u8);
        writer.WriteString(id, "#"u8, Name);
        writer.WriteLiteral("#ORDER#"u8);
        writer.WriteString(order, "#"u8, Name);
    });

    private static string? UserNamed(string id, string name) => Key((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("USER#"u8);
        writer.WriteString(id, "#"u8, Name);
        writer.WriteLiteral("#"u8);
        writer.WriteString(name, "#"u8, Name);
    });

    private static string? Key(KeyWriterAction action)
    {
        try
        {
            return Encoding.UTF8.GetString(KeyVectorRunner.Write(action, 64));
        }
        catch (KeyFormatException)
        {
            return null;
        }
    }
}
