using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using DynaSharp.Credentials;

namespace DynaSharp.Testing;

/// <summary>
/// A canned answer of a <see cref="StubSts"/> to <c>AssumeRoleWithWebIdentity</c>. <see cref="Credentials"/> and <see cref="Error"/> cover what STS says; for anything else, such as a body that is not XML or a
/// status of its own, a test sets <see cref="StatusCode"/> and <see cref="Body"/>: <c>new StubStsResponse { StatusCode = HttpStatusCode.ServiceUnavailable, Body = "busy" }</c>.
/// </summary>
/// <remarks>
/// The bodies keep to what botocore parsed, byte for byte: <c>sts_vectors.json</c> in the tests of this library holds them. The credentials body is the sample of the AWS documentation with other values, so an element the sample
/// has and a test does not choose, such as <c>Audience</c> or <c>RequestId</c>, keeps the value of the sample.
/// </remarks>
public sealed class StubStsResponse
{
    private const string SampleRoleArn = "arn:aws:iam::123456789012:role/orders";
    private const string SampleSessionName = "dynasharp-1793000000";
    private const string SamplePartition = "aws";
    private const string SampleAccount = "123456789012";
    private const string DefaultErrorMessage = "The message of the service.";

    // The source file may have CRLF, and the body has LF on every platform.
    private static readonly CompositeFormat CredentialsBody = CompositeFormat.Parse("""
        <AssumeRoleWithWebIdentityResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
          <AssumeRoleWithWebIdentityResult>
            <SubjectFromWebIdentityToken>system:serviceaccount:shop:orders</SubjectFromWebIdentityToken>
            <Audience>sts.amazonaws.com</Audience>
            <AssumedRoleUser>
              <Arn>{0}</Arn>
              <AssumedRoleId>AROACLKWSDQRAOEXAMPLE:{1}</AssumedRoleId>
            </AssumedRoleUser>
            <Credentials>
              <SessionToken>{2}</SessionToken>
              <SecretAccessKey>{3}</SecretAccessKey>
              <Expiration>{4}</Expiration>
              <AccessKeyId>{5}</AccessKeyId>
            </Credentials>
            <SourceIdentity>SourceIdentityValue</SourceIdentity>
            <Provider>https://oidc.eks.eu-west-1.amazonaws.com/id/EXAMPLE</Provider>
          </AssumeRoleWithWebIdentityResult>
          <ResponseMetadata>
            <RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId>
          </ResponseMetadata>
        </AssumeRoleWithWebIdentityResponse>

        """.ReplaceLineEndings("\n"));

    private static readonly CompositeFormat ErrorBody = CompositeFormat.Parse("""
        <ErrorResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
          <Error>
            <Type>Sender</Type>
            <Code>{0}</Code>
            <Message>{1}</Message>
          </Error>
          <RequestId>c6104cbe-af31-11e0-8154-cbc7ccf896c7</RequestId>
        </ErrorResponse>

        """.ReplaceLineEndings("\n"));

    private readonly AwsCredentials? _credentials;

    /// <summary>Initializes a new instance of the <see cref="StubStsResponse"/> class for a body of your own: HTTP 200 and an empty body unless a test sets them.</summary>
    public StubStsResponse()
    {
    }

    private StubStsResponse(AwsCredentials credentials)
    {
        _credentials = credentials;
        Body = Render(credentials, SampleRoleArn, SampleSessionName);
    }

    /// <summary>Gets the HTTP status code. Defaults to <see cref="HttpStatusCode.OK"/>.</summary>
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    /// <summary>
    /// Gets the response body, as text that goes out as UTF-8 and <c>text/xml</c>. Defaults to empty, which a provider takes for a broken answer. Can be any text. For <see cref="Credentials"/> this is the body for
    /// the role and the session of the sample of the AWS documentation; what a <see cref="StubSts"/> sends has <c>AssumedRoleUser</c> derived from the request instead.
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Creates the answer that hands out credentials: HTTP 200, and the given values in the sample body of the AWS documentation.</summary>
    /// <remarks>
    /// <c>Arn</c> and <c>AssumedRoleId</c> of <c>AssumedRoleUser</c> are derived from the role and the session name of each request. <c>Expiration</c> goes out in UTC, to the second, as <c>yyyy-MM-ddTHH:mm:ssZ</c>. A provider on the system
    /// clock rejects a time in the past, and one that lies within 15 minutes starts a refresh, so a test without a fake clock uses a time far ahead. Values are XML-escaped.
    /// </remarks>
    /// <param name="credentials">The credentials, with <see cref="AwsCredentials.SessionToken"/> and <see cref="AwsCredentials.Expiration"/>, which STS always sends.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="credentials"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="credentials"/> has no session token or no expiration.</exception>
    public static StubStsResponse Credentials(AwsCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.SessionToken is null || credentials.Expiration is null)
        {
            throw new ArgumentException("STS always sends a session token and an expiration, so the credentials must have both.", nameof(credentials));
        }

        return new StubStsResponse(credentials);
    }

    /// <summary>
    /// Creates an <c>ErrorResponse</c> of STS, with the status botocore's model gives the code: 403 for <c>IDPRejectedClaim</c> and <c>RegionDisabledException</c>, and 400 for the rest and for a code the model does not list.
    /// </summary>
    /// <remarks>
    /// A provider reads the code, so a transient code such as <c>IDPCommunicationError</c> or <c>Throttling</c> is retried at 400 as well, and <c>ExpiredTokenException</c> reads the token file again. It also retries by the status,
    /// which is not known here for a code outside the model: the real service sends <c>AccessDenied</c> as 403 and <c>InternalFailure</c> as 500, so a test of a 5xx or a 429 passes <paramref name="statusCode"/>.
    /// </remarks>
    /// <param name="code">The error code, for example <c>InvalidIdentityToken</c>. It is XML-escaped.</param>
    /// <param name="message">The message of the error. A provider leaves it out of its exception. It is XML-escaped.</param>
    /// <param name="statusCode">The status of the response, in place of the one of the model.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null or empty.</exception>
    public static StubStsResponse Error(string code, string? message = null, HttpStatusCode? statusCode = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        return new StubStsResponse
        {
            StatusCode = statusCode ?? (code is "IDPRejectedClaim" or "RegionDisabledException" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest),
            Body = string.Format(CultureInfo.InvariantCulture, ErrorBody, Escape(code), Escape(message ?? DefaultErrorMessage)),
        };
    }

    internal string BodyFor(RecordedStsRequest request) => _credentials is null ? Body : Render(_credentials, request.RoleArn, request.RoleSessionName);

    private static string Render(AwsCredentials credentials, string roleArn, string sessionName) => string.Format(
        CultureInfo.InvariantCulture,
        CredentialsBody,
        Escape(AssumedRoleArn(roleArn, sessionName)),
        Escape(sessionName),
        Escape(credentials.SessionToken!),
        Escape(credentials.SecretAccessKey),
        credentials.Expiration!.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        Escape(credentials.AccessKeyId));

    // The session ARN has the name of the role and not its path. A role that is no ARN at all gets the partition and the account of the sample: a stub does not judge the request.
    // Input:  "arn:aws-cn:iam::123456789012:role/team/orders", "session"
    // Result: "arn:aws-cn:sts::123456789012:assumed-role/orders/session"
    private static string AssumedRoleArn(string roleArn, string sessionName)
    {
        var fields = roleArn.Split(':');
        var (partition, account) = fields.Length == 6 ? (fields[1], fields[4]) : (SamplePartition, SampleAccount);
        return $"arn:{partition}:sts::{account}:assumed-role/{roleArn[(roleArn.LastIndexOf('/', StringComparison.Ordinal) + 1)..]}/{sessionName}";
    }

    private static string Escape(string value) => SecurityElement.Escape(value);
}
