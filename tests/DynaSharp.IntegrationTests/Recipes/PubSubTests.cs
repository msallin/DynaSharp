using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Pub/sub via subscription items</see>: a topic lists its subscribers as items, and publishing copies the message into an inbox per subscriber, so each reads and deletes at its own pace. Streams are
/// change data capture and no bus: a shard takes about two readers. MQTT topics hold <c>#</c>, so these keys use <c>|</c> as the delimiter.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class PubSubTests(Backend backend)
{
    private const string Topic = "sensors/#";

    private static TimeProvider Clock => new ManualClock(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task PublishAsync_TwoSubscribers_PutsTheMessageInEachInboxAndNoOtherOne()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Subscription>().Put(new Subscription { Topic = Topic, Subscriber = "ann" }).SendAsync();
        await sandbox.Shop.Of<Subscription>().Put(new Subscription { Topic = Topic, Subscriber = "ben" }).SendAsync();
        await sandbox.Shop.Of<Subscription>().Put(new Subscription { Topic = "other", Subscriber = "eve" }).SendAsync();

        // Act
        await PublishAsync(sandbox.Shop, Clock, "hello");

        // Assert
        await Assert.That(((await InboxAsync(sandbox.Shop, "ann")).Single().Text, (await InboxAsync(sandbox.Shop, "ben")).Single().Text, (await InboxAsync(sandbox.Shop, "eve")).Count)).IsEqualTo(("hello", "hello", 0));
    }

    [Test]
    public async Task Delete_OneSubscriberReadsItsInbox_LeavesTheOtherInbox()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<Subscription>().Put(new Subscription { Topic = Topic, Subscriber = "ann" }).SendAsync();
        await sandbox.Shop.Of<Subscription>().Put(new Subscription { Topic = Topic, Subscriber = "ben" }).SendAsync();
        await PublishAsync(sandbox.Shop, Clock, "hello");
        var read = (await InboxAsync(sandbox.Shop, "ann")).Single();

        // Act
        await sandbox.Shop.Of<InboxMessage>().Delete(read).SendAsync();

        // Assert
        await Assert.That(((await InboxAsync(sandbox.Shop, "ann")).Count, (await InboxAsync(sandbox.Shop, "ben")).Count)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task Format_TopicWithTheDelimiterOfTheKey_Throws()
    {
        // Act
        var failure = await Assert.That(static () => Subscription.Keys.Pk.Format("sensors|temperature")).Throws<KeyFormatException>();

        // Assert
        await Assert.That(failure!.Message).Contains("Topic", StringComparison.Ordinal);
    }

    private static async Task PublishAsync(Table<Item> shop, TimeProvider clock, string text)
    {
        var subscribers = await shop.Of<Subscription>().Query(Subscription.Keys.Pk.Of(Topic)).ItemsAsync().ToListAsync();
        var id = Guid.NewGuid();
        await shop.Of<InboxMessage>().BatchWrite().PutAll(subscribers.Select(subscription => new InboxMessage { Subscriber = subscription.Subscriber, At = clock.GetUtcNow(), MessageId = id, Text = text })).SendAsync();
    }

    private static async Task<List<InboxMessage>> InboxAsync(Table<Item> shop, string subscriber) =>
        await shop.Of<InboxMessage>().Query(InboxMessage.Keys.Pk.Of(subscriber)).Consistent().ItemsAsync().ToListAsync();
}

[DynamoDbEntity]
[Discriminator("SUBSCRIPTION")]
[PartitionKey("TOPIC|{Topic}")]
[SortKey("SUB|{Subscriber}")]
internal sealed partial record Subscription
{
    public required string Topic { get; init; }

    public required string Subscriber { get; init; }
}

[DynamoDbEntity]
[Discriminator("INBOX")]
[PartitionKey("INBOX|{Subscriber}")]
[SortKey("{At}|{MessageId}")]
internal sealed partial record InboxMessage
{
    public required string Subscriber { get; init; }

    public required DateTimeOffset At { get; init; }

    public required Guid MessageId { get; init; }

    public required string Text { get; init; }
}
