using System.Globalization;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Table per period</see>: data with a shelf life goes into a table per month, and a month is dropped as a whole, for free, where deleting its items would cost a write each. The price is that code
/// knows how tables are named, and a read across periods asks each table on its own: a fan-out covers one table.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TablePerPeriodTests(Backend backend)
{
    private static readonly DateTimeOffset October = new(2026, 10, 31, 23, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset November = October.AddHours(2);

    [Test]
    public async Task ReadAsync_TwoPeriods_AsksEachTableAndConcatenatesInOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var prefix = Tables.UniqueName();
        var clock = new ManualClock(October);
        await PutAsync(sandbox, prefix, clock, "last of October");
        clock.Advance(November - October);
        await PutAsync(sandbox, prefix, clock, "first of November");

        // Act
        var read = new List<string>();
        foreach (var month in new[] { October, November })
        {
            await foreach (var entry in LogOf(sandbox, prefix, month).Query(LogEntry.Keys.Pk.Of("app")).ItemsAsync())
            {
                read.Add(entry.Text);
            }
        }

        // Assert
        await Assert.That(string.Join(",", read)).IsEqualTo("last of October,first of November");
    }

    [Test]
    public async Task DeleteTable_APeriodThatExpired_LeavesTheNextOne()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var prefix = Tables.UniqueName();
        var clock = new ManualClock(October);
        await PutAsync(sandbox, prefix, clock, "old");
        clock.Advance(November - October);
        await PutAsync(sandbox, prefix, clock, "new");

        // Act
        await sandbox.Client.DeleteTableAsync(new DeleteTableRequest { TableName = TableOf(prefix, October) });
        var read = await LogOf(sandbox, prefix, November).Query(LogEntry.Keys.Pk.Of("app")).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(read.Single().Text).IsEqualTo("new");
    }

    private static string TableOf(string prefix, DateTimeOffset at) => prefix + "-" + at.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static Table<LogEntry> LogOf(Sandbox sandbox, string prefix, DateTimeOffset at) => sandbox.Client.Table<LogEntry>(TableOf(prefix, at));

    // A real system creates next month's table ahead of time, from a schedule: the write path never does it.
    private static async Task PutAsync(Sandbox sandbox, string prefix, TimeProvider clock, string text)
    {
        var at = clock.GetUtcNow();
        await sandbox.CreateTableAsync(Tables.Request(TableOf(prefix, at), new TableSpec(SortKeyType: KeyAttributeType.String)));
        await LogOf(sandbox, prefix, at).Put(new LogEntry { Source = "app", At = at, Text = text }).SendAsync();
    }
}

[DynamoDbEntity]
[Discriminator("LOG")]
[PartitionKey("LOG#{Source}")]
[SortKey("{At}")]
internal sealed partial record LogEntry
{
    public required string Source { get; init; }

    public required DateTimeOffset At { get; init; }

    public required string Text { get; init; }
}
