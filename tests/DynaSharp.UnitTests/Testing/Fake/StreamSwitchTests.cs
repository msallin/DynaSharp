using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

// FakeDynamoDb.EnableStream and DisableStream, the StreamSpecification of an UpdateTable without a client: a new stream on each enable, closed shards on a disable.
public sealed class StreamSwitchTests
{
    private const string SecondLabel = "2026-01-01T00:00:01.000";

    [Test]
    public async Task DisableStream_EnabledStream_ClosesItsShardAndDescribesItDisabled()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        fake.DisableStream("Users");

        // Assert
        var description = Describe(fake, FakeStreams.UsersArn);
        await Assert.That((description.GetProperty("StreamStatus").GetString(), Shard(description).TryGetProperty("EndingSequenceNumber", out _))).IsEqualTo(("DISABLED", true));
    }

    [Test]
    public async Task DisableStream_EnabledStream_DescribesTheTableWithoutAStreamSpecificationAndWithItsLastStream()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        var table = fake.DisableStream("Users");

        // Assert
        await Assert.That((table.StreamSpecification, table.LatestStreamArn)).IsEqualTo(((StreamSpecification?)null, FakeStreams.UsersArn));
    }

    [Test]
    public async Task DisableStream_WhileTheStreamIsRead_EndsTheReadingWithTheRecordsBefore()
    {
        // Arrange: the clock of the fake never moves, so the reading can only end through the disable
        var fake = FakeStreams.UsersOn(Clock());
        FakeStreams.Put(fake, "a", 1);
        using var streams = fake.CreateStreamsClient();
        var reading = streams.ReadStreamAsync(new ReadStreamRequest { StreamArn = FakeStreams.UsersArn, ShardIteratorType = ShardIteratorType.TrimHorizon }).ToListAsync().AsTask();

        // Act
        fake.DisableStream("Users");

        // Assert
        await Assert.That((await reading).Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisableStream_TableWithoutAnEnabledStream_ThrowsValidationException()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var act = () => fake.DisableStream("Users");

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>().WithMessage("Table has no stream to disable", StringComparison.Ordinal);
    }

    [Test]
    public async Task DisableStream_NoSuchTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        var fake = new FakeDynamoDb(Clock());

        // Act
        var act = () => fake.DisableStream("Users");

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task EnableStream_AfterADisable_GivesANewStreamWithItsOwnArnAndLabel()
    {
        // Arrange
        var time = Clock();
        var fake = FakeStreams.UsersOn(time);
        fake.DisableStream("Users");
        time.Advance(TimeSpan.FromSeconds(1));

        // Act
        var table = fake.EnableStream("Users", StreamViewType.KeysOnly);

        // Assert
        await Assert.That((table.LatestStreamLabel, table.LatestStreamArn, table.StreamSpecification?.StreamViewType))
            .IsEqualTo((SecondLabel, FakeStreams.UsersArn.Replace(FakeStreams.Label, SecondLabel, StringComparison.Ordinal), StreamViewType.KeysOnly));
    }

    [Test]
    public async Task EnableStream_TableWithoutAStream_RecordsTheWritesFromThenOnWithTheViewType()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeStreams.Put(fake, "before", 1);
        var arn = fake.EnableStream("Users", StreamViewType.KeysOnly).LatestStreamArn!;

        // Act
        FakeStreams.Put(fake, "after", 1);

        // Assert
        var record = FakeStreams.Records(fake, arn).Single().GetProperty("dynamodb");
        await Assert.That((record.GetProperty("Keys").GetProperty("pk").GetProperty("S").GetString(), record.TryGetProperty("NewImage", out _))).IsEqualTo(("after", false));
    }

    [Test]
    public async Task EnableStream_EnabledStream_ThrowsValidationException()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        var act = () => fake.EnableStream("Users", StreamViewType.NewImage);

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>().WithMessage("Table already has an enabled stream: TableName: Users", StringComparison.Ordinal);
    }

    [Test]
    public async Task EnableStream_NoSuchTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        var fake = new FakeDynamoDb(Clock());

        // Act
        var act = () => fake.EnableStream("Users", StreamViewType.NewImage);

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    [Arguments(StreamViewType.Unspecified)]
    [Arguments((StreamViewType)99)]
    public async Task EnableStream_ViewTypeOutOfRange_ThrowsArgumentOutOfRangeException(StreamViewType viewType)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var act = () => fake.EnableStream("Users", viewType);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task EnableStream_NullTableName_ThrowsArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb(Clock());

        // Act
        var act = () => fake.EnableStream(null!, StreamViewType.NewImage);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task DisableStream_NullTableName_ThrowsArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb(Clock());

        // Act
        var act = () => fake.DisableStream(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static JsonElement Describe(FakeDynamoDb fake, string arn) =>
        FakeCalls.Ok(fake, "DescribeStream", $$"""{"StreamArn":"{{arn}}"}""").GetProperty("StreamDescription");

    private static JsonElement Shard(JsonElement description) => description.GetProperty("Shards")[0].GetProperty("SequenceNumberRange");
}
