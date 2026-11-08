using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Time-ordered sort keys</see> with version-7 GUIDs: <see cref="Guid.CreateVersion7(DateTimeOffset)"/> starts with the time in milliseconds, and DynaSharp stores a
/// <see cref="Guid"/> as lowercase text, so a sort key is unique and chronological at once, and two writes in one millisecond do not collide. Newest first is a backward read of the partition
/// (<c>ScanIndexForward=false</c>), and a range of time is <c>Keys.Sk.Between</c> of the first and the last version-7 value of two milliseconds.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class Version7SortKeyTests(Backend backend)
{
    private const string Ann = "ann";

    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Query_Descending_ReadsNewestFirst()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<ChatMessage>();
        for (var i = 0; i < 3; i++)
        {
            await messages.Put(new ChatMessage { Room = Ann, Id = Guid.CreateVersion7(Noon.AddSeconds(i)), Text = $"m{i}" }).SendAsync();
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

        // Act
        await messages.Put(new ChatMessage { Room = Ann, Id = Guid.CreateVersion7(Noon), Text = "a" }).SendAsync();
        await messages.Put(new ChatMessage { Room = Ann, Id = Guid.CreateVersion7(Noon), Text = "b" }).SendAsync();

        // Assert
        await Assert.That(await messages.Query(ChatMessage.Keys.Pk.Of(Ann)).CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task Query_BetweenTheBoundsOfTwoMilliseconds_ReadsBothOfThemWhole()
    {
        // Arrange (a message at each end of the range, and one just outside each end)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<ChatMessage>();
        var (from, to) = (Noon.AddSeconds(30), Noon.AddSeconds(150));
        DateTimeOffset[] times = [from.AddMilliseconds(-1), from, to, to.AddMilliseconds(1)];
        for (var i = 0; i < times.Length; i++)
        {
            await messages.Put(new ChatMessage { Room = Ann, Id = Guid.CreateVersion7(times[i]), Text = $"m{i}" }).SendAsync();
        }

        // Act
        var range = ChatMessage.Keys.Sk.Between(Guid.FirstVersion7(from), Guid.LastVersion7(to));
        var read = await messages.Query(ChatMessage.Keys.Pk.Of(Ann), range).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static message => message.Text))).IsEqualTo("m1,m2");
    }

    [Test]
    public async Task Query_FromTheFirstValueOfAMillisecond_ReadsFromThatMillisecondOn()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<ChatMessage>();
        for (var i = 0; i < 3; i++)
        {
            await messages.Put(new ChatMessage { Room = Ann, Id = Guid.CreateVersion7(Noon.AddMilliseconds(i - 1)), Text = $"m{i}" }).SendAsync();
        }

        // Act
        var read = await messages.Query(ChatMessage.Keys.Pk.Of(Ann), ChatMessage.Keys.Sk.Ge(Guid.FirstVersion7(Noon))).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static message => message.Text))).IsEqualTo("m1,m2");
    }
}

[DynamoDbEntity]
[Discriminator("CHAT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("{Id}")]
internal sealed partial record ChatMessage
{
    public required string Room { get; init; }

    public required Guid Id { get; init; }

    public required string Text { get; init; }
}
