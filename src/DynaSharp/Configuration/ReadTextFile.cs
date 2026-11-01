namespace DynaSharp.Configuration;

/// <summary>
/// Reads a whole text file. This is the seam through which the shared AWS files and the container token file are read, so
/// that tests do not touch the disk.
/// </summary>
/// <param name="path">The path of the file.</param>
/// <returns>The content of the file, or <see langword="null"/> if the file does not exist.</returns>
/// <exception cref="IOException">The file exists but cannot be read.</exception>
/// <exception cref="UnauthorizedAccessException">The process may not read the file.</exception>
internal delegate string? ReadTextFile(string path);
