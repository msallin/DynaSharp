using System.Diagnostics.CodeAnalysis;
using DynaSharp.Keys;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Keys;

/// <summary>
/// The bounds of the version-7 values of a millisecond, against what <see cref="Guid.CreateVersion7(DateTimeOffset)"/> makes, compared as DynaSharp stores a <see cref="Guid"/>: by the bytes of
/// its lowercase text in a key.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes the samples reproducible; nothing depends on its unpredictability.")]
public sealed class GuidExtensionsTests
{
    private const int PerMillisecond = 2_000;

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0L, "00000000-0000-7000-8000-000000000000", "00000000-0000-7fff-bfff-ffffffffffff")]
    [Arguments(1L, "00000000-0001-7000-8000-000000000000", "00000000-0001-7fff-bfff-ffffffffffff")]
    [Arguments(1_793_534_400_000L, "01a196fb-1600-7000-8000-000000000000", "01a196fb-1600-7fff-bfff-ffffffffffff")]
    [Arguments(253_402_300_799_999L, "e677d21f-dbff-7000-8000-000000000000", "e677d21f-dbff-7fff-bfff-ffffffffffff")]
    public async Task Bounds_Millisecond_AreItsTimeWithEveryRandomBitZeroAndOne(long milliseconds, string first, string last)
    {
        // Arrange (the epoch, the millisecond after it, a day of 2026, and the last millisecond a DateTimeOffset holds)
        var at = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

        // Act
        var bounds = (Text(Guid.FirstVersion7(at)), Text(Guid.LastVersion7(at)));

        // Assert
        await Assert.That(bounds).IsEqualTo((first, last));
    }

    [Test]
    public async Task Bounds_AnyInstant_AreVersion7WithTheVariantOfRfc9562()
    {
        // Arrange
        Guid[] bounds = [Guid.FirstVersion7(Noon), Guid.LastVersion7(Noon)];

        // Act
        var fields = bounds.Select(static guid => (guid.Version, guid.Variant >> 2));

        // Assert
        await Assert.That(fields).IsEquivalentTo([(7, 0b10), (7, 0b10)]);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(1L)]
    [Arguments(5_000L)]
    [Arguments(9_999L)]
    public async Task Bounds_InstantInsideAMillisecond_AreThoseOfThatMillisecond(long ticks)
    {
        // Arrange (the first tick of the millisecond, the next, the middle and the last)
        var at = Noon.AddTicks(ticks);

        // Act
        var bounds = (Guid.FirstVersion7(at), Guid.LastVersion7(at));

        // Assert
        await Assert.That(bounds).IsEqualTo((Guid.FirstVersion7(Noon), Guid.LastVersion7(Noon)));
    }

    [Test]
    public async Task Bounds_InstantWithAnOffset_AreThoseOfTheSameUtcInstant()
    {
        // Arrange
        var zurich = Noon.ToOffset(TimeSpan.FromHours(1));

        // Act
        var bounds = (Guid.FirstVersion7(zurich), Guid.LastVersion7(zurich));

        // Assert
        await Assert.That(bounds).IsEqualTo((Guid.FirstVersion7(Noon), Guid.LastVersion7(Noon)));
    }

    [Test]
    public async Task FirstVersion7_BeforeTheUnixEpoch_Throws()
    {
        // Arrange
        var before = DateTimeOffset.UnixEpoch.AddMilliseconds(-1);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Guid.FirstVersion7(before)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("at");
    }

    [Test]
    public async Task LastVersion7_BeforeTheUnixEpoch_Throws()
    {
        // Arrange
        var before = DateTimeOffset.MinValue;

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Guid.LastVersion7(before)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("at");
    }

    [Test]
    public async Task Bounds_TheBclTurnsAwayTheSameInstants()
    {
        // Arrange (the bounds cover what CreateVersion7 makes, no more: it throws for a time before the epoch too)
        var before = DateTimeOffset.UnixEpoch.AddMilliseconds(-1);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(Guid.CreateVersion7(before)));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(1_793_534_400_000L)]
    [Arguments(253_402_300_799_999L)]
    public async Task Bounds_IdsOfTheirMillisecond_LieBetweenThemInTheStoredText(long milliseconds)
    {
        // Arrange
        var at = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        var (first, last) = (Stored(Guid.FirstVersion7(at)), Stored(Guid.LastVersion7(at)));

        // Act
        var outside = Enumerable.Range(0, PerMillisecond).Select(_ => Stored(Guid.CreateVersion7(at))).Count(id => KeySamples.Compare(id, first) < 0 || KeySamples.Compare(id, last) > 0);

        // Assert
        await Assert.That(outside).IsEqualTo(0);
    }

    [Test]
    public async Task LastVersion7_AgainstTheNextMillisecond_SortsBelowEveryIdOfIt()
    {
        // Arrange (the boundary between two milliseconds, from both sides)
        var last = Stored(Guid.LastVersion7(Noon));
        var next = Enumerable.Range(0, PerMillisecond).Select(_ => Stored(Guid.CreateVersion7(Noon.AddMilliseconds(1)))).Append(Stored(Guid.FirstVersion7(Noon.AddMilliseconds(1))));

        // Act
        var below = next.Count(id => KeySamples.Compare(id, last) <= 0);

        // Assert
        await Assert.That(below).IsEqualTo(0);
    }

    [Test]
    public async Task FirstVersion7_AgainstThePreviousMillisecond_SortsAboveEveryIdOfIt()
    {
        // Arrange
        var first = Stored(Guid.FirstVersion7(Noon));
        var previous = Enumerable.Range(0, PerMillisecond).Select(_ => Stored(Guid.CreateVersion7(Noon.AddMilliseconds(-1)))).Append(Stored(Guid.LastVersion7(Noon.AddMilliseconds(-1))));

        // Act
        var above = previous.Count(id => KeySamples.Compare(id, first) >= 0);

        // Assert
        await Assert.That(above).IsEqualTo(0);
    }

    [Test]
    public async Task Bounds_ManyInstantsInOrder_InterleaveWithTheirIdsInTheStoredText()
    {
        // Arrange (first, an id, last, per millisecond, over random steps from one millisecond to a year)
        var random = new Random(KeySamples.Seed);
        var at = DateTimeOffset.UnixEpoch;
        var sequence = new List<byte[]>();
        for (var i = 0; i < KeySamples.Samples; i++)
        {
            at = at.AddMilliseconds(random.NextInt64(1, 365L * 24 * 3600 * 1000));
            sequence.AddRange([Stored(Guid.FirstVersion7(at)), Stored(Guid.CreateVersion7(at)), Stored(Guid.LastVersion7(at))]);
        }

        // Act
        var violation = KeySamples.FirstOrderViolation(sequence);

        // Assert
        await Assert.That(violation).IsEqualTo("none");
    }

    [Test]
    public async Task Bounds_StoredText_IsTheLowercaseTextOfTheAttributeToo()
    {
        // Arrange (a key and an attribute hold a Guid in one form, so a bound means the same in a key condition and in a filter)
        var bound = Guid.LastVersion7(Noon);

        // Act
        var (key, attribute) = (System.Text.Encoding.UTF8.GetString(Stored(bound)), Item.From(new Ledger { At = Noon, Id = bound }).GetString("Id"));

        // Assert
        await Assert.That((key, attribute)).IsEqualTo(("01a196fb-1600-7fff-bfff-ffffffffffff", "01a196fb-1600-7fff-bfff-ffffffffffff"));
    }

    private static string Text(Guid guid) => guid.ToString("D");

    private static byte[] Stored(Guid guid) => KeyVectorRunner.Write((ref KeyWriter writer) => writer.WriteGuid(guid), 8);
}
