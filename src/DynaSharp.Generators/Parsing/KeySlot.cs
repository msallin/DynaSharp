namespace DynaSharp.Generators;

/// <summary>The partition key or the sort key of a table.</summary>
/// <param name="Attribute">The metadata name of its attribute.</param>
/// <param name="Label">The name in messages.</param>
/// <param name="DefaultAttribute">The default DynamoDB attribute of a template.</param>
/// <param name="BuilderName">The nested class under <c>Keys</c>.</param>
/// <param name="Role">What the binder accepts.</param>
internal sealed record KeySlot(string Attribute, string Label, string DefaultAttribute, string BuilderName, KeyRole Role);
