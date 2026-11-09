using System.Net;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The bodies follow the examples of the DynamoDB Streams API reference for ListStreams, DescribeStream, GetShardIterator and GetRecords, and the behavior is that of the
// Developer Guide: one record per change in the order of the writes, iterators valid for 15 minutes, records kept for 24 hours, the shard of a disabled stream closed.
public sealed class StreamOperationsTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task HandleListStreams_TableWithStream_ListsItsStream()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        var body = FakeCalls.Ok(fake, "ListStreams", """{"TableName":"Users"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson($$"""{"Streams":[{"StreamArn":"{{FakeStreams.UsersArn}}","TableName":"Users","StreamLabel":"{{FakeStreams.Label}}"}]}""", body)).IsTrue();
    }

    [Test]
    public async Task HandleListStreams_NoTableName_ListsTheStreamsOfEveryTableInTheOrderTheyWereEnabled()
    {
        // Arrange: Orders without a stream until an UpdateTable enables one, after the stream of Users.
        var fake = FakeCalls.With(FakeCalls.Orders, FakeStreams.Users("KEYS_ONLY"));
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Orders","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_IMAGE"}}""");

        // Act
        var body = FakeCalls.Ok(fake, "ListStreams", "{}");

        // Assert
        await Assert.That(body.GetProperty("Streams").EnumerateArray().Select(static stream => stream.GetProperty("TableName").GetString()!)).IsEquivalentTo(["Users", "Orders"]);
    }

    [Test]
    public async Task HandleListStreams_TableWithoutStream_ListsNone()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "ListStreams", """{"TableName":"Users"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Streams":[]}""", body)).IsTrue();
    }

    [Test]
    public async Task HandleListStreams_NoSuchTable_ListsNone()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var body = FakeCalls.Ok(fake, "ListStreams", """{"TableName":"Users"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Streams":[]}""", body)).IsTrue();
    }

    [Test]
    public async Task HandleListStreams_DeletedTable_ListsItsStreamFor24Hours()
    {
        // Arrange: the API reference of DeleteTable: the stream of a deleted table is disabled and deleted after 24 hours.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeCalls.Ok(fake, "DeleteTable", """{"TableName":"Users"}""");
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMilliseconds(1));

        // Act
        var before = FakeCalls.Ok(fake, "ListStreams", """{"TableName":"Users"}""").GetProperty("Streams").GetArrayLength();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var after = FakeCalls.Ok(fake, "ListStreams", """{"TableName":"Users"}""").GetProperty("Streams").GetArrayLength();

        // Assert
        await Assert.That((before, after)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task HandleListStreams_Limit_PagesWithLastEvaluatedStreamArn()
    {
        // Arrange: two streams of Users, enabled at the same moment, so the second label is a millisecond later.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"KEYS_ONLY"}}""");
        const string Second = "arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/2026-01-01T00:00:00.001";

        // Act
        var first = FakeCalls.Ok(fake, "ListStreams", """{"Limit":1}""");
        var next = FakeCalls.Ok(fake, "ListStreams", $$"""{"Limit":1,"ExclusiveStartStreamArn":"{{first.GetProperty("LastEvaluatedStreamArn").GetString()}}"}""");

        // Assert
        await Assert.That((first.GetProperty("Streams")[0].GetProperty("StreamArn").GetString(), next.GetProperty("Streams")[0].GetProperty("StreamArn").GetString(),
            next.TryGetProperty("LastEvaluatedStreamArn", out _))).IsEqualTo((FakeStreams.UsersArn, Second, false));
    }

    [Test]
    public async Task HandleListStreams_UnknownExclusiveStartStreamArn_ListsNone()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        const string Unknown = "arn:aws:dynamodb:us-east-1:123456789012:table/Other/stream/" + FakeStreams.Label;

        // Act
        var body = FakeCalls.Ok(fake, "ListStreams", $$"""{"ExclusiveStartStreamArn":"{{Unknown}}"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Streams":[]}""", body)).IsTrue();
    }

    [Test]
    [Arguments("""{"Limit":0}""", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments("""{"Limit":101}""", "1 validation error detected: Value '101' at 'limit' failed to satisfy constraint: Member must have value less than or equal to 100")]
    [Arguments("""{"TableName":"U s"}""", "1 validation error detected: Value 'U s' at 'tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    [Arguments("""{"ExclusiveStartStreamArn":"arn"}""", "1 validation error detected: Value 'arn' at 'exclusiveStartStreamArn' failed to satisfy constraint: Member must have length greater than or equal to 37")]
    public async Task HandleListStreams_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "ListStreams", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    public async Task HandleDescribeStream_EnabledStream_DescribesItWithItsOpenShard()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("NEW_IMAGE"));

        // Act
        var body = FakeCalls.Ok(fake, "DescribeStream", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}"}""");

        // Assert
        var expected = $$$"""
            {"StreamDescription":{"StreamArn":"{{{FakeStreams.UsersArn}}}","StreamLabel":"{{{FakeStreams.Label}}}","StreamStatus":"ENABLED","StreamViewType":"NEW_IMAGE",
             "CreationRequestDateTime":1767225600,"TableName":"Users","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],
             "Shards":[{"ShardId":"{{{FakeStreams.FirstShard}}}","SequenceNumberRange":{"StartingSequenceNumber":"{{{FakeStreams.FirstSequenceNumber}}}"}}]}}
            """;
        await Assert.That(FakeCalls.SameJson(expected, body)).IsTrue();
    }

    [Test]
    public async Task HandleDescribeStream_DisabledStream_IsDisabledWithAClosedShard()
    {
        // Arrange: one record, then the stream switched off.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");

        // Act
        var description = FakeCalls.Ok(fake, "DescribeStream", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}"}""").GetProperty("StreamDescription");

        // Assert
        var range = description.GetProperty("Shards")[0].GetProperty("SequenceNumberRange");
        await Assert.That((description.GetProperty("StreamStatus").GetString(), range.GetProperty("EndingSequenceNumber").GetString()))
            .IsEqualTo(("DISABLED", "100000000000000000001"));
    }

    [Test]
    [Arguments(""","ShardFilter":{"Type":"CHILD_SHARDS","ShardId":"shardId-00000001767225600000-00000001"}""", 0)]
    [Arguments(""","ExclusiveStartShardId":"shardId-00000001767225600000-00000001","Limit":100""", 0)]
    [Arguments(""","ExclusiveStartShardId":"shardId-0","Limit":1""", 1)]
    public async Task HandleDescribeStream_FilterOrStart_ListsTheShardsAfterIt(string members, int shards)
    {
        // Arrange: a shard that never split has no children, and the shards after a start are those whose id sorts after it.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        var body = FakeCalls.Ok(fake, "DescribeStream", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}"{{members}}}""");

        // Assert
        await Assert.That(body.GetProperty("StreamDescription").GetProperty("Shards").GetArrayLength()).IsEqualTo(shards);
    }

    [Test]
    public async Task HandleDescribeStream_UnknownStream_AnswersResourceNotFound()
    {
        // Arrange: the ARN of the integration tests, of an account nobody has.
        var fake = FakeCalls.With();
        const string Arn = "arn:aws:dynamodb:us-east-1:000000000000:table/no-such-table/stream/2020-01-01T00:00:00.000";

        // Act
        var answer = FakeCalls.Error(fake, "DescribeStream", $$"""{"StreamArn":"{{Arn}}"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", $"Requested resource not found: Stream: {Arn} not found"));
    }

    [Test]
    public async Task HandleDescribeStream_24HoursAfterTheStreamWasDisabled_AnswersResourceNotFound()
    {
        // Arrange
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");
        clock.Advance(TimeSpan.FromHours(24));

        // Act
        var answer = FakeCalls.Error(fake, "DescribeStream", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}"}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    [Arguments("""{"StreamArn":"arn"}""", "1 validation error detected: Value 'arn' at 'streamArn' failed to satisfy constraint: Member must have length greater than or equal to 37")]
    [Arguments("""{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","Limit":0}""", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments("""{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","Limit":101}""", "1 validation error detected: Value '101' at 'limit' failed to satisfy constraint: Member must have value less than or equal to 100")]
    [Arguments("""{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ShardFilter":{"Type":"PARENT_SHARDS","ShardId":"s"}}""", "1 validation error detected: Value 'PARENT_SHARDS' at 'type' failed to satisfy constraint: Member must satisfy enum value set: [CHILD_SHARDS]")]
    [Arguments("""{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ShardFilter":{"Type":"CHILD_SHARDS"}}""", "1 validation error detected: Value null at 'shardId' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{}""", "1 validation error detected: Value null at 'streamArn' failed to satisfy constraint: Member must not be null")]
    public async Task HandleDescribeStream_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "DescribeStream", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    public async Task HandleDescribeStream_ArnLongerThan1024_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With();
        var arn = new string('a', 1025);

        // Act
        var answer = FakeCalls.Error(fake, "DescribeStream", $$"""{"StreamArn":"{{arn}}"}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo($"1 validation error detected: Value '{arn}' at 'streamArn' failed to satisfy constraint: Member must have length less than or equal to 1024");
    }

    [Test]
    [Arguments("ListStreams", """{"Shards":1}""", "the member Shards in ListStreams")]
    [Arguments("DescribeStream", """{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ShardFilter":{"Type":"CHILD_SHARDS","ShardId":"s","Depth":1}}""", "the member Depth in ShardFilter of DescribeStream")]
    [Arguments("GetShardIterator", """{"Timestamp":1}""", "the member Timestamp in GetShardIterator")]
    [Arguments("DescribeStream", """{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","Extra":1}""", "the member Extra in DescribeStream")]
    [Arguments("GetRecords", """{"ShardIterator":"x","StreamArn":"y"}""", "the member StreamArn in GetRecords")]
    public async Task Handle_MemberTheFakeDoesNotImplement_AnswersUnsupported(string operation, string request, string named)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var (_, code, message) = FakeCalls.Error(fake, operation, request);

        // Assert
        await Assert.That((code, message.Contains(named, StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }

    [Test]
    public async Task HandleGetRecords_InsertModifyRemove_DeliversThemInTheOrderOfTheWrites()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeStreams.Put(fake, "a", 2);
        FakeStreams.Delete(fake, "a");

        // Act
        var records = FakeStreams.Records(fake);

        // Assert
        await Assert.That(FakeStreams.Names(records)).IsEquivalentTo(["INSERT", "MODIFY", "REMOVE"]);
    }

    [Test]
    public async Task HandleGetRecords_Insert_IsTheRecordOfTheApiReference()
    {
        // Arrange: pk "a" is 3 bytes, the item with v 1 is 6, so the keys and the new image are 9.
        var fake = FakeCalls.With(FakeStreams.Users("NEW_AND_OLD_IMAGES"));
        FakeStreams.Put(fake, "a", 1);

        // Act
        var record = FakeStreams.Records(fake).Single();

        // Assert
        const string Expected = """
            {"eventName":"INSERT","eventVersion":"1.1","eventSource":"aws:dynamodb","awsRegion":"us-east-1",
             "dynamodb":{"ApproximateCreationDateTime":1767225600,"Keys":{"pk":{"S":"a"}},"NewImage":{"pk":{"S":"a"},"v":{"N":"1"}},
                         "SequenceNumber":"100000000000000000001","SizeBytes":9,"StreamViewType":"NEW_AND_OLD_IMAGES"}}
            """;
        await Assert.That((record.GetProperty("eventID").GetString()!.Length, FakeCalls.SameJson(Expected, FakeCalls.Without(record, "eventID")))).IsEqualTo((32, true));
    }

    [Test]
    public async Task HandleGetRecords_Remove_HoldsTheOldImage()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("NEW_AND_OLD_IMAGES"));
        FakeStreams.Put(fake, "a", 1);
        FakeStreams.Delete(fake, "a");

        // Act
        var record = FakeStreams.Records(fake)[1].GetProperty("dynamodb");

        // Assert
        await Assert.That((record.TryGetProperty("NewImage", out _), record.GetProperty("OldImage").GetProperty("v").GetProperty("N").GetString())).IsEqualTo((false, "1"));
    }

    [Test]
    public async Task HandleGetRecords_Limit_ReturnsAtMostLimitAndAnIteratorForTheRest()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeStreams.Put(fake, "b", 1);
        var first = FakeStreams.Page(fake, FakeStreams.Iterator(fake, "TRIM_HORIZON"), limit: 1);

        // Act
        var rest = FakeStreams.Page(fake, first.GetProperty("NextShardIterator").GetString()!);

        // Assert
        await Assert.That((first.GetProperty("Records").GetArrayLength(), rest.GetProperty("Records")[0].GetProperty("dynamodb").GetProperty("Keys").GetProperty("pk").GetProperty("S").GetString()!))
            .IsEqualTo((1, "b"));
    }

    [Test]
    public async Task HandleGetRecords_RecordsOverOneMegabyte_CutsThePageBeforeThem()
    {
        // Arrange: three new items of almost 400 KB each; two fit into 1 MB, the third would not.
        var fake = FakeCalls.With(FakeStreams.Users("NEW_IMAGE"));
        foreach (var key in new[] { "a", "b", "c" })
        {
            FakeCalls.Ok(fake, "PutItem", $$$$"""{"TableName":"Users","Item":{"pk":{"S":"{{{{key}}}}"},"text":{"S":"{{{{FakeWrites.Text(390_000)}}}}"}}}""");
        }

        var first = FakeStreams.Page(fake, FakeStreams.Iterator(fake, "TRIM_HORIZON"));

        // Act
        var rest = FakeStreams.Page(fake, first.GetProperty("NextShardIterator").GetString()!);

        // Assert
        await Assert.That((first.GetProperty("Records").GetArrayLength(), rest.GetProperty("Records").GetArrayLength())).IsEqualTo((2, 1));
    }

    [Test]
    public async Task HandleGetRecords_OpenShardReadToItsEnd_GivesNoRecordsAndAnIterator()
    {
        // Arrange: an empty page of an open shard is how it says that nothing changed.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        var page = FakeStreams.Page(fake, FakeStreams.Iterator(fake, "LATEST"));

        // Assert
        await Assert.That((page.GetProperty("Records").GetArrayLength(), page.TryGetProperty("NextShardIterator", out _))).IsEqualTo((0, true));
    }

    [Test]
    [Arguments(null, 3, false)]
    [Arguments(1, 1, true)]
    public async Task HandleGetRecords_ClosedShard_EndsWithoutANextIteratorOnceItsRecordsAreRead(int? limit, int count, bool hasNext)
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeStreams.Put(fake, "a", 2);
        FakeStreams.Delete(fake, "a");
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");

        // Act
        var page = FakeStreams.Page(fake, FakeStreams.Iterator(fake, "TRIM_HORIZON"), limit);

        // Assert
        await Assert.That((page.GetProperty("Records").GetArrayLength(), page.TryGetProperty("NextShardIterator", out _))).IsEqualTo((count, hasNext));
    }

    [Test]
    public async Task HandleGetRecords_WriteAfterTheStreamWasDisabled_RecordsNothing()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");
        FakeStreams.Put(fake, "b", 1);

        // Act
        var records = FakeStreams.Records(fake);

        // Assert
        await Assert.That(records.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(14, false)]
    [Arguments(15, true)]
    public async Task HandleGetRecords_IteratorOfMinutesAgo_ExpiresAfter15Minutes(int minutes, bool expired)
    {
        // Arrange
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        var iterator = FakeStreams.Iterator(fake, "TRIM_HORIZON");
        clock.Advance(TimeSpan.FromMinutes(minutes));

        // Act
        var (status, body) = FakeCalls.Send(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");

        // Assert
        await Assert.That(status == HttpStatusCode.OK ? string.Empty : body.GetProperty("__type").GetString())
            .IsEqualTo(expired ? "com.amazonaws.dynamodb.v20120810#ExpiredIteratorException" : string.Empty);
    }

    [Test]
    public async Task HandleGetRecords_NextIterator_LivesAnother15Minutes()
    {
        // Arrange: each page hands out an iterator of its own time.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        var iterator = FakeStreams.Iterator(fake, "TRIM_HORIZON");
        clock.Advance(TimeSpan.FromMinutes(10));
        var next = FakeStreams.Page(fake, iterator).GetProperty("NextShardIterator").GetString()!;
        clock.Advance(TimeSpan.FromMinutes(10));

        // Act
        var page = FakeStreams.Page(fake, next);

        // Assert
        await Assert.That(page.GetProperty("Records").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task HandleGetRecords_PositionTrimmedSinceTheIterator_AnswersTrimmedDataAccess()
    {
        // Arrange: an iterator at a record of almost 24 hours ago, used two minutes later, when the record is gone.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeStreams.Put(fake, "a", 1);
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        var iterator = FakeStreams.Iterator(fake, "AT_SEQUENCE_NUMBER", "100000000000000000001");
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        var answer = FakeCalls.Error(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "TrimmedDataAccessException", "The operation attempted to read past the oldest stream record in a shard"));
    }

    [Test]
    [Arguments("not-an-iterator")]
    [Arguments("other shard")]
    [Arguments("past the end")]
    public async Task HandleGetRecords_TextThatIsNoIteratorOfTheShard_AnswersValidation(string kind)
    {
        // Arrange: a made-up text as the integration tests send it, and iterators of the fake that point at no shard or past the records.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        var iterator = kind switch
        {
            "other shard" => new ShardIterator(FakeStreams.UsersArn, "shardId-other", 0, Start).Encode(),
            "past the end" => new ShardIterator(FakeStreams.UsersArn, FakeStreams.FirstShard, 1, Start).Encode(),
            _ => kind,
        };

        // Act
        var answer = FakeCalls.Error(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "Invalid ShardIterator"));
    }

    [Test]
    public async Task HandleGetRecords_IteratorOfAStreamThatIsGone_AnswersResourceNotFound()
    {
        // Arrange: the iterator is fresh, the stream closed more than a day ago.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");
        clock.Advance(TimeSpan.FromHours(25));
        var iterator = new ShardIterator(FakeStreams.UsersArn, FakeStreams.FirstShard, 0, clock.GetUtcNow()).Encode();

        // Act
        var answer = FakeCalls.Error(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    [Arguments("""{"ShardIterator":"x","Limit":0}""", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments("""{"ShardIterator":"x","Limit":1001}""", "1 validation error detected: Value '1001' at 'limit' failed to satisfy constraint: Member must have value less than or equal to 1000")]
    [Arguments("""{}""", "1 validation error detected: Value null at 'shardIterator' failed to satisfy constraint: Member must not be null")]
    public async Task HandleGetRecords_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "GetRecords", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    public async Task HandleGetRecords_DeleteOfTheSweepOfTimeToLive_NamesTheServiceAsTheUser()
    {
        // Arrange: the Developer Guide: a delete of time to live carries userIdentity {"Type":"Service","PrincipalId":"dynamodb.amazonaws.com"}, a delete of a client none.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeCalls.Ok(fake, "UpdateTimeToLive", """{"TableName":"Users","TimeToLiveSpecification":{"Enabled":true,"AttributeName":"expires"}}""");
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"a"},"expires":{"N":"1767225600"}}}""");
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"b"}}}""");
        FakeStreams.Delete(fake, "b");
        fake.ExpireItems();

        // Act
        var records = FakeStreams.Records(fake);

        // Assert
        await Assert.That((records[2].TryGetProperty("userIdentity", out _), FakeCalls.SameJson("""{"PrincipalId":"dynamodb.amazonaws.com","Type":"Service"}""", records[3].GetProperty("userIdentity"))))
            .IsEqualTo((false, true));
    }

    [Test]
    [Arguments("TRIM_HORIZON", null, "a/INSERT,a/MODIFY,a/REMOVE")]
    [Arguments("AT_SEQUENCE_NUMBER", "100000000000000000002", "a/MODIFY,a/REMOVE")]
    [Arguments("AFTER_SEQUENCE_NUMBER", "100000000000000000001", "a/MODIFY,a/REMOVE")]
    [Arguments("AT_SEQUENCE_NUMBER", FakeStreams.FirstSequenceNumber, "a/INSERT,a/MODIFY,a/REMOVE")]
    [Arguments("AFTER_SEQUENCE_NUMBER", "100000000000000000003", "")]
    [Arguments("LATEST", null, "")]
    public async Task HandleGetShardIterator_Type_StartsWhereTheTypeSays(string type, string? sequenceNumber, string expected)
    {
        // Arrange: three records, 100000000000000000001 to 100000000000000000003, in a shard that starts at 100000000000000000000.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeStreams.Put(fake, "a", 2);
        FakeStreams.Delete(fake, "a");

        // Act
        var page = FakeStreams.Page(fake, FakeStreams.Iterator(fake, type, sequenceNumber));

        // Assert
        var records = page.GetProperty("Records").EnumerateArray()
            .Select(static record => $"{record.GetProperty("dynamodb").GetProperty("Keys").GetProperty("pk").GetProperty("S").GetString()}/{record.GetProperty("eventName").GetString()}");
        await Assert.That(string.Join(",", records)).IsEqualTo(expected);
    }

    [Test]
    public async Task HandleGetShardIterator_Latest_SeesWhatIsWrittenAfterIt()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "before", 1);
        var iterator = FakeStreams.Iterator(fake, "LATEST");
        FakeStreams.Put(fake, "after", 1);

        // Act
        var page = FakeStreams.Page(fake, iterator);

        // Assert
        await Assert.That(page.GetProperty("Records").EnumerateArray().Select(static record => record.GetProperty("dynamodb").GetProperty("Keys").GetProperty("pk").GetProperty("S").GetString()!))
            .IsEquivalentTo(["after"]);
    }

    [Test]
    public async Task HandleGetShardIterator_TrimHorizonAfter24Hours_StartsBehindTheTrimmedRecords()
    {
        // Arrange
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeStreams.Put(fake, "old", 1);
        clock.Advance(TimeSpan.FromHours(24));
        FakeStreams.Put(fake, "new", 1);

        // Act
        var records = FakeStreams.Records(fake);

        // Assert
        await Assert.That(records.Select(static record => record.GetProperty("dynamodb").GetProperty("Keys").GetProperty("pk").GetProperty("S").GetString()!)).IsEquivalentTo(["new"]);
    }

    [Test]
    public async Task HandleGetShardIterator_SequenceNumberOfATrimmedRecord_AnswersTrimmedDataAccess()
    {
        // Arrange
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeStreams.Put(fake, "a", 1);
        clock.Advance(TimeSpan.FromHours(24));

        // Act
        var answer = FakeCalls.Error(fake, "GetShardIterator", $$"""
            {"StreamArn":"{{FakeStreams.UsersArn}}","ShardId":"{{FakeStreams.FirstShard}}","ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":"100000000000000000001"}
            """);

        // Assert
        await Assert.That(answer.Code).IsEqualTo("TrimmedDataAccessException");
    }

    [Test]
    [Arguments("""{"ShardIteratorType":"AT_SEQUENCE_NUMBER"}""", "SequenceNumber is required for the ShardIteratorType AT_SEQUENCE_NUMBER and AFTER_SEQUENCE_NUMBER")]
    [Arguments("""{"ShardIteratorType":"TRIM_HORIZON","SequenceNumber":"100000000000000000001"}""", "One or more parameter values were invalid: SequenceNumber should not be passed for this iterator type")]
    [Arguments("""{"ShardIteratorType":"LATEST","SequenceNumber":"1"}""", "One or more parameter values were invalid: SequenceNumber should not be passed for this iterator type")]
    [Arguments("""{"ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":"1"}""", "Invalid SequenceNumber 1: a sequence number is a decimal number of 21 to 40 digits")]
    [Arguments("""{"ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":"1000000000000000000000000000000000000000000"}""", "Invalid SequenceNumber 1000000000000000000000000000000000000000000: a sequence number is a decimal number of 21 to 40 digits")]
    [Arguments("""{"ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":"1000000000000000000000000000000000000000"}""", "Invalid SequenceNumber 1000000000000000000000000000000000000000 for the shard shardId-00000001767225600000-00000001: it is outside the range of the shard")]
    [Arguments("""{"ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":"10000000000000000000x"}""", "Invalid SequenceNumber 10000000000000000000x: a sequence number is a decimal number of 21 to 40 digits")]
    [Arguments("""{"ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":"099999999999999999999"}""", "Invalid SequenceNumber 99999999999999999999 for the shard shardId-00000001767225600000-00000001: it is outside the range of the shard")]
    [Arguments("""{"ShardIteratorType":"AFTER_SEQUENCE_NUMBER","SequenceNumber":"100000000000000000002"}""", "Invalid SequenceNumber 100000000000000000002 for the shard shardId-00000001767225600000-00000001: it is outside the range of the shard")]
    [Arguments("""{"ShardIteratorType":"AT_TIMESTAMP"}""", "1 validation error detected: Value 'AT_TIMESTAMP' at 'shardIteratorType' failed to satisfy constraint: Member must satisfy enum value set: [TRIM_HORIZON, LATEST, AT_SEQUENCE_NUMBER, AFTER_SEQUENCE_NUMBER]")]
    [Arguments("""{"SequenceNumber":null}""", "1 validation error detected: Value null at 'shardIteratorType' failed to satisfy constraint: Member must not be null")]
    public async Task HandleGetShardIterator_PositionDynamoDbRejects_AnswersValidation(string position, string message)
    {
        // Arrange: a shard with one record, 100000000000000000001.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        var request = $$"""{"StreamArn":"{{FakeStreams.UsersArn}}","ShardId":"{{FakeStreams.FirstShard}}",""" + position[1..];

        // Act
        var answer = FakeCalls.Error(fake, "GetShardIterator", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments("ListStreams", """{"TableName":1}""", "The member TableName must be a string, and it is Number.")]
    [Arguments("ListStreams", """{"Limit":"5"}""", "The member Limit must be a number, and it is String.")]
    [Arguments("DescribeStream", """{"StreamArn":1}""", "The member StreamArn must be a string, and it is Number.")]
    [Arguments("DescribeStream", """{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ExclusiveStartShardId":1}""", "The member ExclusiveStartShardId must be a string, and it is Number.")]
    [Arguments("DescribeStream", """{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ShardFilter":{"Type":"CHILD_SHARDS","ShardId":1}}""", "The member ShardId must be a string, and it is Number.")]
    [Arguments("GetShardIterator", """{"StreamArn":1}""", "The member StreamArn must be a string, and it is Number.")]
    [Arguments("GetShardIterator", """{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ShardId":1}""", "The member ShardId must be a string, and it is Number.")]
    [Arguments("GetShardIterator", """{"StreamArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/x","ShardId":"s","ShardIteratorType":"AT_SEQUENCE_NUMBER","SequenceNumber":1}""", "The member SequenceNumber must be a string, and it is Number.")]
    [Arguments("GetRecords", """{"ShardIterator":1}""", "The member ShardIterator must be a string, and it is Number.")]
    public async Task Handle_MemberOfAnotherKind_AnswersSerializationNamingIt(string operation, string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        var answer = FakeCalls.Error(fake, operation, request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "SerializationException", message));
    }

    [Test]
    [Arguments(37)]
    [Arguments(1024)]
    public async Task HandleDescribeStream_ArnOfALengthAtABound_LooksForTheStream(int length)
    {
        // Arrange
        var fake = FakeCalls.With();
        var arn = new string('a', length);

        // Act
        var answer = FakeCalls.Error(fake, "DescribeStream", $$"""{"StreamArn":"{{arn}}"}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task HandleGetRecords_LimitFromALaterPosition_CountsFromThere()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);
        FakeStreams.Put(fake, "b", 1);
        FakeStreams.Put(fake, "c", 1);
        FakeStreams.Put(fake, "d", 1);
        var iterator = FakeStreams.Iterator(fake, "AFTER_SEQUENCE_NUMBER", "100000000000000000001");

        // Act
        var page = FakeStreams.Page(fake, iterator, limit: 2);

        // Assert
        await Assert.That(page.GetProperty("Records").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task HandleGetShardIterator_UnknownShard_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        var answer = FakeCalls.Error(fake, "GetShardIterator", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}","ShardId":"shardId-other","ShardIteratorType":"LATEST"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Requested resource not found: Shard does not exist: shardId-other"));
    }

    [Test]
    public async Task Handle_TransactionOfTwoPuts_EmitsTheirRecordsTogether()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        FakeCalls.Ok(fake, "TransactWriteItems", """
            {"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}},{"Put":{"TableName":"Users","Item":{"pk":{"S":"b"}}}}]}
            """);

        // Assert
        var records = FakeStreams.Records(fake);
        await Assert.That(records.Select(static record => record.GetProperty("dynamodb").GetProperty("SequenceNumber").GetString()!))
            .IsEquivalentTo(["100000000000000000001", "100000000000000000002"]);
    }

    [Test]
    public async Task Handle_CancelledTransaction_EmitsNoRecords()
    {
        // Arrange: the second put is larger than 400 KB, so the first is taken back.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        FakeCalls.Error(fake, "TransactWriteItems", $$$$$"""
            {"TransactItems":[{"Put":{"TableName":"Users","Item":{"pk":{"S":"a"}}}},{"Put":{"TableName":"Users","Item":{"pk":{"S":"b"},"text":{"S":"{{{{{FakeWrites.Text(410_000)}}}}}"}}}}]}
            """);

        // Assert
        await Assert.That(FakeStreams.Records(fake).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Handle_WritesThatChangeNothing_EmitNoRecords()
    {
        // Arrange: the same item again, an update to the value it has, and a delete of an item that is not there.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeStreams.Put(fake, "a", 1);

        // Act
        FakeStreams.Put(fake, "a", 1);
        FakeCalls.Ok(fake, "UpdateItem", """{"TableName":"Users","Key":{"pk":{"S":"a"}},"UpdateExpression":"SET v = :v","ExpressionAttributeValues":{":v":{"N":"1"}}}""");
        FakeStreams.Delete(fake, "missing");

        // Assert
        await Assert.That(FakeStreams.Names(FakeStreams.Records(fake))).IsEquivalentTo(["INSERT"]);
    }

    [Test]
    public async Task HandleGetRecords_IteratorFromBeforeAResetOnTheSameClock_AnswersResourceNotFound()
    {
        // Arrange: the table and its stream come back at the same millisecond, so only what Reset keeps tells the streams apart.
        var clock = new ManualTimeProvider(Start);
        var fake = FakeStreams.UsersOn(clock);
        FakeStreams.Put(fake, "old", 1);
        var iterator = FakeStreams.Iterator(fake, "TRIM_HORIZON");
        fake.Reset();
        FakeCalls.Ok(fake, "CreateTable", FakeStreams.Users("NEW_AND_OLD_IMAGES"));
        FakeStreams.Put(fake, "new", 1);

        // Act
        var answer = FakeCalls.Error(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }
}
