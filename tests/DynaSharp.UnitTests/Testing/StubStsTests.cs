using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Credentials;
using DynaSharp.UnitTests.Generators;

namespace DynaSharp.UnitTests.Testing;

/// <summary>
/// The stub of STS, over a real socket. Bodies and forms are compared with <c>sts_vectors.json</c>, which botocore wrote and parsed, so neither the writer of the stub nor the parser of this library is what a test compares with.
/// A public constructor of the provider runs on the system clock, with 5 seconds per attempt, which a busy machine can use up after the stub has answered, and the provider then asks again. So these tests set
/// <see cref="StubSts.DefaultResponse"/>, which answers every attempt, and do not count requests. The test that needs an order runs on a clock that stands still, where no attempt times out.
/// </summary>
public sealed class StubStsTests
{
    private const string TokenFile = "token";

    // The default maximum of Linux is 4 MB to send and 6 MB to receive, and Windows starts at far less.
    private const int MoreThanTheSocketsHold = 16 * 1024 * 1024;

    // Earlier than every expiration of the vectors, so that the credentials are good when they arrive.
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    // For a provider on the system clock, which rejects what has expired: far enough ahead that no test run reaches it.
    private static readonly DateTimeOffset FarAhead = new(2100, 1, 2, 3, 4, 5, TimeSpan.Zero);

    // Its endpoint resolves nowhere, so that a test that loses its way to the stub fails without reaching the real STS.
    private static readonly AwsRegion NoSuchRegion = AwsRegion.FromName("no-such-region-1");

    // Long enough that only a hang runs into it, also on a machine that is busy with something else.
    private static readonly TimeSpan NoHang = TimeSpan.FromMinutes(2);

    private static StsVector Sample => Response("DocumentationSample");

    private static StsVector Refreshed => Response("Refreshed");

    private static StsVector Irsa => StsVectors.Rows("requests").Single(static vector => vector.Text("name") == "IrsaJwt");

    private static StsVector ServiceError => StsVectors.Rows("errors").Single(static vector => vector.Text("name") == "WithWhitespace");

    public static IEnumerable<StsVector> Requests() => StsVectors.Rows("requests");

    public static IEnumerable<string> ModeledErrors() => StsVectors.ModeledErrors;

    [Test]
    public async Task Constructor_NoArguments_ListensOnAPortOfThisMachine()
    {
        // Act
        using var sts = new StubSts();

        // Assert
        await Assert.That((sts.Endpoint.Scheme, sts.Endpoint.Host, sts.Endpoint.Port > 0, sts.Endpoint.AbsolutePath)).IsEqualTo(("http", "127.0.0.1", true, "/"));
    }

    [Test]
    public async Task Constructor_TwoInstances_ListenOnTwoPorts()
    {
        // Act
        using var first = new StubSts();
        using var second = new StubSts();

        // Assert
        await Assert.That(first.Endpoint.Port).IsNotEqualTo(second.Endpoint.Port);
    }

    [Test]
    public async Task Credentials_ValuesOfTheSample_IsTheBodyBotocoreParsed()
    {
        // Act
        var response = StubStsResponse.Credentials(CredentialsOf(Sample));

        // Assert
        await Assert.That((response.StatusCode, response.Body)).IsEqualTo((HttpStatusCode.OK, Sample.Text("body")));
    }

    [Test]
    public async Task Enqueue_RequestOfTheSample_SendsTheBodyBotocoreParsed()
    {
        // Arrange
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Sample)));

        // Act
        using var response = await PostAsync(sts, Irsa.Text("body"), Irsa.Text("content_type"));

        // Assert
        await Assert.That((response.StatusCode, await response.Content.ReadAsStringAsync())).IsEqualTo((HttpStatusCode.OK, Sample.Text("body")));
    }

    [Test]
    public async Task Enqueue_RoleWithAPath_DerivesTheSessionArnFromTheNameOfTheRole()
    {
        // Arrange (the session ARN of a role has no path, whatever the path of the role)
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Sample)));

        // Act
        var body = await PostAndReadAsync(sts, "RoleArn=arn%3Aaws%3Aiam%3A%3A210987654321%3Arole%2Fteam%2Fpayments%2Fsettle&RoleSessionName=a-session");

        // Assert
        await Assert.That(ElementsOf(body, "Arn", "AssumedRoleId")).IsEqualTo(("arn:aws:sts::210987654321:assumed-role/settle/a-session", "AROACLKWSDQRAOEXAMPLE:a-session"));
    }

    [Test]
    public async Task Enqueue_RoleOfAnotherPartition_KeepsThePartition()
    {
        // Arrange
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Sample)));

        // Act
        var body = await PostAndReadAsync(sts, "RoleArn=arn%3Aaws-cn%3Aiam%3A%3A123456789012%3Arole%2Forders&RoleSessionName=a-session");

        // Assert
        await Assert.That(ElementsOf(body, "Arn", "Arn").First).IsEqualTo("arn:aws-cn:sts::123456789012:assumed-role/orders/a-session");
    }

    [Test]
    public async Task Enqueue_RoleThatIsNoArn_UsesTheAccountOfTheSample()
    {
        // Arrange (a stub does not judge a request, it says what was sent)
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Sample)));

        // Act
        var body = await PostAndReadAsync(sts, "RoleArn=orders&RoleSessionName=a-session");

        // Assert
        await Assert.That(ElementsOf(body, "Arn", "Arn").First).IsEqualTo("arn:aws:sts::123456789012:assumed-role/orders/a-session");
    }

    [Test]
    public async Task Credentials_ExpirationInAnotherOffset_GoesOutInUtcToTheSecond()
    {
        // Arrange
        var expiration = new DateTimeOffset(2026, 10, 31, 20, 34, 56, 789, TimeSpan.FromHours(2));

        // Act
        var response = StubStsResponse.Credentials(new AwsCredentials("a", "b", "c", expiration));

        // Assert
        await Assert.That(ElementsOf(response.Body, "Expiration", "Expiration").First).IsEqualTo("2026-10-31T18:34:56Z");
    }

    [Test]
    public async Task Credentials_ValuesWithMarkup_AreEscaped()
    {
        // Act
        var response = StubStsResponse.Credentials(new AwsCredentials("a&b", "c<d>e", "f\"g'h", FarAhead));

        // Assert
        await Assert.That((ElementsOf(response.Body, "AccessKeyId", "SecretAccessKey"), ElementsOf(response.Body, "SessionToken", "SessionToken").First))
            .IsEqualTo((("a&amp;b", "c&lt;d&gt;e"), "f&quot;g&apos;h"));
    }

    [Test]
    public async Task Enqueue_RoleAndSessionWithMarkup_AreEscaped()
    {
        // Arrange
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Sample)));

        // Act
        var body = await PostAndReadAsync(sts, "RoleArn=a%26b&RoleSessionName=%3Cs%3E");

        // Assert
        await Assert.That(ElementsOf(body, "Arn", "AssumedRoleId")).IsEqualTo(("arn:aws:sts::123456789012:assumed-role/a&amp;b/&lt;s&gt;", "AROACLKWSDQRAOEXAMPLE:&lt;s&gt;"));
    }

    [Test]
    public async Task Credentials_Null_ThrowsArgumentNullException()
    {
        // Act
        var act = () => StubStsResponse.Credentials(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("credentials");
    }

    [Test]
    public async Task Credentials_NoSessionToken_ThrowsArgumentException()
    {
        // Act
        var act = () => StubStsResponse.Credentials(new AwsCredentials("a", "b", null, FarAhead));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("credentials");
    }

    [Test]
    public async Task Credentials_NoExpiration_GoesOutWithAFixedDateFarAhead()
    {
        // Act
        var response = StubStsResponse.Credentials(new AwsCredentials("a", "b", "c"));

        // Assert
        await Assert.That(ElementsOf(response.Body, "Expiration", "Expiration").First).IsEqualTo("2100-01-01T00:00:00Z");
    }

    // The sample of the README of the package: credentials as a test writes them, without an expiration, which STS always sends.
    [Test]
    public async Task GetCredentialsAsync_CredentialsWithoutExpiration_GetsThemWithTheDefaultExpiration()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token")));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    // The default must not open a refresh window: a second call keeps the credentials, so a test that queued one answer needs no second.
    [Test]
    public async Task GetCredentialsAsync_TwiceWithCredentialsWithoutExpiration_FetchesOnce()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token")));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(sts.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Constructor_NoValues_IsAnEmptyOk()
    {
        // Act
        var response = new StubStsResponse();

        // Assert
        await Assert.That((response.StatusCode, response.Body)).IsEqualTo((HttpStatusCode.OK, string.Empty));
    }

    [Test]
    public async Task Enqueue_BodyOfYourOwn_SendsItAsItStands()
    {
        // Arrange
        using var sts = new StubSts();
        sts.Enqueue(new StubStsResponse { StatusCode = HttpStatusCode.ServiceUnavailable, Body = "not <xml" });

        // Act
        using var response = await PostAsync(sts, "Action=AssumeRoleWithWebIdentity");

        // Assert
        await Assert.That((response.StatusCode, await response.Content.ReadAsStringAsync())).IsEqualTo((HttpStatusCode.ServiceUnavailable, "not <xml"));
    }

    [Test]
    public async Task Enqueue_Answer_GoesOutAsTextXml()
    {
        // Arrange
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Error("InvalidIdentityToken"));

        // Act
        using var response = await PostAsync(sts, "Action=AssumeRoleWithWebIdentity");

        // Assert
        await Assert.That(response.Content.Headers.ContentType!.ToString()).IsEqualTo("text/xml");
    }

    [Test]
    [MethodDataSource(nameof(Requests))]
    public async Task Requests_RowOfBotocore_ReadsTheRoleTheSessionAndTheToken(StsVector vector)
    {
        // Arrange (LongToken comes in over several reads of the socket)
        using var sts = new StubSts();
        sts.DefaultResponse = StubStsResponse.Credentials(CredentialsOf(Sample));

        // Act
        using var _ = await PostAsync(sts, vector.Text("body"), vector.Text("content_type"));

        // Assert
        var request = sts.Requests.Single();
        await Assert.That((request.Action, request.RoleArn, request.RoleSessionName, request.WebIdentityToken))
            .IsEqualTo(("AssumeRoleWithWebIdentity", vector.Text("role_arn"), vector.Text("role_session_name"), vector.Text("token")));
    }

    [Test]
    public async Task Requests_RowOfBotocore_KeepsTheWholeFormAndTheHeaders()
    {
        // Arrange
        using var sts = new StubSts();
        sts.DefaultResponse = StubStsResponse.Credentials(CredentialsOf(Sample));

        // Act
        using var _ = await PostAsync(sts, Irsa.Text("body"), Irsa.Text("content_type"));

        // Assert
        var request = sts.Requests.Single();
        await Assert.That((request.Form["Version"], request.Form["WebIdentityToken"], request.Headers.Contains($"Content-Type: {Irsa.Text("content_type")}"))).IsEqualTo(("2011-06-15", Irsa.Text("token"), true));
    }

    [Test]
    public async Task Requests_NoBody_RecordsEmptyFields()
    {
        // Arrange (a GET has no Content-Length)
        using var sts = new StubSts();
        sts.DefaultResponse = new StubStsResponse();

        // Act
        using var get = new HttpRequestMessage(HttpMethod.Get, sts.Endpoint);
        using var _ = await SendAsync(get);

        // Assert
        var request = sts.Requests.Single();
        await Assert.That((request.Action, request.RoleArn, request.RoleSessionName, request.WebIdentityToken, request.Form.Count)).IsEqualTo((string.Empty, string.Empty, string.Empty, string.Empty, 0));
    }

    [Test]
    public async Task Requests_FieldWithoutAValue_RecordsItEmpty()
    {
        // Arrange
        using var sts = new StubSts();
        sts.DefaultResponse = new StubStsResponse();

        // Act
        using var _ = await PostAsync(sts, "Action&Version=1");

        // Assert
        await Assert.That((sts.Requests.Single().Form["Action"], sts.Requests.Single().Form["Version"])).IsEqualTo((string.Empty, "1"));
    }

    [Test]
    public async Task Requests_AfterDispose_StaysReadable()
    {
        // Arrange
        var sts = new StubSts { DefaultResponse = new StubStsResponse() };
        using (await PostAsync(sts, "Action=A"))
        {
        }

        // Act
        sts.Dispose();

        // Assert
        await Assert.That(sts.Requests.Single().Action).IsEqualTo("A");
    }

    [Test]
    [MethodDataSource(nameof(ModeledErrors))]
    public async Task Error_CodeOfTheModel_HasTheStatusOfTheModel(string code)
    {
        // Act
        var response = StubStsResponse.Error(code);

        // Assert
        await Assert.That((int)response.StatusCode).IsEqualTo(StsVectors.ModeledStatus(code));
    }

    [Test]
    [Arguments("AccessDenied")]
    [Arguments("Throttling")]
    [Arguments("SomethingNew")]
    public async Task Error_CodeOutsideTheModel_Is400(string code)
    {
        // Act
        var response = StubStsResponse.Error(code);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Error_ValuesOfTheVector_IsTheBodyBotocoreParsed()
    {
        // Act
        var response = StubStsResponse.Error(ServiceError.Text("code"), "Not authorized to perform sts:AssumeRoleWithWebIdentity");

        // Assert
        await Assert.That(response.Body).IsEqualTo(ServiceError.Text("body"));
    }

    [Test]
    public async Task Error_NoMessage_HasTheMessageOfTheVectors()
    {
        // Act
        var response = StubStsResponse.Error("InvalidIdentityToken");

        // Assert
        await Assert.That(ElementsOf(response.Body, "Code", "Message")).IsEqualTo(("InvalidIdentityToken", "The message of the service."));
    }

    [Test]
    public async Task Error_StatusCode_ReplacesTheOneOfTheModel()
    {
        // Act
        var response = StubStsResponse.Error("InternalFailure", statusCode: HttpStatusCode.InternalServerError);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
    }

    [Test]
    public async Task Error_ValuesWithMarkup_AreEscaped()
    {
        // Act
        var response = StubStsResponse.Error("a&b", "c<d");

        // Assert
        await Assert.That(ElementsOf(response.Body, "Code", "Message")).IsEqualTo(("a&amp;b", "c&lt;d"));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Error_NoCode_ThrowsArgumentException(string? code)
    {
        // Act
        var act = () => StubStsResponse.Error(code!);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithParameterName("code");
    }

    [Test]
    public async Task GetCredentialsAsync_ErrorOfTheStub_NamesTheCode()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.DefaultResponse = StubStsResponse.Error("IDPRejectedClaim");
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).Contains("IDPRejectedClaim");
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointOfTheStub_ReturnsWhatItWasGiven()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials(Sample.Text("access_key_id"), Sample.Text("secret_access_key"), Sample.Text("session_token"), FarAhead));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.Expiration))
            .IsEqualTo((Sample.Text("access_key_id"), Sample.Text("secret_access_key"), Sample.Text("session_token"), FarAhead));
    }

    [Test]
    public async Task GetCredentialsAsync_ValuesWithMarkup_ComeBackAsTheyWere()
    {
        // Arrange (what the escaping is for: the values a provider reads are the values a test gave)
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        var given = new AwsCredentials("a&b", "c<d>e", "f\"g'h", FarAhead);
        sts.DefaultResponse = StubStsResponse.Credentials(given);
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken)).IsEqualTo((given.AccessKeyId, given.SecretAccessKey, given.SessionToken));
    }

    [Test]
    public async Task GetCredentialsAsync_EndpointOfTheStub_SendsTheTokenOfTheFile()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials("a", "b", "c", FarAhead));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint, "a-session");

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        var request = sts.Requests[0];
        await Assert.That((request.RoleArn, request.RoleSessionName, request.WebIdentityToken)).IsEqualTo((Irsa.Text("role_arn"), "a-session", Irsa.Text("token")));
    }

    [Test]
    public async Task SendAsync_ProviderOnTheStub_SignsWithTheSessionToken()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials(Sample.Text("access_key_id"), Sample.Text("secret_access_key"), Sample.Text("session_token"), FarAhead));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);
        using var dynamoDb = new StubHandler(static _ => new StubResponse { Body = "{}" });
        using var client = TestClient.Create(dynamoDb, new ManualTimeProvider(Now), credentials: provider);

        // Act
        await client.Table("Shop", "pk").Get("a").ItemAsync();

        // Assert
        await Assert.That(dynamoDb.Requests.Single().Headers["X-Amz-Security-Token"]).IsEqualTo(Sample.Text("session_token"));
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task SendAsync_EnvironmentSelectsWebIdentity_SignsWithTheSessionTokenOfEachFetch()
    {
        // Arrange (the variables of the process, the default chain and a file on disk, as a pod has them)
        using var sts = new StubSts();
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Sample)));
        sts.Enqueue(StubStsResponse.Credentials(CredentialsOf(Refreshed)));
        using var directory = TokenDirectory();
        using var environment = new ScopedEnvironmentVariables(
            ("AWS_ENDPOINT_URL_STS", sts.Endpoint.AbsoluteUri),
            ("AWS_ENDPOINT_URL", null),
            ("AWS_STS_REGIONAL_ENDPOINTS", null),
            ("AWS_WEB_IDENTITY_TOKEN_FILE", directory.Combine(TokenFile)),
            ("AWS_ROLE_ARN", Irsa.Text("role_arn")),
            ("AWS_ROLE_SESSION_NAME", null),
            ("AWS_ACCESS_KEY_ID", null),
            ("AWS_SECRET_ACCESS_KEY", null),
            ("AWS_SESSION_TOKEN", null));
        using var dynamoDb = new StubHandler(static _ => new StubResponse { Body = "{}" });
        var time = new ManualTimeProvider(Now);
        using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = NoSuchRegion, HttpHandler = dynamoDb, TimeProvider = time, MaxAttempts = 1 });
        var table = client.Table("Shop", "pk");

        // Act
        await table.Get("a").ItemAsync();
        time.Advance(ExpirationOf(Sample) - TimeSpan.FromMinutes(9) - Now);
        await table.Get("a").ItemAsync();

        // Assert
        var signed = dynamoDb.Requests.Select(static request => (request.Headers["X-Amz-Security-Token"], request.Headers["Authorization"].Split('/')[0].Split('=')[1])).ToList();
        await Assert.That(signed).IsEquivalentTo([(Sample.Text("session_token"), Sample.Text("access_key_id")), (Refreshed.Text("session_token"), Refreshed.Text("access_key_id"))]);
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task GetCredentialsAsync_EndpointAndVariable_TheEndpointWins()
    {
        // Arrange
        using var chosen = new StubSts();
        using var other = new StubSts();
        using var directory = TokenDirectory();
        chosen.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials("a", "b", "c", FarAhead));
        using var environment = new ScopedEnvironmentVariables(("AWS_ENDPOINT_URL_STS", other.Endpoint.AbsoluteUri));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), chosen.Endpoint);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((chosen.Requests.Count > 0, other.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    [NotInParallel(TestClient.ProcessEnvironment)]
    public async Task GetCredentialsAsync_RegionAndVariable_TheVariableWins()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        sts.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials("a", "b", "c", FarAhead));
        using var environment = new ScopedEnvironmentVariables(("AWS_ENDPOINT_URL_STS", sts.Endpoint.AbsoluteUri));
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), NoSuchRegion);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(sts.Requests.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GetCredentialsAsync_NothingQueued_NamesWhatIsMissing()
    {
        // Arrange
        using var sts = new StubSts();
        using var directory = TokenDirectory();
        var provider = new WebIdentityCredentialsProvider(directory.Combine(TokenFile), Irsa.Text("role_arn"), sts.Endpoint);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("STS rejected the web identity token with StubStsNoResponse.");
    }

    [Test]
    public async Task Requests_NothingQueued_SaysHowToQueueInTheBody()
    {
        // Arrange
        using var sts = new StubSts();

        // Act
        using var response = await PostAsync(sts, "Action=A");

        // Assert
        await Assert.That((response.StatusCode, ElementsOf(await response.Content.ReadAsStringAsync(), "Code", "Code").First)).IsEqualTo((HttpStatusCode.BadRequest, "StubStsNoResponse"));
    }

    [Test]
    public async Task Enqueue_QueuedAndDefault_AnswersTheQueueFirstAndTheDefaultAfterIt()
    {
        // Arrange
        using var sts = new StubSts();
        sts.Enqueue(new StubStsResponse { Body = "first" });
        sts.Enqueue(new StubStsResponse { Body = "second" });
        sts.DefaultResponse = new StubStsResponse { Body = "default" };

        // Act
        string[] answers = [await PostAndReadAsync(sts, "a"), await PostAndReadAsync(sts, "b"), await PostAndReadAsync(sts, "c"), await PostAndReadAsync(sts, "d")];

        // Assert
        await Assert.That(answers).IsEquivalentTo(["first", "second", "default", "default"]);
    }

    [Test]
    public async Task Enqueue_Null_ThrowsArgumentNullException()
    {
        // Arrange
        using var sts = new StubSts();

        // Act
        var act = () => sts.Enqueue(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("response");
    }

    [Test]
    public async Task Requests_ClientsAtOnce_AnswersAndRecordsEachOne()
    {
        // Arrange
        using var sts = new StubSts();
        sts.DefaultResponse = new StubStsResponse { Body = "ok" };

        // Act
        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => PostAndReadAsync(sts, $"Action={index}")));

        // Assert
        await Assert.That((string.Join(',', answers.Distinct()), string.Join(',', sts.Requests.Select(static request => request.Action).Order()))).IsEqualTo(("ok", "0,1,2,3,4,5,6,7"));
    }

    [Test]
    public async Task Requests_ClientLeavesInTheMiddleOfARequest_RecordsNothingAndServesTheNext()
    {
        // Arrange (one connection at a time, so the next answer comes after the server has seen this one end)
        using var sts = new StubSts();
        sts.DefaultResponse = new StubStsResponse { Body = "ok" };
        using (var client = await ConnectAsync(sts))
        {
            await client.GetStream().WriteAsync("POST / HTTP/1.1\r\nContent-Length: 100\r\n\r\npartial"u8.ToArray());
        }

        // Act
        var answer = await PostAndReadAsync(sts, "Action=Next");

        // Assert
        await Assert.That((answer, sts.Requests.Single().Action)).IsEqualTo(("ok", "Next"));
    }

    [Test]
    public async Task Requests_ClientResetsItsConnection_ServesTheNext()
    {
        // Arrange (a linger of zero makes the close a reset, which a read on the other side sees as an error)
        using var sts = new StubSts();
        sts.DefaultResponse = new StubStsResponse { Body = "ok" };
        using (var client = await ConnectAsync(sts))
        {
            await client.GetStream().WriteAsync("POST / HTTP/1.1\r\n"u8.ToArray());
            client.LingerState = new LingerOption(true, 0);
        }

        // Act
        var answer = await PostAndReadAsync(sts, "Action=Next");

        // Assert
        await Assert.That(answer).IsEqualTo("ok");
    }

    [Test]
    public async Task Requests_ClientSendsAnUnreadableLength_ServesTheNext()
    {
        // Arrange
        using var sts = new StubSts();
        sts.DefaultResponse = new StubStsResponse { Body = "ok" };
        using (var client = await ConnectAsync(sts))
        {
            await client.GetStream().WriteAsync("POST / HTTP/1.1\r\nContent-Length: many\r\n\r\n"u8.ToArray());
        }

        // Act
        var answer = await PostAndReadAsync(sts, "Action=Next");

        // Assert
        await Assert.That((answer, sts.Requests.Count)).IsEqualTo(("ok", 1));
    }

    [Test]
    public async Task Dispose_ConnectionInTheMiddleOfARequest_EndsItWithoutAHang()
    {
        // Arrange (a connect returns before the server accepts, so a Dispose right after one stops the server in its accept and shows nothing. A write of more than the buffers of two sockets
        // hold can only end once the server has been reading, and the length says that more is to come, so the server is in its read loop when Dispose comes)
        using var sts = new StubSts();
        using var client = await ConnectAsync(sts);
        var stream = client.GetStream();
        await stream.WriteAsync("POST / HTTP/1.1\r\nContent-Length: 1073741824\r\n\r\n"u8.ToArray());
        await stream.WriteAsync(new byte[MoreThanTheSocketsHold]).AsTask().WaitAsync(NoHang);

        // Act
        await Task.Run(sts.Dispose).WaitAsync(NoHang);
        var answered = await ReadAsync(stream);

        // Assert
        await Assert.That((answered, sts.Requests.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Dispose_NoConnection_StopsListening()
    {
        // Arrange
        using var sts = new StubSts();

        // Act
        await Task.Run(sts.Dispose).WaitAsync(NoHang);

        // Assert
        await Assert.That(async () => await ConnectAsync(sts)).Throws<SocketException>();
    }

    [Test]
    public async Task Dispose_Twice_DoesNothingTheSecondTime()
    {
        // Arrange
        var sts = new StubSts();
        sts.Dispose();

        // Act
        var act = sts.Dispose;

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    private static StsVector Response(string name) => StsVectors.Rows("responses").Single(vector => vector.Text("name") == name);

    private static DateTimeOffset ExpirationOf(StsVector vector) => DateTimeOffset.Parse(vector.Text("expiration"), CultureInfo.InvariantCulture);

    private static AwsCredentials CredentialsOf(StsVector vector) =>
        new(vector.Text("access_key_id"), vector.Text("secret_access_key"), vector.Text("session_token"), ExpirationOf(vector));

    private static TempDirectory TokenDirectory()
    {
        var directory = new TempDirectory();
        directory.Write(TokenFile, Irsa.Text("token"));
        return directory;
    }

    private static async Task<HttpResponseMessage> PostAsync(StubSts sts, string form, string contentType = "application/x-www-form-urlencoded")
    {
        using var content = new StringContent(form, Encoding.UTF8);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, sts.Endpoint) { Content = content };
        return await SendAsync(request);
    }

    // Not the proxy of the process: the request stays on this machine. The response is read to its end before the client goes.
    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = NoHang };
        return await http.SendAsync(request);
    }

    private static async Task<string> PostAndReadAsync(StubSts sts, string form)
    {
        using var response = await PostAsync(sts, form);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<TcpClient> ConnectAsync(StubSts sts)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, sts.Endpoint.Port);
            return client;
        }
        catch (SocketException)
        {
            client.Dispose();
            throw;
        }
    }

    // A connection the server closed reads as the end of the stream, and one still in the queue of a listener that stopped reads as a reset.
    private static async Task<int> ReadAsync(NetworkStream stream)
    {
        try
        {
            return await stream.ReadAsync(new byte[1]).AsTask().WaitAsync(NoHang);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    // Input:  "<Code>a</Code><Message>b</Message>", "Code", "Message"
    // Result: ("a", "b")
    private static (string First, string Second) ElementsOf(string body, string first, string second) => (ElementOf(body, first), ElementOf(body, second));

    private static string ElementOf(string body, string name)
    {
        var start = body.IndexOf($"<{name}>", StringComparison.Ordinal) + name.Length + 2;
        return body[start..body.IndexOf($"</{name}>", StringComparison.Ordinal)];
    }
}
