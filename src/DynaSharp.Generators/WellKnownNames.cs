namespace DynaSharp.Generators;

/// <summary>Metadata names of what DynaSharp.dll declares and the generator looks for.</summary>
internal static class WellKnownNames
{
    public const string EntityAttribute = "DynaSharp.Mapping.DynamoDbEntityAttribute";

    public const string SelectResult = "DynaSharp.SelectResult`1";

    public const string DocumentAttribute = "DynaSharp.Mapping.DynamoDbDocumentAttribute";

    public const string PartitionKeyAttribute = "DynaSharp.Mapping.PartitionKeyAttribute";

    public const string SortKeyAttribute = "DynaSharp.Mapping.SortKeyAttribute";

    public const string GlobalIndexKeyAttribute = "DynaSharp.Mapping.GlobalIndexKeyAttribute";

    public const string LocalIndexKeyAttribute = "DynaSharp.Mapping.LocalIndexKeyAttribute";

    public const string AttributeAttribute = "DynaSharp.Mapping.DynamoDbAttributeAttribute";

    public const string IgnoreAttribute = "DynaSharp.Mapping.DynamoDbIgnoreAttribute";

    public const string KeyOnlyAttribute = "DynaSharp.Mapping.KeyOnlyAttribute";

    public const string ConverterAttribute = "DynaSharp.Mapping.DynamoDbConverterAttribute";

    public const string ConstructorAttribute = "DynaSharp.Mapping.DynamoDbConstructorAttribute";

    public const string DiscriminatorAttribute = "DynaSharp.Mapping.DiscriminatorAttribute";

    public const string VersionAttribute = "DynaSharp.Mapping.VersionAttribute";

    public const string OmitEmptyAttribute = "DynaSharp.Mapping.OmitEmptyAttribute";

    public const string DefaultsAttribute = "DynaSharp.Mapping.DynamoDbDefaultsAttribute";

    public const string DefaultConverterAttribute = "DynaSharp.Mapping.DynamoDbDefaultConverterAttribute";

    public const string ComputedFromAttribute = "DynaSharp.Mapping.ComputedFromAttribute";

    public const string EnumAttribute = "DynaSharp.Mapping.DynamoDbEnumAttribute";

    public const string DocumentInterface = "DynaSharp.Mapping.IDynamoDbDocument<TSelf>";

    public const string ConverterInterface = "DynaSharp.Serialization.IDynamoDbConverter<T>";

    public const string SetElementInterface = "DynaSharp.Serialization.IDynamoDbSetElementConverter<T>";

    public const string AddableInterface = "DynaSharp.Serialization.IAddableConverter";

    public const string AttributeNameArgument = "AttributeName";

    public const string VersionMissingArgument = "Missing";

    /// <summary>The number of <c>VersionMissing.Allow</c>, which is all a generator sees of an enum on an attribute.</summary>
    public const int VersionMissingAllow = 1;

    public const string UnknownNameArgument = "UnknownName";

    /// <summary>The number of <c>UnknownEnumName.CountsAsMissing</c>.</summary>
    public const int UnknownNameCountsAsMissing = 1;

    public const string ExpressionHandler = "DynaSharp.Expressions.ExpressionHandler";

    public const string ReadCommandInterface = "DynaSharp.Fluent.IReadCommand";
}
