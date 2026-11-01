namespace DynaSharp.Requests;

/// <summary>
/// The type of a key attribute.
/// </summary>
/// <param name="AttributeName">The attribute name.</param>
/// <param name="AttributeType">The scalar type.</param>
public sealed record AttributeDefinition(string AttributeName, ScalarAttributeType AttributeType);
