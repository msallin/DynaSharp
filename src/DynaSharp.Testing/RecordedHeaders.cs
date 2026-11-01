using System.Globalization;
using System.Net.Http.Headers;

namespace DynaSharp.Testing;

/// <summary>Copies the headers of a message and its content into the dictionary that recordings expose.</summary>
internal static class RecordedHeaders
{
    /// <summary>
    /// Copies the message headers and the content headers by name, ignoring case. A header with several values is joined with
    /// a comma and a space.
    /// </summary>
    /// <param name="headers">The headers of the request or response.</param>
    /// <param name="content">The content of the message, if it has any.</param>
    /// <returns>The copy.</returns>
    public static Dictionary<string, string> Copy(HttpHeaders headers, HttpContent? content)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers.NonValidated)
        {
            copy[header.Key] = header.Value.ToString();
        }

        if (content is not null)
        {
            foreach (var header in content.Headers.NonValidated)
            {
                copy[header.Key] = header.Value.ToString();
            }

            // Computed on demand, so it is not among the headers above.
            if (content.Headers.ContentLength is { } contentLength)
            {
                copy["Content-Length"] = contentLength.ToString(CultureInfo.InvariantCulture);
            }
        }

        return copy;
    }
}
