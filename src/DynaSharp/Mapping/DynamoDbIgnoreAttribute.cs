namespace DynaSharp.Mapping;

/// <summary>
/// Leaves a property out of the mapping: it is neither written nor read, and a key template cannot use it. To store a value only
/// inside a key, use <see cref="KeyOnlyAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbIgnoreAttribute : Attribute
{
}
