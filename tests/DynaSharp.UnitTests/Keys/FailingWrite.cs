namespace DynaSharp.UnitTests.Keys;

/// <summary>A write that must fail, with the value that must not show up in the exception.</summary>
/// <param name="Scenario">What is wrong with the value; also the name of the row.</param>
/// <param name="Secret">The text of the value, as it would appear in a message.</param>
/// <param name="Action">The write.</param>
public readonly record struct FailingWrite(string Scenario, string Secret, KeyWriterAction Action)
{
    public override string ToString() => Scenario;
}
