using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// Properties of the merge over random data. The expected order is computed by the test from the values, with ties by position, and a run is reproducible from its seed: what must
// hold is that the merge gives that order, and that cutting it into pages of any size, with the tokens going through their text, loses and repeats nothing.
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Seeded, so a failure names the run that reproduces it.")]
public sealed class FanOutPagingTests
{
    private const int Runs = 300;

    private const int MostPages = 1000;

    public enum KeyKind
    {
        /// <summary>Strings of characters of every UTF-8 length.</summary>
        Text,

        /// <summary>Numbers of up to 38 digits.</summary>
        Number,

        /// <summary>Binary values.</summary>
        Bytes,
    }

    [Test]
    [Arguments(KeyKind.Text, false)]
    [Arguments(KeyKind.Text, true)]
    [Arguments(KeyKind.Number, false)]
    [Arguments(KeyKind.Number, true)]
    [Arguments(KeyKind.Bytes, false)]
    [Arguments(KeyKind.Bytes, true)]
    public async Task ItemsAsync_RandomPartitions_GiveTheOrderOfTheValues(KeyKind kind, bool descending)
    {
        // Arrange
        var failures = new List<string>();

        // Act
        for (var seed = 0; seed < Runs; seed++)
        {
            var run = new Run(new Random(seed), kind, descending);
            var items = await run.ReadItemsAsync();
            run.Check(items, seed, failures);
        }

        // Assert
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    [Arguments(KeyKind.Text, false)]
    [Arguments(KeyKind.Text, true)]
    [Arguments(KeyKind.Number, false)]
    [Arguments(KeyKind.Number, true)]
    [Arguments(KeyKind.Bytes, false)]
    [Arguments(KeyKind.Bytes, true)]
    public async Task PageAsync_RandomPageSizes_ConcatenateToTheUnpagedMerge(KeyKind kind, bool descending)
    {
        // Arrange
        var failures = new List<string>();

        // Act
        for (var seed = 0; seed < Runs; seed++)
        {
            var random = new Random(seed);
            var run = new Run(random, kind, descending);
            var items = await run.ReadPagesAsync(random);
            run.Check(items, seed, failures);
        }

        // Assert
        await Assert.That(failures).IsEmpty();
    }

    // One data set, its expected merge and a client on a wire that holds it. Which items pass the filter, the sizes of the responses and whether there is a limit are random too.
    private sealed class Run
    {
        private readonly FanOutWire _wire;
        private readonly bool _descending;
        private readonly bool _filtered;
        private readonly int? _limit;
        private readonly AttributeValue[] _partitions;
        private readonly List<string> _expected = [];

        public Run(Random random, KeyKind kind, bool descending)
        {
            _descending = descending;
            _filtered = random.Next(2) == 0;
            _limit = random.Next(2) == 0 ? random.Next(1, 6) : null;
            _wire = new FanOutWire { ItemsPerResponse = random.Next(1, 8) };

            var partitionCount = random.Next(1, 7);
            _partitions = new AttributeValue[partitionCount];
            var candidates = SortKeys(random, kind);
            var entries = new List<(int Rank, int Position, string Partition, string SortKey)>();
            for (var position = 0; position < partitionCount; position++)
            {
                // The list order is not the order of the names, so that ties do not hide behind them.
                var partition = "p" + (((position * 3) % partitionCount) + (10 * position)).ToString(CultureInfo.InvariantCulture);
                _partitions[position] = partition;
                var share = random.Next(0, 4) == 0 ? 0 : random.Next(1, 25);
                for (var rank = 0; rank < candidates.Length && share > 0; rank++)
                {
                    if (random.Next(candidates.Length) < share)
                    {
                        var passes = !_filtered || random.Next(3) > 0;
                        _wire.Put(partition, candidates[rank], passes);
                        if (passes)
                        {
                            entries.Add((rank, position, partition, candidates[rank]));
                        }
                    }
                }
            }

            var ordered = entries.OrderBy(static entry => entry.Rank).ThenBy(static entry => entry.Position);
            _expected.AddRange((descending ? ordered.Reverse() : ordered).Select(static entry => entry.Partition + "/" + entry.SortKey));
        }

        public async Task<List<string>> ReadItemsAsync()
        {
            using var handler = new StubHandler(_wire.Answer);
            using var client = TestClient.Create(handler);
            var items = await Build(client).ItemsAsync().ToListAsync();
            return [.. items.Select(Show)];
        }

        public async Task<List<string>> ReadPagesAsync(Random random)
        {
            using var handler = new StubHandler(_wire.Answer);
            using var client = TestClient.Create(handler);
            var all = new List<string>();
            var token = string.Empty;
            for (var pages = 0; pages < MostPages; pages++)
            {
                var size = random.Next(1, 8);
                var page = await Build(client).PageAsync(size, ContinuationToken.Parse(token));
                all.AddRange(page.Items.Select(Show));
                if (page.Items.Count > size)
                {
                    throw new InvalidOperationException("A page holds more than was asked for.");
                }

                if (!page.HasMore)
                {
                    return all;
                }

                token = page.Next.ToString();
            }

            throw new InvalidOperationException("The pages do not end: a token does not move on.");
        }

        public void Check(List<string> actual, int seed, List<string> failures)
        {
            if (!actual.SequenceEqual(_expected))
            {
                failures.Add($"seed {seed}: expected {string.Join(" ", _expected)} but found {string.Join(" ", actual)}");
            }
        }

        private static string[] SortKeys(Random random, KeyKind kind) => kind switch
        {
            KeyKind.Text => Texts(random),
            KeyKind.Number => Numbers(random),
            _ => Bytes(random),
        };

        // Characters of every UTF-8 length, in random strings, ordered by their bytes by a comparison that has nothing to do with the code under test.
        private static string[] Texts(Random random)
        {
            string[] alphabet = ["a", "Z", "0", "é", "ß", "€", "�", "𝄞", "😀"];
            var texts = new SortedSet<string>(Comparer<string>.Create(static (a, b) => Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b))));
            while (texts.Count < 40)
            {
                var text = new StringBuilder();
                for (var length = random.Next(1, 5); length > 0; length--)
                {
                    text.Append(alphabet[random.Next(alphabet.Length)]);
                }

                texts.Add(text.ToString());
            }

            return [.. texts];
        }

        // Exact values, up to 38 digits and either sign, in order by value, spelled in a way that hides it.
        private static string[] Numbers(Random random)
        {
            var values = new SortedSet<BigInteger>();
            while (values.Count < 40)
            {
                var digits = random.Next(1, 39);
                var text = new StringBuilder(random.Next(2) == 0 ? "-" : string.Empty).Append((char)('1' + random.Next(9)));
                for (var i = 1; i < digits; i++)
                {
                    text.Append((char)('0' + random.Next(10)));
                }

                values.Add(BigInteger.Parse(text.ToString(), CultureInfo.InvariantCulture));
            }

            return [.. values.Select(static value => "{\"N\":\"" + value.ToString(CultureInfo.InvariantCulture) + "\"}")];
        }

        private static string[] Bytes(Random random)
        {
            var values = new SortedSet<byte[]>(Comparer<byte[]>.Create(static (a, b) => a.AsSpan().SequenceCompareTo(b)));
            while (values.Count < 40)
            {
                var bytes = new byte[random.Next(1, 4)];
                random.NextBytes(bytes);
                values.Add(bytes);
            }

            return [.. values.Select(static value => "{\"B\":\"" + Convert.ToBase64String(value) + "\"}")];
        }

        private static string Show(Item item)
        {
            var sortKey = item["sk"];
            return item.GetString("from") + "/" + sortKey.Type switch
            {
                AttributeType.String => sortKey.AsString(),
                _ => sortKey.ToString(),
            };
        }

        private FanOutQueryCommand<Item> Build(DynamoDbClient client)
        {
            var query = client.Table("Shop", "pk", "sk").Query(_partitions);
            query = _limit is { } limit ? query.Limit(limit) : query;
            query = _filtered ? query.Filter(Attr("x").Exists()) : query;
            return _descending ? query.Descending() : query;
        }
    }
}
