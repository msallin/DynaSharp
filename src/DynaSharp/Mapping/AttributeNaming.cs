namespace DynaSharp.Mapping;

/// <summary>How the name of a stored attribute derives from the name of a property, set by <see cref="DynamoDbDefaultsAttribute.Naming"/>.</summary>
public enum AttributeNaming
{
    /// <summary>The name of the property, unchanged: <c>OrderId</c>.</summary>
    AsDeclared = 0,

    /// <summary>The rules of <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/>: <c>orderId</c>, and <c>id</c> for <c>ID</c>.</summary>
    CamelCase,

    /// <summary>The rules of <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>: <c>order_id</c>, and <c>xml_reader</c> for <c>XMLReader</c>.</summary>
    SnakeCase,
}
