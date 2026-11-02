using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>
/// Why an update by handle of what a computed key is derived from throws. A raw path goes past that rule, so this shows through a real index what the rule prevents: the service keeps the item under the old
/// key, and nothing in the response says so. The answer comes from the emulators and not from this code.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ComputedKeyTests(Backend backend)
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    private static readonly DateOnly OldDay = new(2026, 11, 2);

    private static readonly DateOnly NewDay = new(2026, 11, 3);

    [Test]
    public async Task Query_DependencyChangedByARawPath_FindsTheItemUnderTheOldDay()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var arrivals = await CreateAsync(sandbox);
        await arrivals.Put(new Arrival { Id = "a1", At = Monday }).SendAsync();

        // Act
        await arrivals.Update(Arrival.Keys.Of("a1")).Set("At", AttributeValue.FromString(Tuesday.ToString("O"))).SendAsync();
        var byOldDay = await IdsAsync(arrivals, OldDay);
        var byNewDay = await IdsAsync(arrivals, NewDay);

        // Assert
        await Assert.That((byOldDay, byNewDay)).IsEqualTo(("a1", string.Empty));
    }

    [Test]
    public async Task Query_DependencyChangedByAPut_FindsTheItemUnderTheNewDay()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var arrivals = await CreateAsync(sandbox);
        await arrivals.Put(new Arrival { Id = "a1", At = Monday }).SendAsync();

        // Act
        await arrivals.Put(new Arrival { Id = "a1", At = Tuesday }).SendAsync();
        var byOldDay = await IdsAsync(arrivals, OldDay);
        var byNewDay = await IdsAsync(arrivals, NewDay);

        // Assert
        await Assert.That((byOldDay, byNewDay)).IsEqualTo((string.Empty, "a1"));
    }

    private static async Task<Table<Arrival>> CreateAsync(Sandbox sandbox)
    {
        var arrivals = sandbox.Client.Table<Arrival>(Tables.UniqueName());
        await sandbox.CreateTableAsync(arrivals, arrivals.CreateTable().WaitUntilActive());
        return arrivals;
    }

    private static async Task<string> IdsAsync(Table<Arrival> arrivals, DateOnly day)
    {
        var read = await arrivals.Index(Arrival.Indexes.ByDay).Query(Arrival.Keys.ByDayPk.Of(day)).ItemsAsync().Select(static arrival => arrival.Id).ToArrayAsync();
        return string.Join(",", read);
    }
}
