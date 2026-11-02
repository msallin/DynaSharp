using System.Reflection;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// PageAsync with only a token is written by hand on every read that pages, so a read without it, or one that lets the token fall, would go missing without a word.
public sealed class PageForwardingTests
{
    private const string EmptyPage = """{"Items":[],"Count":0,"ScannedCount":0}""";

    private static readonly Dictionary<string, Func<Table<Item>, CancellationToken, Task>> Reads = new(StringComparer.Ordinal)
    {
        ["Query"] = static async (shop, token) => await shop.Query("a").PageAsync(token),
        ["Scan"] = static async (shop, token) => await shop.Scan().PageAsync(token),
        ["QuerySelect"] = static async (shop, token) => await shop.Query("a").Select(Attr<int>("n")).PageAsync(token),
        ["ScanSelect"] = static async (shop, token) => await shop.Scan().Select(Attr<int>("n")).PageAsync(token),
        ["MixedQueryOfTwo"] = static async (shop, token) => await shop.Query("a").Of<Alpha, Beta>().PageAsync(token),
        ["MixedQueryOfThree"] = static async (shop, token) => await shop.Query("a").Of<Alpha, Beta, Gamma>().PageAsync(token),
        ["MixedQueryOfFour"] = static async (shop, token) => await shop.Query("a").Of<Alpha, Beta, Gamma, Delta>().PageAsync(token),
        ["MixedScanOfTwo"] = static async (shop, token) => await shop.Scan().Of<Alpha, Beta>().PageAsync(token),
        ["MixedScanOfThree"] = static async (shop, token) => await shop.Scan().Of<Alpha, Beta, Gamma>().PageAsync(token),
        ["MixedScanOfFour"] = static async (shop, token) => await shop.Scan().Of<Alpha, Beta, Gamma, Delta>().PageAsync(token),
        ["FanOut"] = static async (shop, token) => await shop.Query(["a", "b"]).PageAsync(10, token),
    };

    public static IEnumerable<string> ReadNames() => Reads.Keys;

    [Test]
    [MethodDataSource(nameof(ReadNames))]
    public async Task PageAsync_OnlyAToken_ReadsTheFirstPage(string read)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyPage));
        using var client = TestClient.Create(handler);

        // Act
        await Reads[read](client.Table("Shop", "pk", "sk"), CancellationToken.None);

        // Assert
        var positions = handler.Requests.Count(static request => request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal));
        await Assert.That((handler.Requests.Count > 0, positions)).IsEqualTo((true, 0));
    }

    [Test]
    [MethodDataSource(nameof(ReadNames))]
    public async Task PageAsync_OnlyAToken_CancelsWithIt(string read)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler(_ => StubResponse.Json(EmptyPage));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => Reads[read](client.Table("Shop", "pk", "sk"), cancellation.Token));

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task PageAsync_EveryOverloadWithAPosition_HasOneWithOnlyAToken()
    {
        // Arrange
        var withPosition = PageMethods().Where(static method => method.GetParameters().Any(static parameter => parameter.ParameterType == typeof(ContinuationToken))).ToList();

        // Act
        var missing = withPosition.Where(static method => !HasOverloadWithOnlyAToken(method)).Select(static method => method.DeclaringType!.Name);

        // Assert
        await Assert.That((withPosition.Count > 0, string.Join(' ', missing))).IsEqualTo((true, string.Empty));
    }

    private static IEnumerable<MethodInfo> PageMethods() =>
        typeof(Table<>).Assembly.GetExportedTypes().Where(static type => type.IsByRefLike).SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).Where(static method => method.Name == "PageAsync");

    // The token has no default on that overload, or PageAsync() would have two candidates.
    private static bool HasOverloadWithOnlyAToken(MethodInfo method)
    {
        var expected = method.GetParameters().Select(static parameter => parameter.ParameterType).Where(static type => type != typeof(ContinuationToken));
        return method.DeclaringType!.GetMethods().Any(candidate =>
            candidate.Name == method.Name
            && candidate.GetParameters().Select(static parameter => parameter.ParameterType).SequenceEqual(expected)
            && !candidate.GetParameters()[^1].HasDefaultValue);
    }
}
