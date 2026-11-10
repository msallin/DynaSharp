using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA243: <c>Set</c> of a value that the compiler's nullable analysis says may be null, which removes the attribute.</summary>
public sealed class NullableSetAnalyzerTests
{
    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("CUSTOMER")]
        [PartitionKey("CUSTOMER#{Id}")]
        public partial class Customer
        {
            public required string Id { get; init; }

            public string? Nickname { get; init; }

            public int? Age { get; init; }

            public Address? ShipTo { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string City);

        public sealed class Patch
        {
            public string? Nickname { get; init; }

            public int? Age { get; init; }

            public Address? ShipTo { get; init; }
        }

        public static class Mine
        {
            public static void Set(string attribute, string? value) { }
        }
        """;

    private const string Fields = """
        private static readonly DynamoDbClient client = null!;
        private static readonly Table<Customer> customers = null!;
        private static readonly Customer.Keys.PrimaryKey key = default;

        """;

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, [|dto.Nickname|]).ToRequestJson();""", "Customer.Attrs.Nickname")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.ShipTo, [|dto.ShipTo|]).ToRequestJson();""", "Customer.Attrs.ShipTo")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Age, [|dto.Age|]).ToRequestJson();""", "Customer.Attrs.Age")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, [|(string?)dto.Nickname|]).ToRequestJson();""", "Customer.Attrs.Nickname")]
    [Arguments("""_ = client.TransactWrite().Update(customers, key).Set(Customer.Attrs.Nickname, [|dto.Nickname|]).ToRequestJson();""", "Customer.Attrs.Nickname")]
    public async Task Analyze_SetOfAValueThatMayBeNull_SaysItRemovesTheAttribute(string statement, string handle)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA243 Info: This value may be null, and Set of null removes '{handle}' from the item. Where null means no change, as for a field a request left out, use SetIfNotNull."));
    }

    [Test]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, null).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, (string?)(null)).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, default).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, default(string)).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Nickname, "ann").ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Set(Customer.Attrs.Age, 3).ToRequestJson();""")]
    [Arguments("""if (dto.Nickname is { } nickname) { _ = customers.Update(key).Set(Customer.Attrs.Nickname, nickname).ToRequestJson(); }""")]
    [Arguments("""if (dto.Nickname is null || dto.Age is null) { return; } _ = customers.Update(key).Set(Customer.Attrs.Nickname, dto.Nickname).Set(Customer.Attrs.Age, dto.Age).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).SetIfNotNull(Customer.Attrs.Nickname, dto.Nickname).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Set("Nickname", dto.Nickname).ToRequestJson();""")]
    [Arguments("""_ = customers.Update(key).Add(Customer.Attrs.Age, 1).ToRequestJson();""")]
    [Arguments("""Mine.Set("Nickname", dto.Nickname);""")]
    [Arguments("#nullable disable\n_ = customers.Update(key).Set(Customer.Attrs.Nickname, dto.Nickname).ToRequestJson();\n#nullable restore")]
    public async Task Analyze_SetOfNullOnPurposeOrOfAValueThatIsNot_ReportsNothing(string statement)
    {
        // Arrange (a literal null says the removal, a check before proves the value, and #nullable disable turns the analysis off)
        var source = Run(statement);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Run(string statement) => AnalyzerRun.Member(Fields + "public static void Run(Patch dto)\n{\n" + statement + "\n}\n");

    private static Task<string> ReportAsync(string source) => Shared.ReportWithAsync(source, new NullableSetAnalyzer());
}
