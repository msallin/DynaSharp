namespace DynaSharp.Mapping;

/// <summary>
/// Says once what a project would otherwise repeat on every mapped type: <c>[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, DiscriminatorOptOut = true, OmitEmpty = true)]</c>. It holds for the entities and
/// documents that the assembly declares, nested ones too, and it does not reach a type of another assembly, which has the defaults it was built with.
/// </summary>
/// <remarks>
/// The attribute is read at compile time, for every mapped type, and an analyzer that meets a type from metadata reads the defaults of the assembly of that type. A base type in another assembly counts as part of the type
/// that is mapped: its properties take the defaults of the assembly of that type.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbDefaultsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets how the attribute of a property derives from its name, on entities and on documents. The default is <see cref="AttributeNaming.AsDeclared"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Changing the naming renames stored attributes.</b> Items written before stay as they are, under the old names, and a read skips those and takes the new names for missing: a property keeps its initial value without any error, and a <c>required</c> one throws. For a table that holds
    /// data, either migrate the items, or stay on the old naming and pin what differs with <see cref="DynamoDbAttributeAttribute"/>.
    /// </para>
    /// <para>
    /// <b>Renaming a property does the same.</b> The attribute follows the name of the property under every naming, <see cref="AttributeNaming.AsDeclared"/> too, so a rename refactoring compiles and renames a stored attribute. A property
    /// whose name may change takes <see cref="DynamoDbAttributeAttribute"/>.
    /// </para>
    /// <para>
    /// It goes for every attribute that takes its name from a property, and that includes a plain key: <c>[PartitionKey] public string UserId</c> is the attribute <c>user_id</c> under <see cref="AttributeNaming.SnakeCase"/>. A
    /// table cannot rename a key, so an existing table takes an explicit <see cref="PartitionKeyAttribute.AttributeName"/> or <see cref="DynamoDbAttributeAttribute"/>, which win over this. So does a template of one bare
    /// placeholder: it is the attribute of its property only if its <c>AttributeName</c> is the converted name.
    /// </para>
    /// <para>
    /// It does not touch a name that derives from no property: <c>pk</c>, <c>sk</c>, <c>&lt;index&gt;pk</c>, <c>&lt;index&gt;sk</c> and <c>_type</c> are frozen, whatever the naming. Two properties that end up with one name
    /// are <c>DYNA004</c>, as they would be with one <see cref="DynamoDbAttributeAttribute"/>.
    /// </para>
    /// </remarks>
    public AttributeNaming Naming { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an entity without <see cref="DiscriminatorAttribute"/> behaves as if it had <c>[Discriminator(OptOut = true)]</c>, <see cref="DiscriminatorAttribute.OptOut"/>, for a service whose
    /// tables other tools write, and <c>DYNA020</c> stays away. A <c>[Discriminator("X")]</c> on a type still wins.
    /// </summary>
    public bool DiscriminatorOptOut { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every string, list, set, dictionary and array behaves as if it had <see cref="OmitEmptyAttribute"/>. A property that feeds a key is always written, and so is one that a read
    /// cannot do without, as <see cref="OmitEmptyAttribute"/> says. An empty list then reads back as null or as the initial value, and <c>[OmitEmpty(false)]</c> takes it back for a property or a type.
    /// </summary>
    public bool OmitEmpty { get; set; }

    /// <summary>
    /// Gets or sets the table of every entity of the assembly that declares none with <see cref="DynamoDbEntityAttribute.Table"/>, for a service whose entities share one table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The entity wins.</b> <c>[DynamoDbEntity(Table = "sessions")]</c> puts one entity into a table of its own, and an explicit name of <see cref="DynamoDbClient.Table{T}(string)"/> wins over both.
    /// </para>
    /// <para>
    /// <b>The rules are those of the entity.</b> The name is checked as <see cref="DynamoDbEntityAttribute.Table"/> says, and the entities that end up in the table must agree on the names of their keys.
    /// </para>
    /// </remarks>
    public string? Table { get; set; }
}
