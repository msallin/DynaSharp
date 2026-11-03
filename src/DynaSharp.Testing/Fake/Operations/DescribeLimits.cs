using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>DescribeLimits</c>: the default quotas of an account in a region, as the API reference lists them, 80,000 units for the account and 40,000 for a table. The fake enforces
/// none of them.
/// </summary>
internal static class DescribeLimits
{
    /// <summary>The operation.</summary>
    public const string Name = "DescribeLimits";

    private const int AccountUnits = 80_000;
    private const int TableUnits = 40_000;

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request, which has no members.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        ArgumentNullException.ThrowIfNull(response);
        RequestJson.Members(request, Name);
        response.WriteNumber("AccountMaxReadCapacityUnits"u8, AccountUnits);
        response.WriteNumber("AccountMaxWriteCapacityUnits"u8, AccountUnits);
        response.WriteNumber("TableMaxReadCapacityUnits"u8, TableUnits);
        response.WriteNumber("TableMaxWriteCapacityUnits"u8, TableUnits);
    }
}
