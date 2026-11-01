namespace DynaSharp.Generators;

/// <summary>How reads create an instance.</summary>
/// <param name="Parameters">The arguments of the chosen constructor, in order. A generated fields constructor chains to it with these.</param>
/// <param name="DeclareDefaultConstructor">Whether the type has only the implicit constructor, which the generated one would remove.</param>
/// <param name="NeedsFieldsConstructor">Whether a property is read that is not a parameter, so it must be assigned inside the type.</param>
internal sealed record ConstructorModel(
    EquatableArray<ConstructorParameter> Parameters,
    bool DeclareDefaultConstructor,
    bool NeedsFieldsConstructor);
