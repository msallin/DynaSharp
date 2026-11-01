namespace DynaSharp.Generators;

/// <summary>Where a template is used, which decides what the binder accepts.</summary>
/// <param name="IsSortKey">Sort keys compare by bytes, so an unfixed number or a string before a delimiter gets a warning.</param>
/// <param name="IsIndex">Computed properties cannot feed an index.</param>
/// <param name="IsSparse">Nullable inputs are allowed only in a sparse index.</param>
internal readonly record struct KeyRole(bool IsSortKey, bool IsIndex, bool IsSparse);
