namespace DynaSharp.Generators;

/// <summary>An enum that needs a generated converter in the file of the type that uses it.</summary>
/// <param name="FullName">The enum as code.</param>
/// <param name="ConverterName">The <c>file</c> struct to generate, for example <c>Shop_OrderStatusConverter</c>. Unique within the file.</param>
/// <param name="StoreAsNumber">A <c>[Flags]</c> enum is stored as a number, since combinations have no name.</param>
/// <param name="UnderlyingType">The underlying type as code.</param>
/// <param name="Members">All members in declaration order: writes use the canonical ones, reads accept all names.</param>
internal sealed record EnumModel(
    string FullName,
    string ConverterName,
    bool StoreAsNumber,
    string UnderlyingType,
    EquatableArray<EnumMember> Members);
