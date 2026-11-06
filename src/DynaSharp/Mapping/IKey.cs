using System.ComponentModel;
using DynaSharp.Serialization;

namespace DynaSharp.Mapping;

/// <summary>
/// A primary key of an item, as the generated <c>Keys.Of(...)</c> of an entity builds it. Pass it to <c>Get</c>, <c>Update</c>, <c>Delete</c> and <c>Query</c> of the table of that entity.
/// </summary>
/// <remarks>A key holds the values it was made from, which <c>Keys.Of</c> has checked, and writes them in the format of the template when the command is sent.</remarks>
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
