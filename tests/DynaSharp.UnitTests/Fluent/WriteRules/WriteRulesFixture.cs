namespace DynaSharp.UnitTests.Fluent;

/// <summary>What the tests of the write rules share: the <see cref="Ticket"/> they write, its key, and the messages of its contradictions.</summary>
internal static class WriteRulesFixture
{
    internal const string Key = """{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"}}""";

    internal const string LoadedAndNew =
        "This write of DynaSharp.UnitTests.Fluent.Ticket holds a version above 0 and asks that no item has its key, which can never both hold: an item that does not exist has no version. For a first write use version 0, to replace an item leave out IfNotExists, and to write the entity as it is call IgnoreVersion().";

    internal const string ExistsAtZero =
        "This write of DynaSharp.UnitTests.Fluent.Ticket holds version 0 and asks that an item has its key, which can never both hold: version 0 means that no item has the key. For a first write leave out IfExists, for an item that exists use the version last read, and to write without looking at the version call IgnoreVersion().";

    internal static readonly DateOnly Day = new(2026, 10, 31);

    internal static string Stored(int revision) =>
        """{"pk":{"S":"TICKET#t1"},"sk":{"S":"AT#2026-10-31"},"bystatepk":{"S":"STATE#Open"},"bystatesk":{"S":"000-main"},"bytitlesk":{"S":"TITLE#untitled"},"_type":{"S":"TICKET"},"Id":{"S":"t1"},"OpenedAt":{"S":"2026-10-31"},"State":{"S":"Open"},"Queue":{"S":"main"},"Rank":{"N":"0"},"Title":{"S":"untitled"},"Votes":{"N":"0"},"Revision":{"N":"""
        + "\"" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}";

    internal static Ticket Held(int revision) => new() { Id = "t1", OpenedAt = Day, Revision = revision };

    // A body with @KEY for the key and @ITEM for the stored item, whose braces would not survive an interpolated raw string.
    internal static string Fill(string body, int revision = 0) => body.Replace("@KEY", Key, StringComparison.Ordinal).Replace("@ITEM", Stored(revision), StringComparison.Ordinal);

    internal static void InvalidCall(UpdateCommand<Ticket> command, long version, out Exception? failure)
    {
        failure = null;
        try
        {
            _ = command.IfVersion(version);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            failure = exception;
        }
    }
}
