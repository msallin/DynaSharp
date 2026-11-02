using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>A generated key of one type, see <see cref="HeldKey"/>.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
internal sealed class TypedHeldKey<TKey> : HeldKey
    where TKey : IKey
{
    /// <summary>Gets or sets the key.</summary>
    public TKey Key { get; set; } = default!;

    /// <inheritdoc/>
    public override object? ValueOf(string propertyName) => Key.ValueOf(propertyName);

    /// <inheritdoc/>
    public override void Clear() => Key = default!;
}
