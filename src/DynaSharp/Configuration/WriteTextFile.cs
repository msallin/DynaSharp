namespace DynaSharp.Configuration;

/// <summary>
/// Replaces the content of a text file that exists. This is the seam through which a refreshed SSO token goes back into the token cache of the
/// AWS CLI, so that tests do not touch the disk.
/// </summary>
/// <param name="path">The path of the file.</param>
/// <param name="content">The new content.</param>
/// <exception cref="IOException">The file cannot be written.</exception>
/// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
internal delegate void WriteTextFile(string path, string content);
