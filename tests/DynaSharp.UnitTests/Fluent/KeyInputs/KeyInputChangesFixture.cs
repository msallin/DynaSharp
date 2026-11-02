namespace DynaSharp.UnitTests.Fluent;

/// <summary>What the tests of <see cref="DynaSharp.Fluent.KeyInputChanges"/> share: the key of the <see cref="Ticket"/> most of them update, and that key in a body.</summary>
internal static class KeyInputChangesFixture
{
    internal const string Key = """{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"}}""";

    internal static readonly DateOnly Day = new(2026, 10, 31);

    internal static readonly Ticket.Keys.PrimaryKey TicketKey = Ticket.Keys.Of("t1", Day);

    internal static string Fill(string body) => body.Replace("@KEY", Key, StringComparison.Ordinal);
}
