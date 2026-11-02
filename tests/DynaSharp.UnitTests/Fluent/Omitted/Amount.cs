using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A value that brings its converter along, on the type.</summary>
/// <param name="Cents">The amount.</param>
[DynamoDbConverter(typeof(AmountConverter))]
internal readonly record struct Amount(int Cents);
