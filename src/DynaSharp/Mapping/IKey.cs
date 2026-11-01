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
}
