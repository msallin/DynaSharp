using System.Text.Json;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// A <see cref="JsonElement"/> property against a store: what it writes is a native value the service takes, at the limits the bridge checks, and what the service gives back reads as JSON,
/// numbers in the canonical form of DynamoDB.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class JsonElementPropertyTests(Backend backend)
{
    [Test]
    public async Task PutThenGet_Document_ReadsTheSameJsonWithoutItsNulls()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var document = JsonElement.Parse("""{"to":{"city":"Bern"},"weight":1.50,"count":1e2,"tags":["a",null],"empty":{},"none":[],"blank":"","skipped":null,"ok":true}""");

        // Act
        await shop.Shop.Of<Waybill>().Put(new Waybill { Id = "w1", Document = document }).SendAsync();
        var back = await shop.Shop.Of<Waybill>().Get(Waybill.Keys.Of("w1")).Consistent().ItemAsync();

        // Assert (a store gives the names of a map in an order of its own, and DynamoDB its numbers in a canonical form, 1.5 and 100, where DynamoDB Local keeps the text)
        var expected = JsonElement.Parse("""{"to":{"city":"Bern"},"weight":1.5,"count":100,"tags":["a",null],"empty":{},"none":[],"blank":"","ok":true}""");
        await Assert.That((JsonElement.DeepEquals(back!.Document, expected), back.Remark)).IsEqualTo((true, (JsonElement?)null));
    }

    [Test]
    public async Task PutThenGet_ThirtyTwoLevels_IsStored()
    {
        // Arrange (the attribute is the first level, so 31 arrays around a number are the deepest the service takes)
        await using var shop = await ShopSandbox.StartAsync(backend);
        var document = JsonElement.Parse(new string('[', 31) + "1" + new string(']', 31));

        // Act
        await shop.Shop.Of<Waybill>().Put(new Waybill { Id = "w1", Document = document }).SendAsync();
        var back = await shop.Shop.Of<Waybill>().Get(Waybill.Keys.Of("w1")).Consistent().ItemAsync();

        // Assert
        await Assert.That(back!.Document.GetRawText()).IsEqualTo(document.GetRawText());
    }

    [Test]
    public async Task PutThenGet_NumbersAtTheLimits_AreStored()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        var document = JsonElement.Parse("[12345678901234567890123456789012345678,9.9999999999999999999999999999999999999E+125,-1E-130,0]");

        // Act
        await shop.Shop.Of<Waybill>().Put(new Waybill { Id = "w1", Document = document }).SendAsync();
        var back = await shop.Shop.Of<Waybill>().Get(Waybill.Keys.Of("w1")).Consistent().ItemAsync();

        // Assert
        await Assert.That(back!.Document.EnumerateArray().Select(static number => number.GetDouble()).ToArray())
            .IsEquivalentTo([1.2345678901234568E+37, 9.9999999999999999999999999999999999999E+125, -1E-130, 0]);
    }

    [Test]
    public async Task Get_SetsAndBinaryOtherCodeWrote_ReadAsArraysAndBase64()
    {
        // Arrange
        await using var shop = await ShopSandbox.StartAsync(backend);
        await shop.Shop.Put(new Item
        {
            ["pk"] = Waybill.Keys.Pk.Format("w1"),
            ["sk"] = "WAYBILL",
            ["_type"] = "WAYBILL",
            ["Id"] = "w1",
            ["Document"] = AttributeValue.FromMap([new("ss", AttributeValue.FromStringSet(["a"])), new("ns", AttributeValue.FromNumberSet(["2"])), new("b", new byte[] { 1, 2, 3 }), new("bs", AttributeValue.FromBinarySet([[255]]))]),
            ["Remark"] = AttributeValue.Null,
        }).SendAsync();

        // Act
        var back = await shop.Shop.Of<Waybill>().Get(Waybill.Keys.Of("w1")).Consistent().ItemAsync();

        // Assert
        await Assert.That((JsonElement.DeepEquals(back!.Document, JsonElement.Parse("""{"ss":["a"],"ns":[2],"b":"AQID","bs":["/w=="]}""")), back.Remark)).IsEqualTo((true, (JsonElement?)null));
    }
}
