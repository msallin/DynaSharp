namespace DynaSharp.UnitTests.Fluent;

/// <summary>What a converter reads from a map.</summary>
/// <param name="Sender">Who sent it.</param>
/// <param name="Body">What it says.</param>
internal sealed record Envelope(string Sender, string Body);
