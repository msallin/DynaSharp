using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Everything the emitters need to know about one entity or document.</summary>
/// <param name="Namespace">The namespace, or null for the global one.</param>
/// <param name="Name">The simple name, for example <c>Order</c>.</param>
/// <param name="Identifier">The name as code, for example <c>Order</c> or <c>@class</c>.</param>
/// <param name="FullName">The name for use in code, for example <c>global::Shop.Order</c>.</param>
/// <param name="Keyword">One of <c>record</c>, <c>record struct</c>, <c>class</c> and <c>struct</c>.</param>
/// <param name="HintName">The generated file, for example <c>Shop.Order.g.cs</c>.</param>
/// <param name="ContainingTypes">The types around this one, outermost first.</param>
/// <param name="Constructor">How reads create an instance.</param>
/// <param name="Properties">The properties written or read, base type first, in declaration order.</param>
/// <param name="Entity">The keys and metadata, or null for a document.</param>
/// <param name="Enums">The enums that need a generated converter in this file.</param>
/// <param name="HidesInheritedKeys">Whether <c>Keys</c> needs <c>new</c>, because a base type has a member of that name.</param>
/// <param name="HidesInheritedAttrs">Whether <c>Attrs</c> needs <c>new</c>, because a base type has a member of that name.</param>
/// <param name="HidesInheritedIndexes">Whether <c>Indexes</c> needs <c>new</c>, because a base type has a member of that name.</param>
/// <param name="HidesInheritedTableName">Whether <c>TableName</c> needs <c>new</c>, because a base type has a member of that name.</param>
/// <param name="Reach">The accessibility the type really has, through the types around it, which decides who can name it outside the type.</param>
internal sealed record TypeModel(
    string? Namespace,
    string Name,
    string Identifier,
    string FullName,
    string Keyword,
    string HintName,
    EquatableArray<ContainingType> ContainingTypes,
    ConstructorModel Constructor,
    EquatableArray<PropertyModel> Properties,
    EntityModel? Entity,
    EquatableArray<EnumModel> Enums,
    bool HidesInheritedKeys,
    bool HidesInheritedAttrs,
    bool HidesInheritedIndexes,
    bool HidesInheritedTableName,
    Accessibility Reach)
{
    /// <summary>Gets the property a model refers to by <see cref="PropertyModel.Name"/>.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The property.</returns>
    public PropertyModel Property(string name) => Properties.First(property => property.Name == name);
}
