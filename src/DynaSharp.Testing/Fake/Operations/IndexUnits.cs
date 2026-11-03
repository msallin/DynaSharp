namespace DynaSharp.Testing.Fake;

/// <summary>The capacity units of one index in a <c>ConsumedCapacity</c> with <c>INDEXES</c>.</summary>
/// <param name="IndexName">The index.</param>
/// <param name="IsGlobal"><see langword="true"/> for a global index.</param>
/// <param name="Units">The units.</param>
internal readonly record struct IndexUnits(string IndexName, bool IsGlobal, double Units);
