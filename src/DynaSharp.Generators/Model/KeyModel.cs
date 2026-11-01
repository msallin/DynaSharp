namespace DynaSharp.Generators;

/// <summary>
/// A key. It is either a template, written as its own attribute, or plain: the attribute of a mapped property is the key, so nothing
/// extra is written.
/// </summary>
/// <param name="AttributeName">The attribute, for example <c>pk</c> or <c>gsi1sk</c>.</param>
/// <param name="BuilderName">The nested class under <c>Keys</c>, for example <c>Pk</c> or <c>Gsi1Sk</c>.</param>
/// <param name="Template">The template, or null for a plain key.</param>
/// <param name="PlainProperty">The property of a plain key, by <see cref="PropertyModel.Name"/>; otherwise null.</param>
/// <param name="SharesAttribute">Whether an earlier key of the entity has the same attribute and template and writes it.</param>
/// <param name="Gates">
/// For the key that writes an attribute several indexes share, what each of them needs: the nullable inputs of a sparse index, nothing for any other.
/// One complete gate is enough, because one index without its key would lose the item. Empty for a key of one index.
/// </param>
internal sealed record KeyModel(
    string AttributeName,
    string BuilderName,
    KeyTemplate? Template,
    string? PlainProperty,
    bool SharesAttribute,
    EquatableArray<EquatableArray<string>> Gates = default);
