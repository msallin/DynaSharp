using System.Net;
using System.Text.Json;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

// FakeDynamoDb.SplitStreamShards and what the wire shows of it: the lineage of DescribeStream, a child listed late, the records of the children, a shard gone 24 hours
// after it closed. The first shard of Users is FakeStreams.FirstShard; the children of a split get the next shard numbers of the fake, at the time of the split.
public sealed class ShardSplitTests
{
    private const string Child1 = "shardId-00000001767225600000-00000002";
    private const string Child2 = "shardId-00000001767225600000-00000003";

    [Test]
    public async Task SplitStreamShards_TwoChildren_ListsThemAfterTheirClosedParent()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        fake.SplitStreamShards("Users");

        // Assert
        await Assert.That(string.Join(",", Shards(fake, string.Empty).Select(Lineage))).IsEqualTo($"{FakeStreams.FirstShard}<-:closed,{Child1}<-{FakeStreams.FirstShard}:open,{Child2}<-{FakeStreams.FirstShard}:open");
    }

    [Test]
    public async Task SplitStreamShards_OneChild_ListsOneChild()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        fake.SplitStreamShards("Users", children: 1);

        // Assert
        await Assert.That(string.Join(",", Shards(fake, ChildrenOf(FakeStreams.FirstShard)).Select(static shard => shard.GetProperty("ShardId").GetString()))).IsEqualTo(Child1);
    }

    [Test]
    public async Task DescribeStream_ChildShardsFilter_ListsTheChildrenOfThatShardOnly()
    {
        // Arrange: the children split again, so the first shard has two children and four grandchildren
        var fake = FakeStreams.UsersOn(Clock());
        fake.SplitStreamShards("Users");
        fake.SplitStreamShards("Users");

        // Act
        var children = Shards(fake, ChildrenOf(Child1));

        // Assert
        await Assert.That(children.Select(static shard => shard.GetProperty("ParentShardId").GetString()).Distinct().Single()).IsEqualTo(Child1);
    }

    [Test]
    [Arguments(2, 2)]
    [Arguments(3, 3)]
    public async Task SplitStreamShards_SecondChildListedLate_IsListedOnceItsTimeHasCome(int seconds, int listed)
    {
        // Arrange
        var time = Clock();
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users", secondChildListedAfter: TimeSpan.FromSeconds(3));

        // Act
        time.Advance(TimeSpan.FromSeconds(seconds));

        // Assert
        await Assert.That(Shards(fake, string.Empty).Count).IsEqualTo(listed);
    }

    [Test]
    public async Task DescribeStream_LimitBelowTheShards_EndsThePageAtTheLastShardListed()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());
        fake.SplitStreamShards("Users");

        // Act
        var description = Describe(fake, ""","Limit":2""");

        // Assert
        await Assert.That((description.GetProperty("Shards").GetArrayLength(), description.GetProperty("LastEvaluatedShardId").GetString())).IsEqualTo((2, Child1));
    }

    [Test]
    public async Task SplitStreamShards_WritesAfterTheSplit_GoToTheChildrenAndNotToTheParent()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());
        fake.SplitStreamShards("Users");

        // Act
        foreach (var key in new[] { "a", "b", "c", "d", "e", "f", "g", "h" })
        {
            FakeStreams.Put(fake, key, 1);
        }

        // Assert
        await Assert.That((Count(fake, FakeStreams.FirstShard), Count(fake, Child1) + Count(fake, Child2), Count(fake, Child1) > 0, Count(fake, Child2) > 0)).IsEqualTo((0, 8, true, true));
    }

    [Test]
    public async Task GetShardIterator_StartingSequenceNumberOfAChild_ReadsItsFirstRecord()
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());
        fake.SplitStreamShards("Users", children: 1);
        FakeStreams.Put(fake, "a", 1);
        var start = Shards(fake, ChildrenOf(FakeStreams.FirstShard)).Single().GetProperty("SequenceNumberRange").GetProperty("StartingSequenceNumber").GetString()!;

        // Act
        var records = FakeStreams.Page(fake, Iterator(fake, Child1, "AT_SEQUENCE_NUMBER", start)).GetProperty("Records");

        // Assert
        await Assert.That(FakeStreams.Names(records.EnumerateArray())).IsEquivalentTo(["INSERT"]);
    }

    [Test]
    public async Task GetShardIterator_StartingSequenceNumberOfAChildWhoseRecordsAreTrimmed_AnswersTrimmedDataAccess()
    {
        // Arrange
        var time = Clock();
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users", children: 1);
        FakeStreams.Put(fake, "a", 1);
        var start = Shards(fake, ChildrenOf(FakeStreams.FirstShard)).Single().GetProperty("SequenceNumberRange").GetProperty("StartingSequenceNumber").GetString()!;
        time.Advance(TimeSpan.FromHours(24));

        // Act
        var answer = FakeCalls.Error(fake, "GetShardIterator", IteratorRequest(Child1, "AT_SEQUENCE_NUMBER", start));

        // Assert
        await Assert.That(answer.Code).IsEqualTo("TrimmedDataAccessException");
    }

    [Test]
    public async Task GetShardIterator_ShardClosed24HoursAgo_AnswersResourceNotFound()
    {
        // Arrange
        var time = Clock();
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users", children: 1);
        time.Advance(TimeSpan.FromHours(24));

        // Act
        var answer = FakeCalls.Error(fake, "GetShardIterator", IteratorRequest(FakeStreams.FirstShard, "TRIM_HORIZON"));

        // Assert
        await Assert.That((answer.Code, string.Join(",", Shards(fake, string.Empty).Select(static shard => shard.GetProperty("ShardId").GetString()))))
            .IsEqualTo(("ResourceNotFoundException", Child1));
    }

    [Test]
    public async Task GetRecords_IteratorOfAShardThatIsGone_AnswersValidation()
    {
        // Arrange
        var time = Clock();
        var fake = FakeStreams.UsersOn(time);
        fake.SplitStreamShards("Users", children: 1);
        time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        var iterator = FakeStreams.Iterator(fake, "TRIM_HORIZON");
        time.Advance(TimeSpan.FromMinutes(2));

        // Act
        var answer = FakeCalls.Error(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "Invalid ShardIterator"));
    }

    [Test]
    public async Task SplitStreamShards_NoSuchTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        var fake = new FakeDynamoDb(Clock());

        // Act
        var act = () => fake.SplitStreamShards("Users");

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task SplitStreamShards_TableWithoutAnEnabledStream_ThrowsValidationException()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var act = () => fake.SplitStreamShards("Users");

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>().WithMessage("The table Users has no enabled stream to split.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(3, 0)]
    [Arguments(2, -1)]
    public async Task SplitStreamShards_ChildrenOrDelayOutOfRange_ThrowsArgumentOutOfRangeException(int children, int seconds)
    {
        // Arrange
        var fake = FakeStreams.UsersOn(Clock());

        // Act
        var act = () => fake.SplitStreamShards("Users", children, TimeSpan.FromSeconds(seconds));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SplitStreamShards_NullTableName_ThrowsArgumentNullException()
    {
        // Arrange
        var fake = new FakeDynamoDb(Clock());

        // Act
        var act = () => fake.SplitStreamShards(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    private static ManualTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static string ChildrenOf(string shardId) => $$""","ShardFilter":{"Type":"CHILD_SHARDS","ShardId":"{{shardId}}"}""";

    private static JsonElement Describe(FakeDynamoDb fake, string members) =>
        FakeCalls.Ok(fake, "DescribeStream", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}"{{members}}}""").GetProperty("StreamDescription");

    private static List<JsonElement> Shards(FakeDynamoDb fake, string members) => [.. Describe(fake, members).GetProperty("Shards").EnumerateArray()];

    // <id><-<parent>:open or closed.
    private static string Lineage(JsonElement shard) =>
        shard.GetProperty("ShardId").GetString() + "<-" + (shard.TryGetProperty("ParentShardId", out var parent) ? parent.GetString() : string.Empty)
        + (shard.GetProperty("SequenceNumberRange").TryGetProperty("EndingSequenceNumber", out _) ? ":closed" : ":open");

    private static string IteratorRequest(string shardId, string type, string? sequenceNumber = null) =>
        $$"""{"StreamArn":"{{FakeStreams.UsersArn}}","ShardId":"{{shardId}}","ShardIteratorType":"{{type}}"{{(sequenceNumber is null ? string.Empty : $",\"SequenceNumber\":\"{sequenceNumber}\"")}}}""";

    private static string Iterator(FakeDynamoDb fake, string shardId, string type, string? sequenceNumber = null) =>
        FakeCalls.Ok(fake, "GetShardIterator", IteratorRequest(shardId, type, sequenceNumber)).GetProperty("ShardIterator").GetString()!;

    private static int Count(FakeDynamoDb fake, string shardId) =>
        FakeStreams.Page(fake, Iterator(fake, shardId, "TRIM_HORIZON")).GetProperty("Records").GetArrayLength();
}
