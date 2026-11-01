namespace DynaSharp.Mapping;

/// <summary>
/// Selects the constructor that reads use, when a type has several. Without it the parameterless constructor is used, of any
/// accessibility, and otherwise the single constructor. Parameters bind to properties by name, ignoring case.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbConstructorAttribute : Attribute
{
}
