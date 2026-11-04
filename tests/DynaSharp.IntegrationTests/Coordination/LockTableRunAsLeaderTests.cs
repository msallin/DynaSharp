using DynaSharp.Coordination;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;

namespace DynaSharp.IntegrationTests.Coordination;

/// <summary><see cref="LockTable.RunAsLeaderAsync"/> of <c>DynaSharp.Coordination</c> against each backend, with two <see cref="LockTable"/> instances for two processes.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class LockTableRunAsLeaderTests(Backend backend)
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(3);

    [Test]
    public async Task RunAsLeaderAsync_TwoProcesses_LeadOneAfterTheOtherWithRisingTokens()
    {
        // Arrange (each leader writes under IsHeld and hands the lock on by returning)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var leading = 0;
        var overlapped = false;
        var tokens = new List<long>();
        async Task Work(Lease lease, CancellationToken stop)
        {
            overlapped |= Interlocked.Increment(ref leading) > 1;
            await sandbox.Client.TransactWrite()
                .IsHeld(lease)
                .Put(sandbox.Shop, new Item { ["pk"] = "LEADER", ["sk"] = lease.FencingToken.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                .SendAsync(stop);
            lock (tokens)
            {
                tokens.Add(lease.FencingToken);
            }

            Interlocked.Decrement(ref leading);
        }

        // Act
        await Task.WhenAll(
            new LockTable(sandbox.Shop).RunAsLeaderAsync("leader", Lease, Work),
            new LockTable(sandbox.Shop).RunAsLeaderAsync("leader", Lease, Work)).WaitAsync(TimeSpan.FromSeconds(60));

        // Assert
        await Assert.That((overlapped, string.Join(",", tokens.Order()))).IsEqualTo((false, "1,2"));
    }
}
