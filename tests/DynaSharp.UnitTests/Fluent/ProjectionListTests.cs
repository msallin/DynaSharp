using System.Buffers;
using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

public sealed class ProjectionListTests
{
    [Test]
    [Arguments("a, a", "#n0")]
    [Arguments("a, b, a", "#n0, #n1")]
    [Arguments("a, b, b", "#n0, #n1")]
    [Arguments("a.b[1], a.b[1], a.b", "#n0.#n1[1], #n0.#n1")]
    [Arguments("a, a.b", "#n0, #n0.#n1")]
    [Arguments("a[1], a[10]", "#n0[1], #n0[10]")]
    public async Task Add_SamePathTwice_WritesItOnce(string paths, string expected)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(paths);
        using var parts = new Parts();

        // Act
        parts.List.Add(paths.Split(", "));

        // Assert
        await Assert.That(Text(parts.Text)).IsEqualTo(expected);
    }

    [Test]
    public async Task Add_SameAttributeInASecondCall_WritesItOnce()
    {
        // Arrange
        using var parts = new Parts();
        parts.List.Add([Expr.Attr("a"), Expr.Attr("b")]);

        // Act
        parts.List.Add([Expr.Attr("b")]);

        // Assert
        await Assert.That(Text(parts.Text)).IsEqualTo("#n0, #n1");
    }

    [Test]
    [Arguments("a", "#n0")]
    [Arguments("b", "#n0, #n1")]
    public async Task AddName_OneName_AddsItUnlessItIsThere(string name, string expected)
    {
        // Arrange
        using var parts = new Parts();
        parts.List.Add(["a"]);

        // Act
        parts.List.AddName(name);

        // Assert
        await Assert.That(Text(parts.Text)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("a.b", "a", "#n0.#n1")]
    [Arguments("a[1]", "a", "#n0[1]")]
    [Arguments("x, a.b", "a", "#n0, #n1.#n2")]
    [Arguments("x.a", "a", "#n0.#n1, #n1")]
    [Arguments("x, y, z", "z", "#n0, #n1, #n2")]
    public async Task AddName_PathOfTheProjection_CountsByWhatItStartsWith(string paths, string name, string expected)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(paths);
        using var parts = new Parts();
        parts.List.Add(paths.Split(", "));

        // Act
        parts.List.AddName(name);

        // Assert
        await Assert.That(Text(parts.Text)).IsEqualTo(expected);
    }

    [Test]
    public async Task AddName_NameWithADot_IsOneName()
    {
        // Arrange
        using var parts = new Parts();
        parts.List.Add(["x"]);

        // Act
        parts.List.AddName("a.b");

        // Assert
        await Assert.That((Text(parts.Text), parts.Names())).IsEqualTo(("#n0, #n1", """{"#n0":"x","#n1":"a.b"}"""));
    }

    [Test]
    public async Task Resolve_NoProjection_IsNull()
    {
        // Arrange
        using var parts = new Parts();

        // Act
        var paths = parts.List.Resolve();

        // Assert
        await Assert.That(paths).IsNull();
    }

    [Test]
    public async Task Resolve_Paths_PutsTheNamesBack()
    {
        // Arrange
        using var parts = new Parts();

        // Act
        parts.List.Add(["fullName", "address.city", "lines[2].sku", "a\"b"]);
        var paths = parts.List.Resolve();

        // Assert
        await Assert.That(paths).IsEqualTo("fullName, address.city, lines[2].sku, a\"b");
    }

    [Test]
    [Arguments("", "")]
    [Arguments("a", "#n0, #n1")]
    public async Task Complete_Names_AddsThemOnlyToAProjection(string paths, string expected)
    {
        // Arrange (a read without a projection gets every attribute anyway)
        ArgumentNullException.ThrowIfNull(paths);
        using var parts = new Parts();
        parts.List.Add(paths.Length == 0 ? [] : paths.Split(", "));

        // Act
        parts.List.Complete(["a", "b"]);

        // Assert
        await Assert.That(Text(parts.Text)).IsEqualTo(expected);
    }

    private static string Text(PooledByteBuffer buffer) => Encoding.UTF8.GetString(buffer.WrittenSpan);

    // A projection over buffers and maps of its own, as a writer makes one over its clause and its maps.
    private sealed class Parts : IDisposable
    {
        private PlaceholderMaps _maps = new(ArrayPool<byte>.Shared);

        public PooledByteBuffer Text { get; } = new();

        public PooledByteBuffer Scratch { get; } = new();

        public ProjectionList List => new(Text, ref _maps, Scratch);

        public string Names()
        {
            using var body = new PooledByteBuffer();
            _maps.WriteMembers(body);
            var text = Encoding.UTF8.GetString(body.WrittenSpan);
            return text[(text.IndexOf(':', StringComparison.Ordinal) + 1)..];
        }

        public void Dispose()
        {
            _maps.Clear(maxRetainedLength: 0);
            Text.Dispose();
            Scratch.Dispose();
        }
    }
}
