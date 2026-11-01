using System.Xml;

namespace DynaSharp.Credentials;

/// <summary>
/// The XML document that STS returns for <c>AssumeRoleWithWebIdentity</c>, reduced to the fields that matter. Parsed with
/// <see cref="XmlReader"/>, without reflection and without a serializer, so it works under Native AOT.
/// </summary>
/// <remarks>
/// A success, of which only the four fields of <c>Credentials</c> are read:
/// <code>
/// &lt;AssumeRoleWithWebIdentityResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/"&gt;
///   &lt;AssumeRoleWithWebIdentityResult&gt;
///     &lt;AssumedRoleUser&gt;...&lt;/AssumedRoleUser&gt;
///     &lt;Credentials&gt;
///       &lt;AccessKeyId&gt;ASIA...&lt;/AccessKeyId&gt;
///       &lt;SecretAccessKey&gt;...&lt;/SecretAccessKey&gt;
///       &lt;SessionToken&gt;...&lt;/SessionToken&gt;
///       &lt;Expiration&gt;2026-10-31T18:34:56Z&lt;/Expiration&gt;
///     &lt;/Credentials&gt;
///   &lt;/AssumeRoleWithWebIdentityResult&gt;
/// &lt;/AssumeRoleWithWebIdentityResponse&gt;
/// </code>
/// A failure, of which only <c>Code</c> is read. <c>Message</c> is left alone on purpose: it is free text of the service, and an error
/// about the parameters may quote them, which here includes the token:
/// <code>
/// &lt;ErrorResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/"&gt;
///   &lt;Error&gt;&lt;Type&gt;Sender&lt;/Type&gt;&lt;Code&gt;InvalidIdentityToken&lt;/Code&gt;&lt;Message&gt;...&lt;/Message&gt;&lt;/Error&gt;
///   &lt;RequestId&gt;...&lt;/RequestId&gt;
/// &lt;/ErrorResponse&gt;
/// </code>
/// Elements are compared by local name, so any namespace or prefix is accepted, and elements that are not named here are skipped.
/// </remarks>
/// <param name="Credentials">The fields of <c>Credentials</c>, all <see langword="null"/> for a failure.</param>
/// <param name="ErrorCode">The <c>Code</c> of <c>Error</c>, or <see langword="null"/> if there is none.</param>
internal readonly record struct StsDocument(CredentialsDocument Credentials, string? ErrorCode)
{
    /// <summary>The name of the element that holds the session token, for the message about a missing field.</summary>
    public const string SessionTokenElement = "SessionToken";

    private const string ResultElement = "AssumeRoleWithWebIdentityResult";
    private const string CredentialsElement = "Credentials";
    private const string ErrorElement = "Error";

    // No DTD and no resolver: the document comes over the network, possibly through a proxy, and must not make this process
    // expand entities or fetch anything.
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    /// <summary>Parses a response body.</summary>
    /// <param name="xml">The body.</param>
    /// <param name="document">The fields; a field that is not in the document is <see langword="null"/>.</param>
    /// <returns><see langword="false"/> if the body is not well-formed XML, which is what the page of a proxy usually is, or is cut off.</returns>
    public static bool TryParse(byte[] xml, out StsDocument document)
    {
        string? accessKeyId = null;
        string? secretAccessKey = null;
        string? sessionToken = null;
        string? expiration = null;
        string? code = null;

        // The open elements below the root, by level. A field counts only at its own place: an unknown element may hold a Code of its own.
        var path = new string?[3];
        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, Settings);

            // ReadElementContentAsString already moves past the element, so this loop only calls Read for what it does not consume.
            // Calling it after every element would skip the sibling that follows without whitespace in between.
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                var depth = reader.Depth;
                var name = reader.LocalName;
                if (depth < path.Length)
                {
                    path[depth] = name;
                }

                if (depth == 2 && path[1] == ErrorElement && name == "Code")
                {
                    code = reader.ReadElementContentAsString();
                }
                else if (depth == 3 && path[1] == ResultElement && path[2] == CredentialsElement && IsField(name))
                {
                    var value = reader.ReadElementContentAsString();
                    switch (name)
                    {
                        case "AccessKeyId":
                            accessKeyId = value;
                            break;
                        case "SecretAccessKey":
                            secretAccessKey = value;
                            break;
                        case SessionTokenElement:
                            sessionToken = value;
                            break;
                        default:
                            expiration = value;
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }
        }
        catch (XmlException)
        {
            // The exception quotes parts of the document, so it goes no further.
            document = default;
            return false;
        }

        document = new StsDocument(new CredentialsDocument(accessKeyId, secretAccessKey, sessionToken, expiration, null), code);
        return true;
    }

    private static bool IsField(string name) => name is "AccessKeyId" or "SecretAccessKey" or SessionTokenElement or "Expiration";
}
