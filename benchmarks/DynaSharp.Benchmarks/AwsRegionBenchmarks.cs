using BenchmarkDotNet.Attributes;

namespace DynaSharp.Benchmarks;

[MemoryDiagnoser]
public class AwsRegionBenchmarks
{
    [Benchmark]
    public AwsRegion FromName() => AwsRegion.FromName("eu-central-2");
}
