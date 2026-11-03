using System.Security.Cryptography;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">ULID / KSUID sort keys</see>: an id that sorts by creation time, so a sort key is unique and chronological at once, and two writes in one millisecond do not collide. Newest first is a backward
/// read of the partition (<c>ScanIndexForward=false</c>). A range of time is a range of the leading characters. <see cref="Guid.CreateVersion7()"/> gives the same order as a <see cref="Guid"/>.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class UlidSortKeyTests(Backend backend)
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string Ann = "ann";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Query_Descending_ReadsNewestFirst()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<ChatMessage>();
        var clock = new ManualClock(Noon);
        for (var i = 0; i < 3; i++)
        {
            await messages.Put(new ChatMessage { Room = Ann, Id = NewUlid(clock), Text = $"m{i}" }).SendAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        var newest = await messages.Query(ChatMessage.Keys.Pk.Of(Ann)).Descending().Limit(2).PageAsync();

        // Assert
        await Assert.That(string.Join(",", newest.Items.Select(static message => message.Text))).IsEqualTo("m2,m1");
    }

    [Test]
    public async Task Put_InOneMillisecond_KeepsBothItems()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<ChatMessage>();
        var clock = new ManualClock(Noon);

        // Act
        await messages.Put(new ChatMessage { Room = Ann, Id = NewUlid(clock), Text = "a" }).SendAsync();
        await messages.Put(new ChatMessage { Room = Ann, Id = NewUlid(clock), Text = "b" }).SendAsync();

        // Assert
        await Assert.That(await messages.Query(ChatMessage.Keys.Pk.Of(Ann)).CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task Query_BetweenTwoTimes_ReadsTheRangeByTheLeadingCharacters()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<ChatMessage>();
        var clock = new ManualClock(Noon);
        for (var i = 0; i < 4; i++)
        {
            await messages.Put(new ChatMessage { Room = Ann, Id = NewUlid(clock), Text = $"m{i}" }).SendAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        // Act
        // The upper bound is the first millisecond after the range: an id of the last millisecond is longer than
        // its timestamp, so it sorts after the bare timestamp of its own millisecond and before that of the next.
        var range = SortKeyCondition.Between(Timestamp(Noon.AddSeconds(30)), Timestamp(Noon.AddSeconds(150).AddMilliseconds(1)));
        var read = await messages.Query(ChatMessage.Keys.Pk.Of(Ann), range).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static message => message.Text))).IsEqualTo("m1,m2");
    }

    // 48 bits of milliseconds in 10 characters, then 80 random bits in 16, in an alphabet that sorts like its values.
    private static string NewUlid(TimeProvider clock) => string.Create(26, Timestamp(clock.GetUtcNow()), static (text, timestamp) =>
    {
        timestamp.CopyTo(text);
        for (var i = 10; i < 26; i++)
        {
            text[i] = Crockford[RandomNumberGenerator.GetInt32(32)];
        }
    });

    private static string Timestamp(DateTimeOffset at) => string.Create(10, at.ToUnixTimeMilliseconds(), static (text, milliseconds) =>
    {
        for (var i = 9; i >= 0; i--, milliseconds >>= 5)
        {
            text[i] = Crockford[(int)(milliseconds & 31)];
        }
    });
}

[DynamoDbEntity]
[Discriminator("CHAT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("{Id}")]
internal sealed partial record ChatMessage
{
    public required string Room { get; init; }

    public required string Id { get; init; }

    public required string Text { get; init; }
}
