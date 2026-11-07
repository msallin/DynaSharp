using System.Buffers;
using DynaSharp.Fluent;
using DynaSharp.Generators;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The analyzer's view of a path, which must be the view of the library at run time.</summary>
public sealed class PathSyntaxTests
{
    [Test]
    public async Task IsValid_EveryShortPath_AgreesWithTheWriter()
    {
        // Arrange: every string of up to six of these characters holds every way to get a path wrong.
        var paths = AllStrings("a.[]1", 6);

        // Act
        var disagreements = paths.Where(static path => PathSyntax.IsValid(path) != WriterAccepts(path)).ToList();

        // Assert
        await Assert.That(paths.Count).IsEqualTo(19531);
        await Assert.That(disagreements).IsEmpty();
    }

    [Test]
    [Arguments("status")]
    [Arguments("address.city")]
    [Arguments("lines[2].sku")]
    [Arguments("matrix[0][1]")]
    [Arguments("a-b c")]
    [Arguments("#odd:name")]
    public async Task IsValid_Path_IsTrue(string path)
    {
        // Arrange
        var expected = true;

        // Act
        var valid = PathSyntax.IsValid(path);

        // Assert
        await Assert.That(valid).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("a..b")]
    [Arguments("a[")]
    [Arguments("a[]")]
    [Arguments("a[x]")]
    [Arguments("a[1]b")]
    [Arguments("a.")]
    [Arguments(".a")]
    [Arguments("[0]")]
    public async Task IsValid_NoPath_IsFalse(string path)
    {
        // Arrange
        var expected = false;

        // Act
        var valid = PathSyntax.IsValid(path);

        // Assert
        await Assert.That(valid).IsEqualTo(expected);
    }

    [Test]
    [Arguments("status", "status")]
    [Arguments("address.city", "address")]
    [Arguments("lines[2].sku", "lines")]
    public async Task Root_Path_IsTheFirstName(string path, string root)
    {
        // Arrange
        var expected = root;

        // Act
        var name = PathSyntax.Root(path);

        // Assert
        await Assert.That(name).IsEqualTo(expected);
    }

    private static List<string> AllStrings(string alphabet, int maxLength)
    {
        var strings = new List<string> { string.Empty };
        var start = 0;
        for (var length = 1; length <= maxLength; length++)
        {
            var end = strings.Count;
            for (var index = start; index < end; index++)
            {
                strings.AddRange(alphabet.Select(character => strings[index] + character));
            }

            start = end;
        }

        return strings;
    }

    private static bool WriterAccepts(string path)
    {
        var maps = new PlaceholderMaps(ArrayPool<byte>.Shared);
        using var text = new PooledByteBuffer(ArrayPool<byte>.Shared, 256);
        try
        {
            maps.AppendPath(text, path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            maps.Clear(maxRetainedLength: 0);
        }
    }
}
