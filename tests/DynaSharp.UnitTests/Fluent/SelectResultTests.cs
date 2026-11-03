using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What a read of one attribute gives without a look at <see cref="SelectResult{TValue}.Found"/>: <c>GetValueOrDefault</c> for no item, and <c>Value</c>, which throws there.</summary>
public sealed class SelectResultTests
{
    [Test]
    public async Task GetValueOrDefault_ItemFound_GivesTheValue()
    {
        // Arrange
        var result = new SelectResult<int>(7, found: true, null);

        // Act
        var value = result.GetValueOrDefault();

        // Assert
        await Assert.That(value).IsEqualTo(7);
    }

    [Test]
    public async Task GetValueOrDefault_NoItem_GivesTheDefaultOfTheType()
    {
        // Arrange
        var result = new SelectResult<int>(default, found: false, null);

        // Act
        var value = result.GetValueOrDefault();

        // Assert
        await Assert.That(value).IsEqualTo(0);
    }

    [Test]
    public async Task GetValueOrDefault_NoItemOfAReference_GivesNull()
    {
        // Arrange
        var result = new SelectResult<string>(null!, found: false, null);

        // Act
        var value = result.GetValueOrDefault();

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    [Arguments(int.MinValue)]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(int.MaxValue)]
    public async Task GetValueOrDefaultWithAFallback_ItemFound_GivesTheValueAndNotTheFallback(int stored)
    {
        // Arrange
        var result = new SelectResult<int>(stored, found: true, null);

        // Act
        var value = result.GetValueOrDefault(42);

        // Assert
        await Assert.That(value).IsEqualTo(stored);
    }

    [Test]
    public async Task GetValueOrDefaultWithAFallback_NoItem_GivesTheFallback()
    {
        // Arrange
        var result = new SelectResult<decimal>(default, found: false, null);

        // Act
        var value = result.GetValueOrDefault(-1m);

        // Assert
        await Assert.That(value).IsEqualTo(-1m);
    }

    [Test]
    public async Task GetValueOrDefaultWithAFallback_ItemWithoutTheNullableAttribute_GivesNullAndNotTheFallback()
    {
        // Arrange (the item exists, so its null is the answer)
        var result = new SelectResult<string?>(null, found: true, null);

        // Act
        var value = result.GetValueOrDefault("none");

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Value_NoItem_Throws()
    {
        // Arrange
        var result = new SelectResult<int>(default, found: false, null);

        // Act
        var failure = TestClient.Catch(() => _ = result.Value);

        // Assert
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task GetValueOrDefault_GetOfAnItemThatDoesNotExist_GivesTheFallback()
    {
        // Arrange (the use case: a due date with a fallback for an invoice that was deleted)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        var result = await client.Table<TenantInvoice>("Billing").Get(TenantInvoice.Keys.Of("acme", "2026-09", 1)).Select(TenantInvoice.Attrs.DueDate);

        // Assert
        await Assert.That(result.GetValueOrDefault(DateOnly.MinValue)).IsEqualTo(DateOnly.MinValue);
    }
}
