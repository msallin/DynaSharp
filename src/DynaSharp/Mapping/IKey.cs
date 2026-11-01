using DynaSharp.Serialization;

namespace DynaSharp.Mapping;

/// <summary>
/// A primary key that writes itself. Generated key structs implement it through <see cref="IKey{TEntity}"/>, and they hold what the key is
/// made of rather than the text of the key, so a key costs no string until it is written.
/// </summary>
public interface IKey
{
    /// <summary>Writes the key attributes, without the surrounding braces.</summary>
    /// <param name="writer">The writer, positioned inside a <c>Key</c> object.</param>
    public void WriteKey(DynamoDbWriter writer);

    /// <summary>
    /// Gets a value the key was made from, for an update that writes an index key which wants more of it than the text of the key holds. Generated code implements this for such an entity
    /// only, and everything else reads the text of the key.
    /// </summary>
    /// <param name="propertyName">The property, as it stands in the template.</param>
    /// <returns>The value, or <see langword="null"/> if the key does not hold it.</returns>
    public object? ValueOf(string propertyName) => null;
}
