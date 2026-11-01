using System.Diagnostics.CodeAnalysis;
using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Keys;
using DynaSharp.Requests;
using TUnit.Assertions.Enums;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>
/// Checks the claims of the key encodings against the emulators instead of against the spec: that DynamoDB's comparison of
/// sort keys (UTF-8 bytes) gives the order the encodings promise, for <c>BETWEEN</c>, <c>begins_with</c> and the comparison
/// operators, negative numbers included. The expected order is always computed from the values, never from the encoded text.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded shuffle makes the insertion order reproducible; the emulators sort by key anyway.")]
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class KeyOrderingTests(Backend backend)
{
    private const string Name = "Value";
    private const string Partition = "p";

    private static readonly decimal[] Amounts =
    [
        -9_999_999_999.99m, -99_999_999.99m, -1000.5m, -100m, -12.5m, -12.49m, -1m, -0.01m, 0m, 0.01m, 1m, 12.49m, 12.5m, 100m, 1000.5m,
        99_999_999.99m, 9_999_999_999.99m,
    ];

    private static readonly DateTimeOffset[] Instants =
    [
        new(2025, 12, 31, 23, 59, 59, 999, TimeSpan.Zero),
        new(2026, 1, 1, 0, 0, 0, 0, TimeSpan.Zero),
        new(2026, 10, 1, 0, 0, 0, 0, TimeSpan.Zero),
        new(2026, 10, 15, 12, 0, 0, 500, TimeSpan.Zero),
        new(2026, 10, 31, 12, 34, 56, 789, TimeSpan.Zero),
        new(2026, 10, 31, 23, 59, 59, 999, TimeSpan.Zero),
        new(2026, 11, 1, 0, 0, 0, 0, TimeSpan.Zero),
        new(2027, 1, 1, 0, 0, 0, 0, TimeSpan.Zero),
    ];

    private delegate void KeyAction(ref KeyWriter writer);

    [Test]
    public async Task Query_FixedPointSortKeysIncludingNegatives_ReturnsTheNumericOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Amounts).Select(a => FixedPoint(a)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk >= :low", forward: true, (":low", FixedPoint(Amounts[0])));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Amounts.Order().Select(a => FixedPoint(a)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_FixedPointSortKeysBackwards_ReturnsTheReverseNumericOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Amounts).Select(a => FixedPoint(a)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk >= :low", forward: false, (":low", FixedPoint(Amounts[0])));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Amounts.OrderDescending().Select(a => FixedPoint(a)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BetweenTwoNegativeNumbers_ReturnsTheNumbersOfTheRangeInclusive()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Amounts).Select(a => FixedPoint(a)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk BETWEEN :low AND :high", forward: true, (":low", FixedPoint(-1000.5m)), (":high", FixedPoint(-12.49m)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Amounts.Where(a => a is >= -1000.5m and <= -12.49m).Order().Select(a => FixedPoint(a)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BetweenANegativeAndAPositiveNumber_ReturnsEverythingAcrossZero()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Amounts).Select(a => FixedPoint(a)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk BETWEEN :low AND :high", forward: true, (":low", FixedPoint(-12.5m)), (":high", FixedPoint(12.49m)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Amounts.Where(a => a is >= -12.5m and <= 12.49m).Order().Select(a => FixedPoint(a)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_LessThanZero_ReturnsExactlyTheNegativeNumbers()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Amounts).Select(a => FixedPoint(a)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk < :zero", forward: true, (":zero", FixedPoint(0m)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Amounts.Where(a => a < 0).Order().Select(a => FixedPoint(a)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BeginsWithTheSignLetter_SeparatesNegativeFromNonNegativeNumbers()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Amounts).Select(a => FixedPoint(a)));

        // Act
        var negative = await QueryAsync(sandbox, table, "begins_with(sk, :sign)", forward: true, (":sign", "N"));
        var nonNegative = await QueryAsync(sandbox, table, "begins_with(sk, :sign)", forward: true, (":sign", "P"));

        // Assert
        await Assert.That((negative.Length, nonNegative.Length)).IsEqualTo((Amounts.Count(a => a < 0), Amounts.Count(a => a >= 0)));
    }

    [Test]
    public async Task Query_PlainIntegerSortKeys_ReturnTheTextOrderThatTheFixedFormatsExistToAvoid()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        var numbers = Enumerable.Range(1, 12).ToList();
        await PutAsync(sandbox, table, numbers.Select(n => Plain(n)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk >= :low", forward: true, (":low", "0"));

        // Assert
        await Assert.That(keys).IsEquivalentTo(["1", "10", "11", "12", "2", "3", "4", "5", "6", "7", "8", "9"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_ZeroPaddedDigitSortKeys_ReturnTheNumericOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        long[] numbers = [999_999, 1, 100, 9, 123_456, 10, 99, 0];
        await PutAsync(sandbox, table, numbers.Select(n => Digits(n, 6)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk >= :low", forward: true, (":low", Digits(0, 6)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(numbers.Order().Select(n => Digits(n, 6)), CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(KeyTimeFormat.Year)]
    [Arguments(KeyTimeFormat.Month)]
    [Arguments(KeyTimeFormat.Day)]
    [Arguments(KeyTimeFormat.Hour)]
    [Arguments(KeyTimeFormat.Minute)]
    public async Task Query_BeginsWithACoarseTimestamp_ReturnsTheItemsOfThatBucket(KeyTimeFormat format)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Instants.Select(i => Timestamp(i, KeyTimeFormat.Millisecond)));
        var probe = Instants[4];

        // Act
        var keys = await QueryAsync(sandbox, table, "begins_with(sk, :prefix)", forward: true, (":prefix", Timestamp(probe, format)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Instants.Where(i => InBucket(i, probe, format)).Select(i => Timestamp(i, KeyTimeFormat.Millisecond)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BetweenTwoTimestamps_ReturnsTheInstantsInChronologicalOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, Shuffled(Instants).Select(i => Timestamp(i, KeyTimeFormat.Millisecond)));
        var from = Instants[1];
        var to = Instants[5];

        // Act
        var keys = await QueryAsync(
            sandbox,
            table,
            "sk BETWEEN :from AND :to",
            forward: true,
            (":from", Timestamp(from, KeyTimeFormat.Millisecond)),
            (":to", Timestamp(to, KeyTimeFormat.Millisecond)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(Instants.Where(i => i >= from && i <= to).Select(i => Timestamp(i, KeyTimeFormat.Millisecond)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BetweenPrefixAndUpperBound_IsInclusiveOnWholeFields()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        var orders = Orders();
        await PutAsync(sandbox, table, orders.Select(o => o.Key));
        var from = Instants[2];
        var to = Instants[4];

        // Act
        var keys = await QueryAsync(sandbox, table, "sk BETWEEN :from AND :to", forward: true, (":from", OrderPrefix(from)), (":to", OrderUpperBound(to)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(orders.Where(o => o.At >= from && o.At <= to).Select(o => o.Key).Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BetweenPrefixAndPrefix_ExcludesTheItemsAtTheUpperBound()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        var orders = Orders();
        await PutAsync(sandbox, table, orders.Select(o => o.Key));
        var from = Instants[2];
        var to = Instants[4];

        // Act
        var keys = await QueryAsync(sandbox, table, "sk BETWEEN :from AND :to", forward: true, (":from", OrderPrefix(from)), (":to", OrderPrefix(to)));

        // Assert
        await Assert.That(keys).IsEquivalentTo(orders.Where(o => o.At >= from && o.At < to).Select(o => o.Key).Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Query_BeginsWithAPrefixThatEndsOnTheDelimiter_NeverMatchesALongerValue()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        await PutAsync(sandbox, table, [UserOrder("1", "a"), UserOrder("1", "b"), UserOrder("10", "a"), UserOrder("100", "a"), UserOrder("2", "a")]);

        // Act
        var wholeField = await QueryAsync(sandbox, table, "begins_with(sk, :prefix)", forward: true, (":prefix", "USER#1#"));
        var partial = await QueryAsync(sandbox, table, "begins_with(sk, :prefix)", forward: true, (":prefix", "USER#1"));

        // Assert
        await Assert.That((wholeField.Length, partial.Length)).IsEqualTo((2, 4));
    }

    [Test]
    public async Task Query_StringSortKeys_ReturnTheOrderOfTheirUtf8Bytes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await CreateTableAsync(sandbox);
        string[] values =
        [
            "a", "z", "Z", "é", "€", "퟿", "", "￿", char.ConvertFromUtf32(0x10000), char.ConvertFromUtf32(0x1F600),
            char.ConvertFromUtf32(0x10FFFE), "日本",
        ];
        await PutAsync(sandbox, table, Shuffled(values).Select(v => Text(v)));

        // Act
        var keys = await QueryAsync(sandbox, table, "sk >= :low", forward: true, (":low", "0"));

        // Assert
        await Assert.That(keys).IsEquivalentTo(values.OrderBy(v => v, Utf8Order.Instance), CollectionOrdering.Matching);
    }

    private static List<(DateTimeOffset At, string Key)> Orders()
    {
        var guids = new[] { "11111111-1111-1111-1111-111111111111", "ffffffff-ffff-ffff-ffff-ffffffffffff" };
        return [.. Instants.SelectMany(at => guids.Select(guid => (at, OrderKey(at, Guid.Parse(guid)))))];
    }

    private static bool InBucket(DateTimeOffset candidate, DateTimeOffset probe, KeyTimeFormat format) => format switch
    {
        KeyTimeFormat.Year => candidate.Year == probe.Year,
        KeyTimeFormat.Month => (candidate.Year, candidate.Month) == (probe.Year, probe.Month),
        KeyTimeFormat.Day => candidate.Date == probe.Date,
        KeyTimeFormat.Hour => candidate.Date == probe.Date && candidate.Hour == probe.Hour,
        _ => candidate.Date == probe.Date && candidate.Hour == probe.Hour && candidate.Minute == probe.Minute,
    };

    private static List<T> Shuffled<T>(IEnumerable<T> values)
    {
        var list = values.ToList();
        var random = new Random(20261031);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private static Task<string> CreateTableAsync(Sandbox sandbox) => sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.String));

    private static async Task PutAsync(Sandbox sandbox, string table, IEnumerable<string> sortKeys)
    {
        foreach (var sortKey in sortKeys)
        {
            await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = new Dictionary<string, AttributeValue> { ["pk"] = Partition, ["sk"] = sortKey } });
        }
    }

    private static async Task<string[]> QueryAsync(Sandbox sandbox, string table, string sortKeyCondition, bool forward, params (string Placeholder, AttributeValue Value)[] values)
    {
        var response = await sandbox.Client.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = $"pk = :pk AND {sortKeyCondition}",
            ExpressionAttributeValues = Items.Values([(":pk", Partition), .. values]),
            ScanIndexForward = forward,
            ConsistentRead = true,
        });
        return [.. response.Items.Select(item => item["sk"].AsString())];
    }

    private static string FixedPoint(decimal value) => Write((ref KeyWriter writer) => writer.WriteFixedPoint(value, 10, 2, Name));

    private static string Plain(int value) => Write((ref KeyWriter writer) => writer.WriteInteger((long)value, default, Name));

    private static string Digits(long value, int width) => Write((ref KeyWriter writer) => writer.WriteDigits(value, width, Name));

    private static string Text(string value) => Write((ref KeyWriter writer) => writer.WriteString(value, "#"u8, Name));

    private static string Timestamp(DateTimeOffset value, KeyTimeFormat format) => Write((ref KeyWriter writer) => writer.WriteTimestamp(value, format));

    private static string OrderKey(DateTimeOffset at, Guid id) => Write((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("ORDER#"u8);
        writer.WriteTimestamp(at, KeyTimeFormat.Millisecond);
        writer.WriteLiteral("#"u8);
        writer.WriteGuid(id);
    });

    private static string OrderPrefix(DateTimeOffset at) => Write((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("ORDER#"u8);
        writer.WriteTimestamp(at, KeyTimeFormat.Millisecond);
        writer.WriteLiteral("#"u8);
    });

    private static string OrderUpperBound(DateTimeOffset at) => Write((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("ORDER#"u8);
        writer.WriteTimestamp(at, KeyTimeFormat.Millisecond);
        writer.WriteLiteral("#"u8);
        writer.WriteUpperBound();
    });

    private static string UserOrder(string userId, string orderId) => Write((ref KeyWriter writer) =>
    {
        writer.WriteLiteral("USER#"u8);
        writer.WriteString(userId, "#"u8, Name);
        writer.WriteLiteral("#ORDER#"u8);
        writer.WriteString(orderId, "#"u8, Name);
    });

    private static string Write(KeyAction action)
    {
        var writer = new KeyWriter(stackalloc byte[128]);
        try
        {
            action(ref writer);
            return writer.ToString();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Orders strings the way DynamoDB documents it: by the bytes of their UTF-8 encoding.</summary>
    private sealed class Utf8Order : IComparer<string>
    {
        public static Utf8Order Instance { get; } = new();

        public int Compare(string? x, string? y) => Encoding.UTF8.GetBytes(x!).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y!));
    }
}
