using BenchmarkDotNet.Attributes;

namespace DynaSharp.Benchmarks;

/// <summary>What the check of <c>Keys.Of</c> costs on a key of strings, against the struct that <c>Of</c> makes and against the text that <c>Format</c> makes.</summary>
[MemoryDiagnoser]
public class KeyBenchmarks
{
    private const string Tenant = "acme-industries";

    private const string User = "user-4711";

    private const string Order = "2026-11-02-000042";

    [Benchmark(Baseline = true)]
    public BenchKeyed.Keys.PrimaryKey Hold() => new(Tenant, User, Order);

    [Benchmark]
    public BenchKeyed.Keys.PrimaryKey Of() => BenchKeyed.Keys.Of(Tenant, User, Order);

    [Benchmark]
    public BenchKeyed.Keys.PrimaryKey? TryOf() => BenchKeyed.Keys.TryOf(Tenant, User, Order);

    [Benchmark]
    public BenchKeyed.Keys.PrimaryKey? TryOf_Rejected() => BenchKeyed.Keys.TryOf(Tenant, User, "a#b");

    [Benchmark]
    public string Format() => BenchKeyed.Keys.Pk.Format(Tenant, User);
}
