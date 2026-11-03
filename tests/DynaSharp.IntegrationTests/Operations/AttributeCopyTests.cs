using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>
/// <c>SET a = b</c> in an update expression, a path operand that copies an attribute of the same item, which is how an update writes an index key whose text is the text of a stored attribute.
/// What a typed update relies on: the copy lands in the index, a copy of an attribute the item lacks fails the whole update, and a number does not become a string key.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class AttributeCopyTests(Backend backend)
{
    [Test]
    public async Task UpdateItem_CopyOfAStringIntoAnIndexKey_PutsTheItemIntoTheIndex()
    {
        // Arrange
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = sandbox.Shop.Name, Item = Items.Values(("pk", "A"), ("sk", "A"), ("owner", "ann"), ("gsi2sk", "S")) });

        // Act
        await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = sandbox.Shop.Name,
            Key = Items.Values(("pk", "A"), ("sk", "A")),
            UpdateExpression = "SET #k = #s",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#k"] = "gsi2pk", ["#s"] = "owner" },
        });
        var found = await sandbox.Shop.Index("GSI2", "gsi2pk", "gsi2sk").Query("ann").ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(found.Single().GetString("gsi2pk")).IsEqualTo("ann");
    }

    [Test]
    public async Task UpdateItem_CopyOfAnAttributeTheItemLacks_FailsWithValidationException()
    {
        // Arrange (an item without the attribute, and no item at all, fail alike)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = sandbox.Shop.Name, Item = Items.Values(("pk", "A"), ("sk", "A")) });

        // Act
        var onItem = await Items.FailureOfAsync<ValidationException>(() => CopyOwnerAsync(sandbox, "A"));
        var onNothing = await Items.FailureOfAsync<ValidationException>(() => CopyOwnerAsync(sandbox, "missing"));
        var stored = await sandbox.Shop.Get("A", "A").Consistent().ItemAsync();

        // Assert
        await Assert.That((onItem.Message, onNothing.Message, stored!.ContainsKey("gsi2pk")))
            .IsEqualTo(("The provided expression refers to an attribute that does not exist in the item", "The provided expression refers to an attribute that does not exist in the item", false));
    }

    [Test]
    public async Task UpdateItem_CopyOfANumberIntoAStringIndexKey_FailsWithValidationException()
    {
        // Arrange (why a number never qualifies: the index key is declared as a string)
        await using var sandbox = await ShopSandbox.StartAsync(backend);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = sandbox.Shop.Name, Item = Items.Values(("pk", "A"), ("sk", "A"), ("owner", AttributeValue.FromNumber(7))) });

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => CopyOwnerAsync(sandbox, "A"));

        // Assert
        await Assert.That(failure.Message).Contains("Type mismatch", StringComparison.Ordinal);
    }

    private static Task<UpdateItemResponse> CopyOwnerAsync(ShopSandbox sandbox, string pk) => sandbox.Client.UpdateItemAsync(new UpdateItemRequest
    {
        TableName = sandbox.Shop.Name,
        Key = Items.Values(("pk", pk), ("sk", "A")),
        UpdateExpression = "SET #k = #s",
        ExpressionAttributeNames = new Dictionary<string, string> { ["#k"] = "gsi2pk", ["#s"] = "owner" },
    });
}
