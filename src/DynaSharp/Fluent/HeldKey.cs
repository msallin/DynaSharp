using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// A generated key that an update keeps until its terminal, for an entity whose key text holds less than an input. A key is a struct, the clause that wants it can still follow, and a box would be paid by every
/// update of such an entity, also by the many that never ask. So a state keeps one of these and fills it again for the next update, which costs nothing as long as the type of the key stays.
/// </summary>
internal abstract class HeldKey
{
    /// <summary>Gets a value the key was made from, see <see cref="IKey.ValueOf"/>.</summary>
    /// <param name="propertyName">The property, as it stands in the template.</param>
    /// <returns>The value, or <see langword="null"/> if the key does not hold it.</returns>
    public abstract object? ValueOf(string propertyName);

    /// <summary>Lets go of the key, because a state goes back to its pool without what its command held.</summary>
    public abstract void Clear();
}
