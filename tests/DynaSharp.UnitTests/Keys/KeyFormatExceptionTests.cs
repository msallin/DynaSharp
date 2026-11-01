using DynaSharp.Keys;

namespace DynaSharp.UnitTests.Keys;

public sealed class KeyFormatExceptionTests
{
    private const string Name = "Name";

    public static IEnumerable<FailingWrite> FailingWrites() =>
    [
        new("string with a delimiter", "S3cr3t#Value", static (ref KeyWriter writer) => writer.WriteString("S3cr3t#Value", "#"u8, Name)),
        new("string with a control character", "S3cr3t\u0001Value", static (ref KeyWriter writer) => writer.WriteString("S3cr3t\u0001Value", "#"u8, Name)),
        new("string with a lone surrogate", "S3cr3t", static (ref KeyWriter writer) => writer.WriteString("S3cr3t\uD800", "#"u8, Name)),
        new("utf-8 with a delimiter", "S3cr3t#Value", static (ref KeyWriter writer) => writer.WriteUtf8Value("S3cr3t#Value"u8, "#"u8, Name)),
        new("integer with a delimiter", "987654321", static (ref KeyWriter writer) => writer.WriteInteger(987654321L, "5"u8, Name)),
        new("unsigned integer with a delimiter", "987654321", static (ref KeyWriter writer) => writer.WriteInteger(987654321UL, "5"u8, Name)),
        new("negative digits", "987654321", static (ref KeyWriter writer) => writer.WriteDigits(-987654321L, 12, Name)),
        new("digits too wide", "987654321", static (ref KeyWriter writer) => writer.WriteDigits(987654321UL, 3, Name)),
        new("decimal with a delimiter", "987654.321", static (ref KeyWriter writer) => writer.WriteDecimal(987654.321m, "."u8, Name)),
        new("fixed point with too many fraction digits", "12345.678", static (ref KeyWriter writer) => writer.WriteFixedPoint(12345.678m, 8, 2, Name)),
        new("fixed point out of range", "12345.67", static (ref KeyWriter writer) => writer.WriteFixedPoint(12345.67m, 4, 2, Name)),
        new("unspecified date time", "2026-10-31", static (ref KeyWriter writer) => writer.WriteTimestamp(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Unspecified), KeyTimeFormat.Day, Name)),
        new("formattable with a delimiter", "S3cr3t#Value", static (ref KeyWriter writer) => writer.WriteFormattable(new RawText("S3cr3t#Value"), "#"u8, Name)),
    ];

    [Test]
    [MethodDataSource(nameof(FailingWrites))]
    public async Task Message_FailedWrite_NamesNoValue(FailingWrite failing)
    {
        // Arrange
        // Act
        var exception = Capture(failing);

        // Assert
        await Assert.That(exception.Message).DoesNotContain(failing.Secret, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(FailingWrites))]
    public async Task ParamName_FailedWrite_IsThePlaceholder(FailingWrite failing)
    {
        // Arrange
        // Act
        var exception = Capture(failing);

        // Assert
        await Assert.That(exception.ParamName).IsEqualTo(Name);
    }

    [Test]
    [MethodDataSource(nameof(FailingWrites))]
    public async Task Message_FailedWrite_NamesThePlaceholder(FailingWrite failing)
    {
        // Arrange
        // Act
        var exception = Capture(failing);

        // Assert
        await Assert.That(exception.Message).Contains($"'{Name}'", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_MessageOnly_HasNoParameterName()
    {
        // Arrange
        // Act
        var exception = new KeyFormatException("The text is not a key.");

        // Assert
        await Assert.That((exception.Message, exception.ParamName)).IsEqualTo(("The text is not a key.", null));
    }

    [Test]
    public async Task Constructor_PlaceholderAndReason_ReportsBoth()
    {
        // Arrange
        // Act
        var exception = new KeyFormatException("OrderId", "The value is empty.");

        // Assert
        await Assert.That((exception.ParamName, exception.Message)).IsEqualTo(("OrderId", "The value is empty. (Parameter 'OrderId')"));
    }

    [Test]
    public async Task KeyFormatException_Type_IsAnArgumentException()
    {
        // Arrange
        var exception = new KeyFormatException("The text is not a key.");

        // Act
        var isArgumentException = exception is ArgumentException;

        // Assert
        await Assert.That(isArgumentException).IsTrue();
    }

    private static KeyFormatException Capture(FailingWrite failing)
    {
        try
        {
            KeyVectorRunner.Write(failing.Action, 64);
        }
        catch (KeyFormatException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"'{failing.Scenario}' did not fail.");
    }
}
