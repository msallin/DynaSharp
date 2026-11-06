using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>What <c>IncludingAll()</c> needs of one entity.</summary>
/// <param name="FullName">The name for use in code, for example <c>global::Shop.Order</c>.</param>
/// <param name="PartitionKey">The attribute of the partition key of its table.</param>
/// <param name="SortKey">The attribute of the sort key of its table, or null.</param>
/// <param name="Reach">The accessibility the type really has, see <see cref="TypeModel.Reach"/>.</param>
internal sealed record RegisteredEntity(string FullName, string PartitionKey, string? SortKey, Accessibility Reach)
{
    /// <summary>Gets the name as a message shows it, for example <c>Shop.Order</c>.</summary>
    public string DisplayName => FullName.Substring("global::".Length);
}
