using System.Security.Cryptography;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">ULID / KSUID sort keys</see>: an id that sorts by creation time, so a sort key is unique and chronological at once, and two writes in one millisecond do not collide. Newest first is a backward
/// read of the partition (<c>ScanIndexForward=false</c>). A range of time is a range of the leading characters, with the lowest and the highest random part behind them. <see cref="Version7SortKeyTests"/> is the
/// same with a version-7 <see cref="Guid"/>, which needs no code of its own; this is for a service that has ULIDs already.
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
        var messages = sandbox.Shop.Of<UlidMessage>();
        var clock = new ManualClock(Noon);
        for (var i = 0; i < 3; i++)
        {
            await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = $"m{i}" }).SendAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        var newest = await messages.Query(UlidMessage.Keys.Pk.Of(Ann)).Descending().Limit(2).PageAsync();

        // Assert
        await Assert.That(string.Join(",", newest.Items.Select(static message => message.Text))).IsEqualTo("m2,m1");
    }

    [Test]
    public async Task Put_InOneMillisecond_KeepsBothItems()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<UlidMessage>();
        var clock = new ManualClock(Noon);

        // Act
        await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = "a" }).SendAsync();
        await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = "b" }).SendAsync();

        // Assert
        await Assert.That(await messages.Query(UlidMessage.Keys.Pk.Of(Ann)).CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task Query_BetweenTwoTimes_ReadsTheRangeByTheLeadingCharacters()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<UlidMessage>();
        var clock = new ManualClock(Noon);
        for (var i = 0; i < 4; i++)
        {
            await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = $"m{i}" }).SendAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        // Act
        // The bounds of a millisecond are its timestamp followed by the lowest and the highest random part, as the bounds of a version-7 Guid are.
        var range = UlidMessage.Keys.Sk.Between(Timestamp(Noon.AddSeconds(30)) + new string('0', 16), Timestamp(Noon.AddSeconds(150)) + new string('Z', 16));
        var read = await messages.Query(UlidMessage.Keys.Pk.Of(Ann), range).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static message => message.Text))).IsEqualTo("m1,m2");
    }

    [Test]
    public async Task Query_BetweenTheBoundsOfOneMillisecond_ReadsAllOfIt()
    {
        // Arrange (two ids of one millisecond, and one of the millisecond after it)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var messages = sandbox.Shop.Of<UlidMessage>();
        var clock = new ManualClock(Noon);
        await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = "a" }).SendAsync();
        await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = "b" }).SendAsync();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await messages.Put(new UlidMessage { Room = Ann, Id = NewUlid(clock), Text = "c" }).SendAsync();

        // Act
        var range = UlidMessage.Keys.Sk.Between(Timestamp(Noon) + new string('0', 16), Timestamp(Noon) + new string('Z', 16));
        var read = await messages.Query(UlidMessage.Keys.Pk.Of(Ann), range).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", read.Select(static message => message.Text).Order(StringComparer.Ordinal))).IsEqualTo("a,b");
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
internal sealed partial record UlidMessage
{
    public required string Room { get; init; }

    public required string Id { get; init; }

    public required string Text { get; init; }
}
