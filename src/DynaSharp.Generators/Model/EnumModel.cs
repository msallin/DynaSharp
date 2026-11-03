namespace DynaSharp.Generators;

/// <summary>An enum that needs a generated converter in the file of the type that uses it.</summary>
/// <param name="FullName">The enum as code.</param>
/// <param name="ConverterName">The <c>file</c> struct to generate, for example <c>Shop_OrderStatusConverter</c>. Unique within the file.</param>
/// <param name="StoreAsNumber">A <c>[Flags]</c> enum is stored as a number, since combinations have no name.</param>
/// <param name="UnderlyingType">The underlying type as code.</param>
/// <param name="Members">All members in declaration order: writes use the canonical ones, reads accept all names.</param>
/// <param name="CountsUnknownAsMissing">
/// Whether the converter answers <c>CountsAsMissing</c> for a stored string that is no name of a member, which <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> asks for. Never for a number.
/// </param>
internal sealed record EnumModel(
    string FullName,
    string ConverterName,
    bool StoreAsNumber,
    string UnderlyingType,
    EquatableArray<EnumMember> Members,
    bool CountsUnknownAsMissing);
