using DynaSharp.DefaultsFixture;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>
/// The opt-in converters of the library as defaults of an assembly, in a real build: <see cref="Payment"/> of <c>DynaSharp.DefaultsFixture</c>, which takes
/// <c>BoolAsNumberConverter</c>, <c>SdkDateTimeConverter</c> and <c>EmptyStringAsMissingConverter&lt;Guid, GuidConverter&gt;</c> from <c>[assembly: DynamoDbDefaultConverter]</c>.
/// </summary>
public sealed class OptInConverterDefaultsBuildTests
{
    private static readonly Guid Receipt = new("0b5d1d6e-6c4e-4b8a-9d55-7a1d3c2f4e10");

    [Test]
    public async Task ToRequestJson_PutOfAPayment_StoresTheFormsOfTheSdk()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var payment = new Payment { PaymentId = "p1", Settled = true, PaidAt = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567), Receipt = Receipt };

        // Act
        var json = client.Table<Payment>("Shop").Put(payment).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "settled":{"N":"1"},"paid_at":{"S":"2026-10-31T12:00:00.123Z"},"receipt":{"S":"0b5d1d6e-6c4e-4b8a-9d55-7a1d3c2f4e10"}
            """);
    }

    [Test]
    public async Task ItemAsync_ItemTheSdkWrote_ReadsThroughTheDefaults()
    {
        // Arrange (a receipt stored as "" counts as missing, and a nullable reads null)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"payment_id":{"S":"p1"},"settled":{"N":"1"},"paid_at":{"S":"2026-10-31T12:00:00.123Z"},"receipt":{"S":""},"local_time":{"S":"2026-10-31T13:00:00.0000000+01:00"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var payment = await client.Table<Payment>("Shop").Get(Payment.Keys.Of("p1")).ItemAsync();

        // Assert
        await Assert.That((payment!.Settled, payment.PaidAt, payment.PaidAt.Kind, payment.Receipt, payment.LocalTime.Offset))
            .IsEqualTo((true, new DateTime(2026, 10, 31, 12, 0, 0, 123, DateTimeKind.Utc), DateTimeKind.Utc, (Guid?)null, TimeSpan.FromHours(1)));
    }

    [Test]
    public async Task ItemAsync_EmptyStringInAListThroughTheDefault_ThrowsBecauseAnElementIsNotAsked()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"payment_id":{"S":"p1"},"refunds":{"L":[{"S":""}]}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Payment>("Shop").Get(Payment.Keys.Of("p1")).ItemAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("refunds");
    }
}
