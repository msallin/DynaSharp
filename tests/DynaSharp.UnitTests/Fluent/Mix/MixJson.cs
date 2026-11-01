using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>Stored items of the types of a mix, with the discriminator in any place, and pages of them.</summary>
internal static class MixJson
{
    public const string AlphaItem = """{"_type":{"S":"ALPHA"},"pk":{"S":"a"},"sk":{"S":"1"},"Name":{"S":"first"}}""";

    public const string BetaItem = """{"pk":{"S":"a"},"sk":{"S":"2"},"Amount":{"N":"7"},"_type":{"S":"BETA"}}""";

    public const string GammaItem = """{"pk":{"S":"a"},"_type":{"S":"GAMMA"},"sk":{"S":"3"}}""";

    public const string DeltaItem = """{"sk":{"S":"4"},"pk":{"S":"a"},"note":{"M":{"_type":{"S":"ALPHA"}}},"_type":{"S":"DELTA"}}""";

    /// <summary>A page of items, with one more item read than it holds.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The response.</returns>
    public static StubResponse Page(params string[] items) => StubResponse.Json("{\"Items\":[" + string.Join(",", items) + "],\"Count\":" + items.Length + ",\"ScannedCount\":" + (items.Length + 1) + "}");

    /// <summary>What a mixed item holds, as text.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its type and sort key.</returns>
    public static string Show(Mixed<Alpha, Beta> item) => item.Match(alpha => "A" + alpha.Sk, beta => "B" + beta.Sk);

    /// <inheritdoc cref="Show(Mixed{Alpha, Beta})"/>
    public static string Show(Mixed<Alpha, Beta, Gamma> item) => item.Match(alpha => "A" + alpha.Sk, beta => "B" + beta.Sk, gamma => "G" + gamma.Sk);

    /// <inheritdoc cref="Show(Mixed{Alpha, Beta})"/>
    public static string Show(Mixed<Alpha, Beta, Gamma, Delta> item) => item.Match(alpha => "A" + alpha.Sk, beta => "B" + beta.Sk, gamma => "G" + gamma.Sk, delta => "D" + delta.Sk);
}
