using System.ComponentModel;

namespace DynaSharp.Mapping;

/// <summary>
/// A key attribute of an entity and the properties it is made of, which is what keeps a key consistent when an update changes one of them by its handle. The
/// generated code creates one per attribute, once per type. A table key without properties of its own and a plain table key are left out: the first cannot go
/// stale, and DynamoDB rejects an update of the second by itself, unless an index key repeats it, and then it says how to read the value. A key of an index without properties has no inputs: it follows the other key of its index, and an index with nothing else is written by every update.
/// </summary>
/// <param name="attributeName">The key attribute.</param>
/// <param name="text">The template as declared, or <c>{Property}</c> for a plain key of an index, whose attribute is the property itself.</param>
/// <param name="isTableKey">Whether it is a key of the table, which DynamoDB does not let an update change.</param>
/// <param name="inputs">The properties the key is made of.</param>
/// <param name="format">
/// Formats the key from the values of <paramref name="inputs"/>, in that order. <see langword="null"/> for a table key, which is never written by an update, and for a
/// plain key, which has no text of its own.
/// </param>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DynamoDbKeyTemplate(string attributeName, string text, bool isTableKey, IReadOnlyList<DynamoDbKeyInput> inputs, Func<object?[], string>? format = null)
{
    /// <summary>Gets the key attribute.</summary>
    public string AttributeName { get; } = attributeName;

    /// <summary>Gets the template as declared.</summary>
    public string Text { get; } = text;

    /// <summary>Gets a value indicating whether it is a key of the table.</summary>
    public bool IsTableKey { get; } = isTableKey;

    /// <summary>Gets the properties the key is made of.</summary>
    public IReadOnlyList<DynamoDbKeyInput> Inputs { get; } = inputs;

    /// <summary>Gets what formats the key from the values of <see cref="Inputs"/>, or <see langword="null"/> for a key that an update never writes.</summary>
    public Func<object?[], string>? Format { get; } = format;

    /// <summary>
    /// Gets what reads the values of <see cref="Inputs"/> from a key of the table, for an update that writes an index key made of them or stores them. It gives <see langword="null"/> for a key that does not fit,
    /// and a <see langword="null"/> value for an input that the text does not give back. <see langword="null"/> for a key whose inputs no index uses and none of which has an attribute of its own.
    /// </summary>
    public Func<AttributeValue, object?[]?>? Parse { get; init; }

    /// <summary>
    /// Gets a value indicating whether an index, or a store of the inputs, wants more of an input than the keys of the table give back, such as a timestamp that this key holds to the day. An update through a
    /// generated key then holds that key, see <see cref="IKey.ValueOf"/>.
    /// </summary>
    public bool LosesValues { get; init; }

    /// <summary>
    /// Gets the attribute whose stored text this key of an index is, byte for byte, or <see langword="null"/> for any other key. That is a template of one bare placeholder over a property with an attribute of its
    /// own, whose converter writes a string of the same text as the key: a <see cref="string"/>, also with <c>:raw</c>, a <see cref="Guid"/>, a <see cref="DateOnly"/>, a timestamp in <c>ticks</c> and an enum by name,
    /// each with its built-in or generated converter. An update that has to produce such a key without its input copies it from the attribute, <c>SET key = attribute</c>, which DynamoDB evaluates on the item
    /// and rejects with a <see cref="ValidationException"/> when the item lacks the attribute.
    /// </summary>
    public string? CopyOf { get; init; }
}
