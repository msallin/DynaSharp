using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>
/// A value of its type that a built-in converter refuses on write, reported by the generated write with the type, the property and the attribute, what was wrong and the fix, and no value.
/// A test service that left a <see cref="DateTime"/> unset met this first: <c>default(DateTime)</c> has <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
public sealed class GeneratedWriteRefusalTests
{
    private const string Fix = "its DateTime has DateTimeKind.Unspecified, so it names no instant. Give it DateTimeKind.Utc, or store it with [DynamoDbConverter(typeof(SdkDateTimeConverter))], which takes it for local time as the AWS SDK does.";

    private const string NotFinite = "it is NaN or an infinity, which DynamoDB cannot store. Give it a finite number.";

    private static readonly DateTime NoKind = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Unspecified);

    private static readonly Logbook Clean = new() { Id = "l1" };

    public static IEnumerable<Func<(Logbook Logbook, string Message)>> DateTimesOfNoKind() =>
    [
        () => (Clean with { LoggedAt = NoKind }, "The property LoggedAt of Logbook, the attribute \"logged_at\", cannot be written: " + Fix),
        () => (Clean with { SignedAt = NoKind }, "The property SignedAt of Logbook, the attribute \"SignedAt\", cannot be written: " + Fix),
        () => (Clean with { Watches = [Logbook.Utc, NoKind] }, "The property Watches of Logbook, the attribute \"Watches\", cannot be written: " + Fix),
        () => (Clean with { On = NoKind }, "The property On of Logbook, the attribute \"On\", cannot be written: " + Fix),
        () => (Clean with { Last = new LogEntry { At = default } }, "The property At of LogEntry, the attribute \"At\", cannot be written: " + Fix),
    ];

    public static IEnumerable<Func<(Logbook Logbook, string Message)>> NumbersThatAreNotFinite() =>
    [
        () => (Clean with { Depth = double.NaN }, "The property Depth of Logbook, the attribute \"Depth\", cannot be written: " + NotFinite),
        () => (Clean with { Depth = double.NegativeInfinity }, "The property Depth of Logbook, the attribute \"Depth\", cannot be written: " + NotFinite),
        () => (Clean with { Soundings = [1.5, double.PositiveInfinity] }, "The property Soundings of Logbook, the attribute \"Soundings\", cannot be written: " + NotFinite),
    ];

    [Test]
    [MethodDataSource(nameof(DateTimesOfNoKind))]
    public async Task ItemFrom_DateTimeOfNoKind_ThrowsArgumentExceptionNamingThePropertyAndTheFix(Logbook logbook, string message)
    {
        // Arrange
        // Act
        var act = () => Item.From(logbook);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NumbersThatAreNotFinite))]
    public async Task ItemFrom_NumberThatIsNotFinite_ThrowsArgumentOutOfRangeExceptionNamingTheProperty(Logbook logbook, string message)
    {
        // Arrange
        // Act
        var act = () => Item.From(logbook);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_PutOfAnUnsetDateTime_NamesThePropertyAndKeepsTheConverterFailureInside()
    {
        // Arrange (default(DateTime) is the time of no kind that test data holds when nobody set it)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var logbook = Clean with { LoggedAt = default };

        // Act
        var failure = TestClient.Catch(() => _ = client.Table<Logbook>("Shop").Put(logbook).ToRequestJson());

        // Assert
        await Assert.That((failure.Message, failure.InnerException?.GetType().Name)).IsEqualTo(("The property LoggedAt of Logbook, the attribute \"logged_at\", cannot be written: " + Fix, "UnspecifiedDateTimeException"));
    }

    [Test]
    public async Task ItemFrom_NullListOfDateTimes_ThrowsWhatTheConverterThrows()
    {
        // Arrange (the catch filters on the refusals, so any other failure goes on as it is)
        var logbook = Clean with { Watches = null! };

        // Act
        var act = () => Item.From(logbook);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ItemFrom_DateTimeOfNoKindThroughSdkDateTimeConverter_IsWrittenAsLocalTime()
    {
        // Arrange (the converter that takes it for local time, as the AWS SDK does, refuses nothing)
        var logbook = Clean with { Noted = NoKind };

        // Act
        var item = Item.From(logbook);

        // Assert
        await Assert.That(item["Noted"].AsString()).IsEqualTo(DateTime.SpecifyKind(NoKind, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }
}
