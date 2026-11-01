using DynaSharp.Operations;
using DynaSharp.UnitTests.Operations;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// Reads response bodies recorded from real DynamoDB. The expectations are the bodies themselves: what the library parses must
/// write back as the same JSON, and the shapes real DynamoDB is known to accept (empty set members, maps in lists) must survive.
/// </summary>
public sealed class RealAwsResponseTests
{
    public static IEnumerable<string> GetItemNames() => RealAwsResponses.Names("GetItem");

    public static IEnumerable<string> QueryNames() => RealAwsResponses.Names("Query");

    [Test]
    [MethodDataSource(nameof(GetItemNames))]
    public async Task ReadResponse_RealGetItemResponse_WritesBackTheSameItem(string name)
    {
        // Arrange
        var recorded = RealAwsResponses.Get(name);

        // Act
        var response = TestJson.Read(recorded.Wire, static (ref reader) => GetItemOperation.ReadResponse(ref reader));
        var written = $$"""{"Item":{{TestJson.WriteItem(response.Item!)}}}""";

        // Assert
        await Assert.That(JsonComparer.Differences(recorded.Wire, written)).IsEqualTo(string.Empty);
    }

    [Test]
    [MethodDataSource(nameof(QueryNames))]
    public async Task ReadResponse_RealQueryResponse_ReturnsTheItemsAndCountsOfTheBody(string name)
    {
        // Arrange
        var recorded = RealAwsResponses.Get(name);

        // Act
        var response = TestJson.Read(recorded.Wire, static (ref reader) => QueryOperation.ReadResponse(ref reader));

        // Assert
        await Assert.That(JsonComparer.Differences(recorded.Wire, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_RealStringSetWithEmptyMember_KeepsTheEmptyMember()
    {
        // Arrange (real DynamoDB accepts the empty string as a member of a string set)
        var recorded = RealAwsResponses.Get("esm_put_ss_mixed");

        // Act
        var response = TestJson.Read(recorded.Wire, static (ref reader) => GetItemOperation.ReadResponse(ref reader));

        // Assert
        await Assert.That(response.Item!["attr"]).IsEqualTo(AttributeValue.FromStringSet([string.Empty, "a"]));
    }

    [Test]
    public async Task ReadResponse_RealBinarySetWithZeroLengthMember_KeepsTheEmptyMember()
    {
        // Arrange
        var recorded = RealAwsResponses.Get("esm_put_bs_mixed");

        // Act
        var response = TestJson.Read(recorded.Wire, static (ref reader) => GetItemOperation.ReadResponse(ref reader));

        // Assert
        await Assert.That(response.Item!["attr"]).IsEqualTo(AttributeValue.FromBinarySet([[], [1]]));
    }

    [Test]
    public async Task ReadResponse_RealItemWithOnlyItsKey_ReturnsAnItemWithOneAttribute()
    {
        // Arrange (an item that exists but has nothing besides its key is not a missing item)
        var recorded = RealAwsResponses.Get("esm_upd_del_last");

        // Act
        var response = TestJson.Read(recorded.Wire, static (ref reader) => GetItemOperation.ReadResponse(ref reader));

        // Assert
        await Assert.That(response.Item!.Keys).IsEquivalentTo(["pk"]);
    }

    [Test]
    public async Task ReadResponse_RealListOfMapsWithDifferentKeyOrders_ReadsEachMapByName()
    {
        // Arrange (DynamoDB does not keep the key order of a map: the second map lists y before x)
        var recorded = RealAwsResponses.Get("proj_get_a3");

        // Act
        var response = TestJson.Read(recorded.Wire, static (ref reader) => GetItemOperation.ReadResponse(ref reader));

        // Assert
        var second = response.Item!["l"].AsList()[1].AsMap();
        await Assert.That((second["x"].AsString(), second["y"].AsString())).IsEqualTo(("x1", "y1"));
    }

    [Test]
    public async Task All_Recordings_AreNamedUniquely()
    {
        // Arrange
        var names = RealAwsResponses.All.Select(static response => response.Name).ToList();

        // Assert
        await Assert.That(names.Distinct().Count()).IsEqualTo(names.Count);
    }
}
