using System.Collections.Immutable;

namespace DynaSharp.Generators;

/// <summary>
/// Keeps the generated files of a compilation apart. A hint name comes from one type, which cannot see the others, and the driver compares hint names
/// ignoring case and gives up all output on a clash: <c>Shop.Order.g.cs</c> and <c>Shop.ORDER.g.cs</c>. So all hint names are looked at together, and the
/// rare types that clash get a number, by the order of their full names.
/// </summary>
internal static class HintNames
{
    private const string Extension = ".g.cs";

    /// <summary>Finds the types whose hint name clashes with another, with the hint name each gets instead.</summary>
    /// <param name="entities">The entities.</param>
    /// <param name="documents">The documents.</param>
    /// <returns>The new names, which is empty in the usual case, so that a new type does not touch the output of the others.</returns>
    public static EquatableArray<RenamedHint> FindClashes(ImmutableArray<TypeModel?> entities, ImmutableArray<TypeModel?> documents)
    {
        var clashes = entities.Concat(documents)
            .OfType<TypeModel>()
            .GroupBy(static type => type.HintName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(static group => group.OrderBy(static type => type.FullName, StringComparer.Ordinal).Select(Renamed));
        return new EquatableArray<RenamedHint>([.. clashes]);
    }

    /// <summary>Gets the hint name of a type.</summary>
    /// <param name="type">The type.</param>
    /// <param name="clashes">What <see cref="FindClashes"/> found.</param>
    /// <returns>The hint name.</returns>
    public static string Of(TypeModel type, EquatableArray<RenamedHint> clashes) =>
        clashes.FirstOrDefault(clash => clash.FullName == type.FullName)?.HintName ?? type.HintName;

    private static RenamedHint Renamed(TypeModel type, int position)
    {
        var stem = type.HintName.Substring(0, type.HintName.Length - Extension.Length);
        return new RenamedHint(type.FullName, $"{stem}.{CodeText.Number(position)}{Extension}");
    }
}
