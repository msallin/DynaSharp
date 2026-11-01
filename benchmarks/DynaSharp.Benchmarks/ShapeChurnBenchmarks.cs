using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

/// <summary>
/// Reads pages of 100 items whose attribute sets vary, which is what decides how attribute names are shared: the common page where
/// every item looks alike, a single-table page with a few entity types, and 1000 distinct attribute sets of 100 items each. Those come
/// as one page per set (a tenant at a time), mixed so that no page repeats a set (a scan over a schemaless table), and as subsets of a
/// small vocabulary of names (optional attributes). Numbers are per page; the churn runs read all 1000 pages per operation.
/// </summary>
[MemoryDiagnoser]
public class ShapeChurnBenchmarks
{
    private const int ShapeCount = 1000;
    private const int PageSize = 100;
    private const int AttributesPerItem = 10;
    private const int VocabularySize = 32;

    private byte[] _oneShape = null!;
    private byte[] _threeShapes = null!;
    private byte[][] _onePagePerShape = null!;
    private byte[][] _mixedPages = null!;
    private byte[][] _optionalAttributes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var distinctNames = Enumerable.Range(0, ShapeCount).Select(DistinctNames).ToArray();
        var subsets = Subsets();

        _oneShape = Page(_ => distinctNames[0]);
        _threeShapes = Page(item => distinctNames[item % 3]);
        _onePagePerShape = [.. distinctNames.Select(names => Page(_ => names))];
        _mixedPages = [.. Enumerable.Range(0, ShapeCount).Select(page => Page(item => distinctNames[MixedShape(page, item)]))];
        _optionalAttributes = [.. Enumerable.Range(0, ShapeCount).Select(page => Page(item => subsets[MixedShape(page, item)]))];
    }

    [Benchmark(Baseline = true)]
    public int OneShape() => Read(_oneShape);

    [Benchmark]
    public int ThreeShapes() => Read(_threeShapes);

    [Benchmark(OperationsPerInvoke = ShapeCount)]
    public int Churn_OnePagePerShape() => ReadAll(_onePagePerShape);

    [Benchmark(OperationsPerInvoke = ShapeCount)]
    public int Churn_MixedPages() => ReadAll(_mixedPages);

    [Benchmark(OperationsPerInvoke = ShapeCount)]
    public int Churn_OptionalAttributes() => ReadAll(_optionalAttributes);

    private static int ReadAll(byte[][] pages)
    {
        var count = 0;
        foreach (var page in pages)
        {
            count += Read(page);
        }

        return count;
    }

    private static int Read(byte[] page)
    {
        var reader = new DynamoDbReader(page);
        return OperationJson.ReadItems(ref reader).Count;
    }

    // Every shape occurs once per position, so 100 times in all, and a page holds 100 different shapes.
    private static int MixedShape(int page, int item) => (page + (item * 10)) % ShapeCount;

    // Four names all items have, the rest belongs to the shape: 6004 distinct names.
    private static string[] DistinctNames(int shape)
    {
        var names = new string[AttributesPerItem];
        names[0] = "pk";
        names[1] = "sk";
        names[2] = "type";
        names[3] = "tenant";
        for (var i = 4; i < AttributesPerItem; i++)
        {
            names[i] = string.Create(CultureInfo.InvariantCulture, $"tenant{shape:D4}Field{i}");
        }

        return names;
    }

    // 1000 distinct sets of ten names out of 32, so few names but many sets. The seed keeps the sets the same across runs.
    private static string[][] Subsets()
    {
#pragma warning disable CA5394 // Test data, not security.
        var random = new Random(42);
        var seen = new HashSet<string>();
        var subsets = new List<string[]>();
        while (subsets.Count < ShapeCount)
        {
            var picked = Enumerable.Range(0, VocabularySize).OrderBy(_ => random.Next()).Take(AttributesPerItem).Order().ToArray();
            if (seen.Add(string.Join(',', picked)))
            {
                subsets.Add([.. picked.Select(index => string.Create(CultureInfo.InvariantCulture, $"attribute{index:D2}"))]);
            }
        }
#pragma warning restore CA5394

        return [.. subsets];
    }

    private static byte[] Page(Func<int, string[]> namesOfItem)
    {
        var json = new StringBuilder("[");
        for (var item = 0; item < PageSize; item++)
        {
            json.Append(item == 0 ? "{" : ",{");
            var names = namesOfItem(item);
            for (var attribute = 0; attribute < names.Length; attribute++)
            {
                json.Append(CultureInfo.InvariantCulture, $"{(attribute == 0 ? string.Empty : ",")}\"{names[attribute]}\":{{\"S\":\"value-{item}-{attribute}\"}}");
            }

            json.Append('}');
        }

        return Encoding.UTF8.GetBytes(json.Append(']').ToString());
    }
}
