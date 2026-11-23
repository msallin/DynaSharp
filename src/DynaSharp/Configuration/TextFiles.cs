namespace DynaSharp.Configuration;

/// <summary>The default <see cref="ReadTextFile"/> and <see cref="WriteTextFile"/>, which use the disk, and what their failures mean.</summary>
internal static class TextFiles
{
    /// <summary>Gets the default <see cref="ReadTextFile"/>, which reads from disk (see <see cref="ReadFromDisk"/>). One shared delegate, so that no call site converts a method group.</summary>
    public static ReadTextFile DiskReader { get; } = ReadFromDisk;

    /// <summary>Gets the writer to disk. One shared delegate, so that no call site converts a method group.</summary>
    public static WriteTextFile DiskWriter { get; } = ReplaceOnDisk;

    /// <summary>The default <see cref="ReadTextFile"/>: reads a UTF-8 file from disk.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The content, or <see langword="null"/> if the file or its folder does not exist.</returns>
    public static string? ReadFromDisk(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tells whether an exception from a <see cref="ReadTextFile"/> means that the file cannot be read (permissions, I/O, a path
    /// the platform does not support), as opposed to a bug.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> if the file cannot be read.</returns>
    public static bool IsFileReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    /// <summary>Describes a failure for which <see cref="IsFileReadFailure"/> is true, without paths or content.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>For example <c>access denied</c>.</returns>
    public static string DescribeFileReadFailure(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "access denied",
        IOException => "I/O error",
        _ => "path not supported",
    };

    // As botocore's JSONFileCache: the new content goes into a file next to the old one, which then takes its place in one step, so that a reader
    // never sees half a file. The new file starts as a copy of the old one, so that it keeps its permissions (0600, as the AWS CLI writes it)
    // on every platform without code for each.
    private static void ReplaceOnDisk(string path, string content)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(path, temporary);
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
