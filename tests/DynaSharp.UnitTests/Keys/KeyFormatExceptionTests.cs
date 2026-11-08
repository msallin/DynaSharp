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

    public static IEnumerable<(FailingWrite Failing, string Reason)> Reasons() =>
    [
        (new("negative digits", "-1", static (ref KeyWriter writer) => writer.WriteDigits(-1L, 12, Name)), "The value is negative, which a zero-padded format cannot order."),
        (new("digits too wide", "1000", static (ref KeyWriter writer) => writer.WriteDigits(1000UL, 3, Name)), "The value has more digits than the width of its format."),
        (new("too many fraction digits", "1.234", static (ref KeyWriter writer) => writer.WriteFixedPoint(1.234m, 8, 2, Name)), "The value has more fraction digits than its format."),
        (new("out of range", "12345.67", static (ref KeyWriter writer) => writer.WriteFixedPoint(12345.67m, 4, 2, Name)), "The value is out of the range of its format."),
        (new("unspecified time", "2026", static (ref KeyWriter writer) => writer.WriteTimestamp(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Unspecified), KeyTimeFormat.Day, Name)), "The kind of the time is unspecified; use UTC or local time."),
        (new("never fits", "-", static (ref KeyWriter writer) => writer.WriteFormattable(default(NeverFits), "#"u8, Name)), "The formatted text is longer than the limit of 65536 bytes."),
    ];

    [Test]
    [MethodDataSource(nameof(Reasons))]
    public async Task Message_FailedWrite_SaysWhatIsWrongWithTheValue(FailingWrite failing, string reason)
    {
        // Arrange
        // Act
        var exception = Capture(failing);

        // Assert
        await Assert.That(exception.Message).StartsWith(reason, StringComparison.Ordinal);
    }

    [Test]
    public async Task InTemplate_ValueWithADelimiter_KeepsThatItHoldsOne()
    {
        // Arrange
        var exception = Capture(FailingWrites().First());

        // Act
        var inTemplate = exception.InTemplate("A#{Name}", "#").InTemplate("B#{Name}", "#");

        // Assert
        await Assert.That(inTemplate.Message).StartsWith("The value contains a delimiter of the key template 'B#{Name}' ('#').", StringComparison.Ordinal);
    }

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
    [MethodDataSource(nameof(FailingWrites))]
    public async Task InTemplate_FailedWrite_NamesNoValue(FailingWrite failing)
    {
        // Arrange
        var exception = Capture(failing);

        // Act
        var named = exception.InTemplate("USER#{Name}", "#");

        // Assert
        await Assert.That((named.Message.Contains("'USER#{Name}'", StringComparison.Ordinal), named.Message.Contains(failing.Secret, StringComparison.Ordinal), named.ParamName)).IsEqualTo((true, false, Name));
    }

    [Test]
    public async Task InTemplate_Delimiter_NamesTheDelimitersOfTheTemplateAndNotWhichOneTheValueHolds()
    {
        // Arrange
        var exception = Capture(FailingWrites().First());

        // Act
        var named = exception.InTemplate("{A}-{B}.{Name}", "-.");

        // Assert
        await Assert.That(named.Message).IsEqualTo("The value contains a delimiter of the key template '{A}-{B}.{Name}' ('-.'). (Parameter 'Name')");
    }

    [Test]
    public async Task InTemplate_OtherProblem_AddsTheTemplateToTheReason()
    {
        // Arrange
        var exception = new KeyFormatException(Name, "The value is empty.");

        // Act
        var named = exception.InTemplate("USER#{Name}", "#");

        // Assert
        await Assert.That(named.Message).IsEqualTo("The value is empty. The key template is 'USER#{Name}'. (Parameter 'Name')");
    }

    [Test]
    public async Task InTemplate_MessageOnly_KeepsTheException()
    {
        // Arrange (no writer makes one, but a type that formats itself could throw it)
        var exception = new KeyFormatException("The text is not a key.");

        // Act
        var named = exception.InTemplate("USER#{Name}", "#");

        // Assert
        await Assert.That(ReferenceEquals(named, exception)).IsTrue();
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
