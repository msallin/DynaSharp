using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>
/// Which number spellings the emulators accept. <c>FromNumber&lt;double&gt;</c> and <c>WriteNumber&lt;double&gt;</c> write the
/// shortest round-trip text of a double, which can be <c>-0</c>, <c>1E+21</c> or <c>1E-07</c>; DynamoDB's documentation does not say
/// whether such spellings are valid, so the emulators' answers are pinned here. DynamoDB Local is AWS's own implementation.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class NumberFormatTests(Backend backend)
{
    [Test]
    public async Task FromNumber_DoubleNegativeZero_WritesMinusZero()
    {
        // Arrange (precondition of the tests below: this is the text that goes over the wire)
        var value = AttributeValue.FromNumber(-0.0);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("-0");
    }

    [Test]
    public async Task PutItem_NegativeZero_IsAcceptedAndStoredAsZero()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(-0.0));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("0");
    }

    [Test]
    public async Task PutItem_DoubleWithPositiveExponent_IsAcceptedAndNormalizedToDigits()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var written = AttributeValue.FromNumber(1e21);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, written);

        // Assert
        await Assert.That((written.AsNumber(), value.AsNumber())).IsEqualTo(("1E+21", "1000000000000000000000"));
    }

    [Test]
    public async Task PutItem_DoubleWithNegativeExponent_IsAcceptedAndNormalizedToADecimal()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var written = AttributeValue.FromNumber(1e-7);

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, written);

        // Assert
        await Assert.That((written.AsNumber(), value.AsNumber())).IsEqualTo(("1E-07", "0.0000001"));
    }

    [Test]
    [Arguments("1e5", "100000")]
    [Arguments("1E5", "100000")]
    [Arguments("+5", "5")]
    [Arguments(".5", "0.5")]
    [Arguments("5.", "5")]
    [Arguments("0005", "5")]
    [Arguments("1.50", "1.5")]
    public async Task PutItem_UnusualButValidSpelling_IsAcceptedAndNormalized(string spelling, string stored)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(spelling));

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo(stored);
    }

    [Test]
    [Arguments(" 5")]
    [Arguments("5 ")]
    [Arguments("0x10")]
    [Arguments("1_000")]
    [Arguments("five")]
    [Arguments("1,5")]
    public async Task PutItem_SpellingThatIsNotANumber_ThrowsValidationException(string spelling)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(spelling)));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    [Test]
    public async Task PutItem_DoubleMaxValue_ThrowsValidationException()
    {
        // Arrange: a double reaches 1.8E+308, DynamoDB stops at 9.99E+125
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(double.MaxValue)));

        // Assert
        await Assert.That(failure.Message).Contains("overflow");
    }

    [Test]
    public async Task PutItem_DoubleEpsilon_ThrowsValidationException()
    {
        // Arrange: the smallest positive double is 5E-324, DynamoDB stops at 1E-130
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(double.Epsilon)));

        // Assert
        await Assert.That(failure.Message).Contains("underflow");
    }

    [Test]
    public async Task PutItem_DoubleWithRepeatingFraction_RoundTripsExactly()
    {
        // Arrange: 0.1 + 0.2 prints as 0.30000000000000004, 17 significant digits, well inside DynamoDB's 38
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var value = await Items.RoundTripAsync(sandbox.Client, table, AttributeValue.FromNumber(0.1 + 0.2));

        // Assert
        await Assert.That(value.AsNumber<double>()).IsEqualTo(0.1 + 0.2);
    }
}
