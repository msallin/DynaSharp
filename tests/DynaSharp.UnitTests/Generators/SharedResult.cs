namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// A result of the generator that several tests read, made once. Under Stryker it is made every time: Stryker switches the active mutant inside a running test host, so a kept result would be that of
/// an earlier mutant. The tests would then be blind to every later mutant of the generator, and a result kept from a mutant would fail them for mutants it has nothing to do with.
/// </summary>
/// <typeparam name="T">The type of the result.</typeparam>
/// <param name="create">Makes the result.</param>
internal sealed class SharedResult<T>(Func<T> create)
{
    private static readonly bool UnderStryker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STRYKER_MUTANT_FILE"));

    private readonly Lazy<T> _kept = new(create);

    public T Value => UnderStryker ? create() : _kept.Value;
}
