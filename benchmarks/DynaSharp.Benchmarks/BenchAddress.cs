using DynaSharp.Mapping;

namespace DynaSharp.Benchmarks;

/// <summary>The nested document of <see cref="BenchOrder"/>.</summary>
/// <param name="street">The street.</param>
/// <param name="city">The city.</param>
/// <param name="zip">The postal code.</param>
[DynamoDbDocument]
internal sealed partial class BenchAddress(string street, string city, string zip)
{
    [DynamoDbAttribute("street")]
    public string Street { get; } = street;

    [DynamoDbAttribute("city")]
    public string City { get; } = city;

    [DynamoDbAttribute("zip")]
    public string Zip { get; } = zip;
}
