using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>
/// The conditions of generated key builders on a real sort: the items are written through their entity, and each query says which of them come back and in which order. What decides that is
/// the emulator comparing stored keys, so an encoding or a bound that does not follow the values shows up as a wrong row.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class SortKeyConditionTests(Backend backend)
{
    private const string Meter = "m-1";

    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    // In the order of each key: Sequence is the position of the row in all of them.
    private static readonly MeterReading[] Readings =
    [
        Reading(1, Noon.AddDays(-1), -1000.5m, "a", 9),
        Reading(2, Noon, -12.5m, "a b", 10),
        Reading(3, Noon, -12.49m, "a!", 99),
        Reading(4, Noon.AddMilliseconds(1), 0m, "ab", 100),
        Reading(5, Noon.AddSeconds(1), 12.49m, "b", 1000),
        Reading(6, Noon.AddDays(1), 12.5m, "é", 9999),
    ];

    [Test]
    public async Task Query_ConditionsOnATimestamp_SelectTheRowsOfTheInstants()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);

        // Act
        int[][] rows =
        [
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Ge(Noon)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Gt(Noon)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Le(Noon)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Lt(Noon)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Between(Noon, Noon.AddMilliseconds(1))).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Eq(Noon, 2)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Ge(Noon, 3)).ItemsAsync()),
        ];

        // Assert
        await Assert.That(string.Join(" | ", rows.Select(static row => string.Join(",", row)))).IsEqualTo("2,3,4,5,6 | 4,5,6 | 1,2,3 | 1 | 2,3,4 | 2 | 3,4,5,6");
    }

    [Test]
    public async Task Query_RangeOfTimestamps_TakesTheKeysAtFromAndBetweenAndLeavesOutThoseAtTo()
    {
        // Arrange (rows 2 and 3 are at from, 4 lies between, 5 is at to; Between takes it and Range does not)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        var (from, to) = (Noon, Noon.AddSeconds(1));

        // Act
        int[][] rows =
        [
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Range(from, to)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Between(from, to)).ItemsAsync()),
            await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Range(to, to)).ItemsAsync()),
        ];

        // Assert
        await Assert.That(string.Join(" | ", rows.Select(static row => string.Join(",", row)))).IsEqualTo("2,3,4 | 2,3,4,5 | ");
    }

    [Test]
    public async Task Query_RangeOfAFieldBeforeARawValue_LeavesOutEveryKeyAtTo()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var appointments = sandbox.Client.Table<Appointment>(Tables.UniqueName());
        await sandbox.CreateTableAsync(appointments, appointments.CreateTable().WaitUntilActive());
        foreach (var (day, slot) in new[] { (1, "z"), (2, "!"), (2, "z#z"), (3, "a#b"), (4, "!"), (4, "z"), (5, "a") })
        {
            await appointments.Put(new Appointment { Key = "k", Day = day, Slot = slot }).SendAsync();
        }

        // Act
        var read = await appointments.Query(Appointment.Keys.Pk.Of("k"), Appointment.Keys.Sk.Range(2, 4)).ItemsAsync().Select(static appointment => $"{appointment.Day}:{appointment.Slot}").ToArrayAsync();

        // Assert
        await Assert.That(string.Join(",", read)).IsEqualTo("2:!,2:z#z,3:a#b");
    }

    [Test]
    public async Task Query_ConditionsOnASignedNumberOfAnIndex_SelectTheRowsOfTheNumbers()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        var byValue = readings.Index(MeterReading.Indexes.ByValue);

        // Act
        int[][] rows =
        [
            await RowsAsync(byValue.Query(MeterReading.Keys.ByValuePk.Of(Meter), MeterReading.Keys.ByValueSk.Ge(-12.49m)).ItemsAsync()),
            await RowsAsync(byValue.Query(MeterReading.Keys.ByValuePk.Of(Meter), MeterReading.Keys.ByValueSk.Gt(-12.49m)).ItemsAsync()),
            await RowsAsync(byValue.Query(MeterReading.Keys.ByValuePk.Of(Meter), MeterReading.Keys.ByValueSk.Le(0m)).ItemsAsync()),
            await RowsAsync(byValue.Query(MeterReading.Keys.ByValuePk.Of(Meter), MeterReading.Keys.ByValueSk.Lt(-12.5m)).ItemsAsync()),
            await RowsAsync(byValue.Query(MeterReading.Keys.ByValuePk.Of(Meter), MeterReading.Keys.ByValueSk.Between(-12.5m, 12.49m)).ItemsAsync()),
        ];

        // Assert
        await Assert.That(string.Join(" | ", rows.Select(static row => string.Join(",", row)))).IsEqualTo("3,4,5,6 | 4,5,6 | 1,2,3,4 | 1 | 2,3,4,5");
    }

    [Test]
    public async Task Query_ConditionsOnDigitsOfALocalIndex_SelectTheRowsOfTheNumbers()
    {
        // Arrange (9 and 10, which plain digits would put the other way round)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        var byDigits = readings.Index(MeterReading.Indexes.ByDigits);

        // Act
        int[][] rows =
        [
            await RowsAsync(byDigits.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.ByDigitsSk.Ge(10)).ItemsAsync()),
            await RowsAsync(byDigits.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.ByDigitsSk.Gt(10)).ItemsAsync()),
            await RowsAsync(byDigits.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.ByDigitsSk.Le(99)).ItemsAsync()),
            await RowsAsync(byDigits.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.ByDigitsSk.Lt(10)).ItemsAsync()),
            await RowsAsync(byDigits.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.ByDigitsSk.Between(10, 100)).ItemsAsync()),
        ];

        // Assert
        await Assert.That(string.Join(" | ", rows.Select(static row => string.Join(",", row)))).IsEqualTo("2,3,4,5,6 | 3,4,5,6 | 1,2,3 | 1 | 2,3,4");
    }

    [Test]
    public async Task Query_ConditionsOnAStringThatEndsTheKey_FollowTheBytesOfWholeValues()
    {
        // Arrange (a longer value sorts above a shorter one, so Gt keeps it and Le leaves it out)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        var byLabel = readings.Index(MeterReading.Indexes.ByLabel);

        // Act
        int[][] rows =
        [
            await RowsAsync(byLabel.Query(MeterReading.Keys.ByLabelPk.Of(Meter), MeterReading.Keys.ByLabelSk.Ge("ab")).ItemsAsync()),
            await RowsAsync(byLabel.Query(MeterReading.Keys.ByLabelPk.Of(Meter), MeterReading.Keys.ByLabelSk.Gt("a")).ItemsAsync()),
            await RowsAsync(byLabel.Query(MeterReading.Keys.ByLabelPk.Of(Meter), MeterReading.Keys.ByLabelSk.Le("a")).ItemsAsync()),
            await RowsAsync(byLabel.Query(MeterReading.Keys.ByLabelPk.Of(Meter), MeterReading.Keys.ByLabelSk.Lt("ab")).ItemsAsync()),
            await RowsAsync(byLabel.Query(MeterReading.Keys.ByLabelPk.Of(Meter), MeterReading.Keys.ByLabelSk.Between("a!", "b")).ItemsAsync()),
        ];

        // Assert
        await Assert.That(string.Join(" | ", rows.Select(static row => string.Join(",", row)))).IsEqualTo("4,5,6 | 2,3,4,5,6 | 1 | 1,2,3 | 3,4,5");
    }

    [Test]
    public async Task Query_ValueThatContinuesALowerOne_MatchesBetweenOnItsWholeValue()
    {
        // Arrange (b is the upper bound, and ba sorts above it)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        await readings.Put(Reading(7, Noon, 1m, "ba", 1)).SendAsync();
        var byLabel = readings.Index(MeterReading.Indexes.ByLabel);

        // Act
        var rows = await RowsAsync(byLabel.Query(MeterReading.Keys.ByLabelPk.Of(Meter), MeterReading.Keys.ByLabelSk.Between("ab", "b")).ItemsAsync());

        // Assert
        await Assert.That(rows).IsEquivalentTo([4, 5]);
    }

    [Test]
    public async Task Query_OpenRangeBesideAnotherEntity_ReachesItOnlyInARawRead()
    {
        // Arrange (ZONE# sorts above AT#, so nothing in the key keeps a Ge on the entity)
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        await readings.Of<MeterZone>().Put(new MeterZone { Meter = Meter, Zone = "north" }).SendAsync();
        var shop = sandbox.Client.Table(readings.Name, "pk", "sk");

        // Act
        var typed = await RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Ge(Noon.AddDays(1))).ItemsAsync());
        var raw = await shop.Query(MeterReading.Keys.Pk.Format(Meter), MeterReading.Keys.Sk.Ge(Noon.AddDays(1))).ItemsAsync().CountAsync();

        // Assert
        await Assert.That((typed.Length, raw)).IsEqualTo((1, 2));
    }

    [Test]
    public async Task Query_ConditionOfAKeyOverSeveralPartitions_MergesWhatEachSelects()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);
        await readings.Put(Reading(7, Noon.AddHours(1), 1m, "z", 1) with { Meter = "m-2" }).SendAsync();

        // Act
        var rows = await RowsAsync(readings.Query(new[] { Meter, "m-2" }.Select(MeterReading.Keys.Pk.Of), MeterReading.Keys.Sk.Gt(Noon.AddSeconds(1))).ItemsAsync());

        // Assert
        await Assert.That(rows).IsEquivalentTo([7, 6]);
    }

    [Test]
    public async Task Query_ConditionsOnAPlainNumber_CompareTheNumbers()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var ticks = sandbox.Client.Table<CounterTick>(Tables.UniqueName());
        await sandbox.CreateTableAsync(ticks, ticks.CreateTable().WaitUntilActive());
        foreach (var number in new long[] { -2, 9, 10, 100 })
        {
            await ticks.Put(new CounterTick { Stream = "s", Number = number }).SendAsync();
        }

        // Act
        var numbers = new[]
        {
            await NumbersAsync(ticks.Query(CounterTick.Keys.Pk.Of("s"), CounterTick.Keys.Sk.Ge(9)).ItemsAsync()),
            await NumbersAsync(ticks.Query(CounterTick.Keys.Pk.Of("s"), CounterTick.Keys.Sk.Gt(9)).ItemsAsync()),
            await NumbersAsync(ticks.Query(CounterTick.Keys.Pk.Of("s"), CounterTick.Keys.Sk.Le(9)).ItemsAsync()),
            await NumbersAsync(ticks.Query(CounterTick.Keys.Pk.Of("s"), CounterTick.Keys.Sk.Lt(9)).ItemsAsync()),
            await NumbersAsync(ticks.Query(CounterTick.Keys.Pk.Of("s"), CounterTick.Keys.Sk.Between(-2, 10)).ItemsAsync()),
            await NumbersAsync(ticks.Query(CounterTick.Keys.Pk.Of("s"), CounterTick.Keys.Sk.Eq(10)).ItemsAsync()),
        };

        // Assert
        await Assert.That(string.Join(" | ", numbers)).IsEqualTo("9,10,100 | 10,100 | -2,9 | -2 | -2,9,10 | 10");
    }

    [Test]
    public async Task Query_ConditionsOnAPlainString_CompareAndMatchAPrefix()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var words = sandbox.Client.Table<DictionaryWord>(Tables.UniqueName());
        await sandbox.CreateTableAsync(words, words.CreateTable().WaitUntilActive());
        foreach (var word in new[] { "apple", "apply", "b" })
        {
            await words.Put(new DictionaryWord { Language = "en", Word = word }).SendAsync();
        }

        // Act
        var prefixed = await words.Query(DictionaryWord.Keys.Pk.Of("en"), DictionaryWord.Keys.Sk.BeginsWith("appl")).ItemsAsync().CountAsync();
        var above = await words.Query(DictionaryWord.Keys.Pk.Of("en"), DictionaryWord.Keys.Sk.Gt("apple")).ItemsAsync().CountAsync();

        // Assert
        await Assert.That((prefixed, above)).IsEqualTo((2, 2));
    }

    [Test]
    public async Task Query_BetweenTheWrongWayRound_IsRejected()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var readings = await CreateAsync(sandbox);

        // Act
        Task Query() => RowsAsync(readings.Query(MeterReading.Keys.Pk.Of(Meter), MeterReading.Keys.Sk.Between(Noon.AddDays(1), Noon)).ItemsAsync());

        // Assert
        await Assert.That(Query).Throws<ValidationException>();
    }

    private static MeterReading Reading(int sequence, DateTimeOffset at, decimal value, string label, int digits) =>
        new() { Meter = Meter, At = at, Sequence = sequence, Value = value, Label = label, Digits = digits };

    private static async Task<Table<MeterReading>> CreateAsync(Sandbox sandbox)
    {
        var readings = sandbox.Client.Table<MeterReading>(Tables.UniqueName());
        await sandbox.CreateTableAsync(readings, readings.CreateTable().Including<MeterZone>().WaitUntilActive());
        foreach (var reading in Readings)
        {
            await readings.Put(reading).SendAsync();
        }

        return readings;
    }

    private static async Task<int[]> RowsAsync(IAsyncEnumerable<MeterReading> items) => await items.Select(static reading => reading.Sequence).ToArrayAsync();

    private static async Task<string> NumbersAsync(IAsyncEnumerable<CounterTick> items) => string.Join(",", await items.Select(static tick => tick.Number).ToArrayAsync());
}
