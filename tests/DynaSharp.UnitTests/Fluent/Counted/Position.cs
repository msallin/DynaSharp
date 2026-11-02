namespace DynaSharp.UnitTests.Fluent;

/// <summary>What a converter reads names into. <see cref="Unknown"/> is no default, so that a property that holds it was left alone by the read.</summary>
internal enum Position
{
    Bow,
    Stern,
    Unknown,
}
