using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// A string with an <c>[OmitEmpty]</c> of its own that feeds only keys of sparse indexes, <see cref="BlankListing"/>: an empty value goes as null goes, for its attribute and for the keys of its indexes, on every
/// way of writing, and a key built from it by hand still refuses it.
/// </summary>
public sealed class OmitEmptySparseIndexTests
{
    [Test]
    public async Task SendAsync_PutOfEmptyStrings_StoresNoIndexKeyAndReadsThemBack()
    {
        // Arrange (DynamoDB rejects an empty string in an index key, so before this the put threw KeyFormatException)
        var fake = new FakeDynamoDb();
        fake.CreateTable<BlankListing>("Shop");
        using var client = fake.CreateClient();
        var table = client.Table<BlankListing>("Shop");

        // Act
        await table.Put(new BlankListing { Id = "a", Code = string.Empty, Tenant = string.Empty, Seller = string.Empty }).SendAsync();
        var read = await table.Get(BlankListing.Keys.Of("a")).ItemAsync();

        // Assert
        await Assert.That((string.Join(", ", fake.Items("Shop")[0].Keys.Order(StringComparer.Ordinal)), read)).IsEqualTo(("Id, _type, pk", new BlankListing { Id = "a", Code = string.Empty, Tenant = null, Seller = string.Empty }));
    }

    [Test]
    public async Task SendAsync_PutOfValues_IndexesTheItem()
    {
        // Arrange
        var fake = new FakeDynamoDb();
        fake.CreateTable<BlankListing>("Shop");
        using var client = fake.CreateClient();
        var table = client.Table<BlankListing>("Shop");
        await table.Put(new BlankListing { Id = "a", Code = "c" }).SendAsync();

        // Act
        var found = await table.Index(BlankListing.Indexes.ByCode).Query(BlankListing.Keys.ByCodePk.Of("c")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Select(static listing => listing.Id)).IsEquivalentTo(["a"]);
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEmptyInputOfASharedAttribute_LeavesOutTheAttributeAndBothSortKeys()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<BlankListing>("Shop").Put(new BlankListing { Id = "a", Tenant = string.Empty, Seller = "s", Buyer = "b" }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Item":{"pk":{"S":"a"},"_type":{"S":"LISTING"},"Id":{"S":"a"},"Seller":{"S":"s"},"Buyer":{"S":"b"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEmptyGateOfOneOfTwoIndexes_KeepsTheSharedAttributeTheOtherNeeds()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<BlankListing>("Shop").Put(new BlankListing { Id = "a", Tenant = "t", Seller = string.Empty, Buyer = "b" }).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Item":{"pk":{"S":"a"},"shared":{"S":"TENANT#t"},"bybuyersk":{"S":"BUYER#b"},"_type":{"S":"LISTING"},"Id":{"S":"a"},"Tenant":{"S":"t"},"Buyer":{"S":"b"}}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetOfAnEmptyString_RemovesTheAttributeAndTheKeyOfItsIndex()
    {
        // Arrange (the handle counts an empty value as null, so the update removes it, and the removal of an input of a sparse index takes its key along)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<BlankListing>("Shop").Update(BlankListing.Keys.Of("a")).Set(BlankListing.Attrs.Code, string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n2 = if_not_exists(#n2, :v0) REMOVE #n0, #n1","ExpressionAttributeNames":{"#n0":"Code","#n1":"bycodepk","#n2":"_type"}
            """);
    }

    [Test]
    public async Task ToRequestJson_SetIfNotNullOfAnEmptyString_AddsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<BlankListing>("Shop").Update(BlankListing.Keys.Of("a")).SetIfNotNull(BlankListing.Attrs.Code, string.Empty).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("Code");
    }

    [Test]
    public async Task SetIfNotExists_EmptyString_ThrowsArgumentException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var listings = client.Table<BlankListing>("Shop");

        // Act
        var failure = TestClient.Catch(() => _ = listings.Update(BlankListing.Keys.Of("a")).SetIfNotExists(BlankListing.Attrs.Code, string.Empty));

        // Assert
        await Assert.That(failure is ArgumentException { ParamName: "value" }).IsTrue();
    }

    [Test]
    public async Task Of_EmptyString_ThrowsKeyFormatException()
    {
        // Arrange, Act (a key built by hand is no write that can leave it out)
#pragma warning disable DYNA122 // The value is one that the key refuses, on purpose.
        var call = static () => BlankListing.Keys.ByCodePk.Of(string.Empty);
#pragma warning restore DYNA122

        // Assert
        await Assert.That(call).Throws<KeyFormatException>();
    }
}
