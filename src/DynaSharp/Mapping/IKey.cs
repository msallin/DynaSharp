using System.ComponentModel;
using DynaSharp.Serialization;

namespace DynaSharp.Mapping;

/// <summary>
/// A primary key that writes itself. Generated key structs implement it through <see cref="IKey{TEntity}"/>, and they hold what the key is
/// made of rather than the text of the key, so a key costs no string until it is written. A generated <c>Keys.Of</c> has checked the values by then.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IKey
{
    /// <summary>Writes the key attributes, without the surrounding braces.</summary>
    /// <param name="writer">The writer, positioned inside a <c>Key</c> object.</param>
    public void WriteKey(DynamoDbWriter writer);

    /// <summary>
    /// Gets a value the key was made from, for an update that wants more of it than the text of the key holds, for an index key or to store it. Generated code implements this for such an entity
    /// only, and everything else reads the text of the key.
    /// </summary>
    /// <param name="propertyName">The property, as it stands in the template.</param>
    /// <returns>The value, or <see langword="null"/> if the key does not hold it.</returns>
    public object? ValueOf(string propertyName) => null;
}
