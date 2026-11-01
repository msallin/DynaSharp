using System.Security.Cryptography;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Large object pointer / chunking</see>: an item holds at most 400 KB, so a larger value is cut into chunks in one partition, with a manifest that is written last. A reader that finds
/// the manifest finds every chunk. Beyond a few MB, a pointer to S3 is the better item.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class LargeObjectChunkingTests(Backend backend)
{
    private const int ChunkSize = 300 * 1024;
    private const string FileId = "report";

    [Test]
    public async Task Put_ItemOverTheLimit_IsRejected()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var whole = new StoredChunk { FileId = FileId, Index = 0, Data = RandomNumberGenerator.GetBytes(500 * 1024) };

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () => await sandbox.Shop.Of<StoredChunk>().Put(whole).SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("size", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task Load_ChunkedFile_GivesBackWhatWasSaved()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var content = RandomNumberGenerator.GetBytes(1024 * 1024);

        // Act
        await SaveAsync(sandbox, content);
        var loaded = await LoadAsync(sandbox);

        // Assert
        await Assert.That(loaded!.SequenceEqual(content)).IsTrue();
    }

    [Test]
    public async Task Load_NoManifestYet_FindsNoFileAlthoughChunksExist()
    {
        // Arrange (a writer that stopped halfway)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Of<StoredChunk>().Put(new StoredChunk { FileId = FileId, Index = 0, Data = new byte[10] }).SendAsync();

        // Act
        var loaded = await LoadAsync(sandbox);

        // Assert
        await Assert.That(loaded).IsNull();
    }

    private static async Task SaveAsync(CommandSandbox sandbox, byte[] content)
    {
        var chunks = content.Chunk(ChunkSize).Select(static (part, index) => new StoredChunk { FileId = FileId, Index = index, Data = part }).ToList();
        await sandbox.Shop.Of<StoredChunk>().BatchWrite().PutAll(chunks).SendAsync();
        await sandbox.Shop.Of<FileManifest>().Put(new FileManifest { FileId = FileId, Chunks = chunks.Count }).SendAsync();
    }

    private static async Task<byte[]?> LoadAsync(CommandSandbox sandbox)
    {
        var manifest = await sandbox.Shop.Of<FileManifest>().Get(FileManifest.Keys.Of(FileId)).Consistent().ItemAsync();
        if (manifest is null)
        {
            return null;
        }

        var content = new List<byte>();
        await foreach (var chunk in sandbox.Shop.Of<StoredChunk>().Query(StoredChunk.Keys.Pk.Of(FileId), StoredChunk.Keys.Sk.BeginsWith()).Consistent().ItemsAsync())
        {
            content.AddRange(chunk.Data.Span);
        }

        return [.. content];
    }
}

[DynamoDbEntity]
[Discriminator("CHUNK")]
[PartitionKey("FILE#{FileId}")]
[SortKey("CHUNK#{Index:d4}")]
internal sealed partial record StoredChunk
{
    public required string FileId { get; init; }

    public required int Index { get; init; }

    public required ReadOnlyMemory<byte> Data { get; init; }
}

[DynamoDbEntity]
[Discriminator("MANIFEST")]
[PartitionKey("FILE#{FileId}")]
[SortKey("MANIFEST")]
internal sealed partial record FileManifest
{
    public required string FileId { get; init; }

    public required int Chunks { get; init; }
}
