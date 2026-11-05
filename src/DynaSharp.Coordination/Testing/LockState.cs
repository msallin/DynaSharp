namespace DynaSharp.Coordination.Testing;

/// <summary>
/// What the item of a lock says, as <see cref="LockTableTesting.GetStateAsync"/> reads it, so a test asserts on a lock without knowing the format of its item.
/// A record, so a test compares it with one it builds: <c>new LockState(IsHeld: true, HolderLabel: "pod-a", LeaseDuration: TimeSpan.FromSeconds(30), FencingToken: 2)</c>.
/// </summary>
/// <param name="IsHeld">Whether a lease holds the lock: the item has an owner. A released lock, and one whose holder released it, is not held.</param>
/// <param name="HolderLabel">The <see cref="LockTableOptions.HolderLabel"/> of the holder, or <see langword="null"/> without one, and while the lock is free.</param>
/// <param name="LeaseDuration">The lease of the holder, in whole milliseconds as stored, or <see langword="null"/> while the lock is free.</param>
/// <param name="FencingToken">The fencing token of the last acquire, which a release keeps: 1 after the first acquire, 0 for an item that no acquire wrote.</param>
public sealed record LockState(bool IsHeld, string? HolderLabel, TimeSpan? LeaseDuration, long FencingToken);
