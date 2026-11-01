using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Mapping;

/// <summary>
/// An entity whose items name their type in a discriminator attribute, which is what a mixed read dispatches on, so only such a type fits
/// <c>shop.Query(pk).Of&lt;Customer, Order&gt;()</c>. The source generator adds this interface to a type with <c>[Discriminator("ORDER")]</c> and leaves it off a type
/// that opted out, which makes a mixed read of that type a compile error.
/// </summary>
/// <remarks>
/// A type written by hand that implements it must have a <see cref="DynamoDbEntityInfo.Discriminator"/>; without one the mixed read throws
/// <see cref="ArgumentException"/>.
/// </remarks>
/// <typeparam name="TSelf">The implementing type.</typeparam>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "It exists for a generic constraint: what it says about the type is known to the generator, and an attribute cannot constrain a type argument.")]
public interface IDynamoDbDiscriminated<TSelf> : IDynamoDbEntity<TSelf>
    where TSelf : IDynamoDbDiscriminated<TSelf>;
