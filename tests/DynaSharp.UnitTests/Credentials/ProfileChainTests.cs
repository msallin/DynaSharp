using DynaSharp.Credentials;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// The profiles that assume a role or federate a web identity, through <see cref="ProfileCredentialsProvider"/>, with STS answered by a stub. The
/// cases follow botocore's <c>AssumeRoleProvider</c>: <c>_get_role_config</c>, <c>_validate_source_profile</c> and <c>_resolve_credentials_from_profile</c>.
/// </summary>
public sealed class ProfileChainTests
{
    private const string ConfigPath = "/aws/config";
    private const string CredentialsPath = "/aws/credentials";
    private const string TokenPath = "/var/run/token";
    private const string Role = "arn:aws:iam::123456789012:role/orders";
    private const string BaseKeys = "aws_access_key_id = AKIABASE\naws_secret_access_key = base-secret\n";

    private static readonly AwsRegion ClientRegion = AwsRegion.FromName("eu-west-1");

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithKeys_AssumesTheRoleWithThem()
    {
        // Arrange (the most common setup: a user's keys and a role in another account)
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\n[profile base]\n{BaseKeys}");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {world.Signers()}").IsEqualTo("ASIA1 AKIABASE");
    }

    [Test]
    public async Task GetCredentialsAsync_ChainOfRoles_AssumesEachWithTheCredentialsOfTheOneBefore()
    {
        // Arrange
        var world = new World($"[profile a]\nrole_arn = {Role}-a\nsource_profile = b\n[profile b]\nrole_arn = {Role}-b\nsource_profile = c\n[profile c]\n{BaseKeys}");

        // Act
        var credentials = await world.Provider("a").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {world.Signers()} {world.Roles()}").IsEqualTo($"ASIA2 AKIABASE,ASIA1 {Role}-b,{Role}-a");
    }

    [Test]
    [Arguments("[profile a]\nrole_arn = r\nsource_profile = b\n[profile b]\nrole_arn = r\nsource_profile = a\n", "'a' -> 'b' -> 'a'")]
    [Arguments("[profile a]\nrole_arn = r\nsource_profile = a\n", "'a' -> 'a'")]
    [Arguments("[profile a]\nrole_arn = r\nsource_profile = b\n[profile b]\nrole_arn = r\nsource_profile = c\n[profile c]\nrole_arn = r\nsource_profile = b\n", "'a' -> 'b' -> 'c' -> 'b'")]
    public async Task GetCredentialsAsync_Loop_FailsNamingTheProfiles(string config, string loop)
    {
        // Arrange
        var world = new World(config);

        // Act
        var act = async () => await world.Provider("a").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"The profiles form a loop: {loop}. A profile may name itself as its source_profile only if it has static keys.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ProfileNamesItselfAndHasKeys_AssumesTheRoleWithItsOwnKeys()
    {
        // Arrange (botocore allows this, so that keys and role can share one profile)
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\n{BaseKeys}");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {world.Signers()}").IsEqualTo("ASIA1 AKIABASE");
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithKeysAndARole_UsesTheKeysAndNotItsRole()
    {
        // Arrange (botocore resolves a source with static keys through the profile providers, which do not assume roles)
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\n[profile base]\nrole_arn = {Role}-base\nsource_profile = other\n{BaseKeys}");

        // Act
        await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{world.Signers()} {world.Roles()}").IsEqualTo($"AKIABASE {Role}");
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileDoesNotExist_FailsNamingBoth()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = missing\n");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The source_profile 'missing' of the profile 'dev' does not exist.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileEmpty_FailsAsAProfileThatDoesNotExist()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile =\n");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("The source_profile '' of the profile 'dev' does not exist.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithoutCredentials_FailsNamingIt()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\n[profile base]\nregion = eu-west-1\n");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The source_profile 'base' of the profile 'dev' has no credentials.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithAProcess_RunsIt()
    {
        // Arrange
        var runner = new ProcessCredentialsProviderTests.FakeRunner($$"""{"Version":1,"AccessKeyId":"AKIAPROCESS","SecretAccessKey":"s"}""");
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\n[profile base]\ncredential_process = creds\n", runner: runner);

        // Act
        await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Signers()).IsEqualTo("AKIAPROCESS");
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithWebIdentity_AssumesTheRoleWithTheFederatedCredentials()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = pod\n[profile pod]\nrole_arn = {Role}-pod\nweb_identity_token_file = {TokenPath}\n");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {world.Actions()}").IsEqualTo("ASIA2 AssumeRoleWithWebIdentity,AssumeRole");
    }

    [Test]
    public async Task GetCredentialsAsync_BothSourceProfileAndCredentialSource_Fails()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\ncredential_source = Environment\n[profile base]\n{BaseKeys}");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The profile 'dev' sets both source_profile and credential_source; a role takes one of them.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments($"role_arn = {Role}\n")]
    [Arguments($"role_arn = {Role}\n{BaseKeys}")]
    public async Task GetCredentialsAsync_RoleWithoutSource_FailsEvenWithKeys(string settings)
    {
        // Arrange (botocore's PartialCredentialsError: the role comes first and takes the profile over)
        var world = new World($"[profile dev]\n{settings}");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The profile 'dev' sets role_arn but neither source_profile nor credential_source, so nothing can assume the role.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_RoleArnEmpty_Fails()
    {
        // Arrange
        var world = new World("[profile dev]\nrole_arn =\nsource_profile = dev\n");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage("The role_arn of the profile 'dev' is empty.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_MfaSerial_FailsNamingItAndWhatToDo()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\nmfa_serial = arn:aws:iam::123456789012:mfa/alice\n[profile base]\n{BaseKeys}");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The profile 'dev' sets mfa_serial. A role that needs the code of an MFA device needs a person at every refresh, which DynaSharp does not ask for. Get a session with the AWS CLI and pass its keys, or assume a role without MFA.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_CredentialSourceEnvironment_AssumesTheRoleWithTheKeysOfTheEnvironment()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\ncredential_source = Environment\n", variables: [("AWS_ACCESS_KEY_ID", "AKIAENV"), ("AWS_SECRET_ACCESS_KEY", "env-secret")]);

        // Act
        await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Signers()).IsEqualTo("AKIAENV");
    }

    [Test]
    public async Task GetCredentialsAsync_CredentialSourceEcsContainerAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var containerReady = new TaskCompletionSource();
        var world = new World(
            $"[profile dev]\nrole_arn = {Role}\ncredential_source = EcsContainer\n",
            variables: [("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/task")],
            container: containerReady.Task);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => world.Provider("dev").GetCredentialsAsync(CancellationToken.None).AsTask());

        // Act
        containerReady.SetResult();
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    [Arguments("Environment", "No credentials from the credential_source Environment of the profile 'dev': AWS_ACCESS_KEY_ID is not set or empty.")]
    [Arguments("EcsContainer", "No credentials from the credential_source EcsContainer of the profile 'dev': neither AWS_CONTAINER_CREDENTIALS_RELATIVE_URI nor AWS_CONTAINER_CREDENTIALS_FULL_URI is set.")]
    [Arguments("Ec2InstanceMetadata", "No credentials from the credential_source Ec2InstanceMetadata of the profile 'dev': it is disabled by AWS_EC2_METADATA_DISABLED=true.")]
    public async Task GetCredentialsAsync_CredentialSourceHasNoCredentials_FailsNamingTheSourceAndWhy(string source, string reason)
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\ncredential_source = {source}\n", variables: [("AWS_EC2_METADATA_DISABLED", "true")]);

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"Assuming the role {Role} needs the credentials of its source, and there are none: {reason}", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("environment")]
    [Arguments("Ec2Config")]
    [Arguments("")]
    public async Task GetCredentialsAsync_UnknownCredentialSource_FailsNamingTheThreeThatExist(string source)
    {
        // Arrange (botocore compares the names exactly)
        var world = new World($"[profile dev]\nrole_arn = {Role}\ncredential_source = {source}\n");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"The credential_source '{source}' of the profile 'dev' is none of Environment, Ec2InstanceMetadata and EcsContainer.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SessionNameDurationAndExternalId_AreSent()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\nrole_session_name = alice\nduration_seconds = 7200\nexternal_id = partner-1\n[profile base]\n{BaseKeys}");

        // Act
        await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].BodyText).IsEqualTo(
            "Action=AssumeRole&Version=2011-06-15&RoleArn=arn%3Aaws%3Aiam%3A%3A123456789012%3Arole%2Forders&RoleSessionName=alice&DurationSeconds=7200&ExternalId=partner-1");
    }

    [Test]
    [Arguments("abc")]
    [Arguments("899")]
    [Arguments("43201")]
    [Arguments("-900")]
    [Arguments("3600.0")]
    [Arguments("")]
    public async Task GetCredentialsAsync_InvalidDuration_FailsNamingTheRange(string duration)
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\nduration_seconds = {duration}\n{BaseKeys}");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The duration_seconds of the profile 'dev' must be a whole number from 900 to 43200.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("900")]
    [Arguments("43200")]
    public async Task GetCredentialsAsync_DurationAtTheLimits_IsSent(string duration)
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\nduration_seconds = {duration}\n{BaseKeys}");

        // Act
        await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].BodyText).Contains($"&DurationSeconds={duration}", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_InvalidSessionName_FailsNamingTheRule()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\nrole_session_name = has space\n{BaseKeys}");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The role_session_name of the profile 'dev' must be 2 to 64 letters, digits or characters of _+=,.@-.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_RegionOfTheClient_SelectsTheEndpoint()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\nregion = ap-south-1\n{BaseKeys}", variables: [("AWS_REGION", "us-west-2")]);

        // Act
        await world.Provider("dev", ClientRegion).GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://sts.eu-west-1.amazonaws.com/");
    }

    [Test]
    [Arguments("AWS_REGION", "https://sts.us-west-2.amazonaws.com/")]
    [Arguments("AWS_DEFAULT_REGION", "https://sts.us-west-2.amazonaws.com/")]
    [Arguments("NONE", "https://sts.ap-south-1.amazonaws.com/")]
    public async Task GetCredentialsAsync_NoClientRegion_TakesTheEnvironmentThenTheProfile(string variable, string endpoint)
    {
        // Arrange (the region of the profile that is resolved, and not of the one AWS_PROFILE names)
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\nregion = ap-south-1\n{BaseKeys}", variables: [(variable, "us-west-2"), ("AWS_PROFILE", "other")]);

        // Act
        await world.ProviderWithoutRegion("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Handler.Requests[0].RequestUri!.ToString()).IsEqualTo(endpoint);
    }

    [Test]
    [Arguments("AWS_REGION", "The AWS_REGION environment variable contains 'Not A Region', which is not a valid AWS region name.")]
    [Arguments("AWS_DEFAULT_REGION", "The AWS_DEFAULT_REGION environment variable contains 'Not A Region', which is not a valid AWS region name.")]
    public async Task GetCredentialsAsync_InvalidRegionInTheEnvironment_FailsNamingTheVariable(string variable, string message)
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\n{BaseKeys}", variables: [(variable, "Not A Region")]);

        // Act
        var act = async () => await world.ProviderWithoutRegion("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_InvalidRegionOfTheProfile_FailsNamingTheProfile()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\nregion = Mars\n{BaseKeys}");

        // Act
        var act = async () => await world.ProviderWithoutRegion("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The region setting of the profile 'dev' contains 'Mars', which is not a valid AWS region name.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_StaticKeysAndAnInvalidRegion_StillReturnsTheKeys()
    {
        // Arrange (the region is looked up only for a profile that calls STS)
        var world = new World($"[profile dev]\n{BaseKeys}region = Mars\n", variables: [("AWS_REGION", "Not A Region")]);

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo("AKIABASE");
    }

    [Test]
    public async Task GetCredentialsAsync_NoRegionAnywhere_FailsAtTheFetch()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\n{BaseKeys}");

        // Act
        var act = async () => await world.ProviderWithoutRegion("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("The STS endpoint depends on the region, and it is not known", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityInTheProfile_FederatesWithTheTokenOfTheFile()
    {
        // Arrange
        var world = new World($"[profile pod]\nrole_arn = {Role}\nweb_identity_token_file = {TokenPath}\nrole_session_name = pod-1\n");

        // Act
        var credentials = await world.Provider("pod", ClientRegion).GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {world.Handler.Requests[0].BodyText}")
            .IsEqualTo("ASIA1 Action=AssumeRoleWithWebIdentity&Version=2011-06-15&RoleArn=arn%3Aaws%3Aiam%3A%3A123456789012%3Arole%2Forders&RoleSessionName=pod-1&WebIdentityToken=the-token");
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityWithoutRole_FailsNamingTheProfile()
    {
        // Arrange
        var world = new World($"[profile pod]\nweb_identity_token_file = {TokenPath}\n");

        // Act
        var act = async () => await world.Provider("pod").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The profile 'pod' sets web_identity_token_file but no role_arn: web identity federation needs the role to assume.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_EmptyTokenFileWithKeys_UsesTheKeysAsBotocoreDoes()
    {
        // Arrange (botocore skips web identity for an empty path, and does not assume the role either, because the key is there)
        var world = new World($"[profile pod]\nrole_arn = {Role}\nweb_identity_token_file =\n{BaseKeys}");

        // Act
        var credentials = await world.Provider("pod").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, world.Handler.Requests.Count)).IsEqualTo(("AKIABASE", 0));
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityAndKeys_FederatesAsBotocoreDoes()
    {
        // Arrange (web identity comes before the static keys of the credentials file)
        var world = new World(
            $"[profile pod]\nrole_arn = {Role}\nweb_identity_token_file = {TokenPath}\n",
            credentials: $"[pod]\n{BaseKeys}");

        // Act
        await world.Provider("pod", ClientRegion).GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Actions()).IsEqualTo("AssumeRoleWithWebIdentity");
    }

    [Test]
    public async Task GetCredentialsAsync_WebIdentityInvalidSessionName_FailsNamingTheRule()
    {
        // Arrange
        var world = new World($"[profile pod]\nrole_arn = {Role}\nweb_identity_token_file = {TokenPath}\nrole_session_name = x\n");

        // Act
        var act = async () => await world.Provider("pod").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("The role_session_name of the profile 'pod' must be", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("[profile dev]\nsso_session = corp\nsso_account_id = 123456789012\nsso_role_name = ReadOnly\n[sso-session corp]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = eu-west-1\n", "SESSION", "eu-west-1")]
    [Arguments("[profile dev]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = ap-southeast-2\nsso_account_id = 123456789012\nsso_role_name = ReadOnly\n", "LEGACY", "ap-southeast-2")]
    public async Task GetCredentialsAsync_SsoProfile_AsksThePortalOfItsRegionWithTheCachedToken(string config, string key, string region)
    {
        // Arrange (the session form of `aws configure sso`, and the older form with the start URL in the profile)
        var world = new World(config, variables: [("USERPROFILE", "/home/dev")]);
        world.AddSsoToken(key == "LEGACY" ? "https://corp.awsapps.com/start" : "corp");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId} {world.Handler.Requests[0].RequestUri!.Host}").IsEqualTo($"{AccessKeyId} portal.sso.{region}.amazonaws.com");
    }

    [Test]
    public async Task GetCredentialsAsync_SsoAndKeys_UsesSsoAsBotocoreDoes()
    {
        // Arrange (SSO comes before the static keys of the credentials file)
        var world = new World(
            "[profile dev]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = eu-west-1\nsso_account_id = 123456789012\nsso_role_name = ReadOnly\n",
            credentials: $"[dev]\n{BaseKeys}",
            variables: [("USERPROFILE", "/home/dev")]);
        world.AddSsoToken("https://corp.awsapps.com/start");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_OnlyAnSsoSession_FallsThroughToTheKeys()
    {
        // Arrange (without the account and the role, botocore's SSOProvider does not apply)
        var world = new World($"[profile dev]\nsso_session = corp\n{BaseKeys}");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo("AKIABASE");
    }

    [Test]
    [Arguments("sso_session = missing\nsso_account_id = 1\nsso_role_name = r\n", "The sso-session 'missing' of the profile 'dev' does not exist in the config file.")]
    [Arguments("sso_session =\nsso_account_id = 1\nsso_role_name = r\n", "The sso-session '' of the profile 'dev' does not exist in the config file.")]
    [Arguments("sso_session = corp\nsso_region = us-east-1\nsso_account_id = 1\nsso_role_name = r\n", "The sso_region of the profile 'dev' is 'us-east-1', and that of its sso-session 'corp' is 'eu-west-1': they must agree.")]
    [Arguments("sso_account_id = 1\n", "The profile 'dev' uses SSO but lacks sso_start_url, sso_region, sso_role_name.")]
    [Arguments("sso_role_name = r\nsso_start_url =\n", "The profile 'dev' uses SSO but lacks sso_start_url, sso_region, sso_account_id.")]
    [Arguments("sso_session = bare\nsso_account_id = 1\nsso_role_name = r\n", "The profile 'dev' uses SSO but lacks sso_start_url, sso_region.")]
    [Arguments("sso_start_url = u\nsso_region = Mars\nsso_account_id = 1\nsso_role_name = r\n", "The sso_region of the profile 'dev' is 'Mars', which is not a valid AWS region name.")]
    public async Task GetCredentialsAsync_SsoConfiguredWrongly_FailsNamingTheSetting(string settings, string message)
    {
        // Arrange (the checks of botocore's _load_sso_config and _resolve_sso_session_reference)
        var world = new World($"[profile dev]\n{settings}[sso-session corp]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = eu-west-1\n[sso-session bare]\n");

        // Act
        var act = async () => await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SsoValueThatAgreesWithTheSession_IsAccepted()
    {
        // Arrange
        var world = new World(
            "[profile dev]\nsso_session = corp\nsso_region = eu-west-1\nsso_account_id = 123456789012\nsso_role_name = ReadOnly\n[sso-session corp]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = eu-west-1\n",
            variables: [("USERPROFILE", "/home/dev")]);
        world.AddSsoToken("corp");

        // Act
        var credentials = await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithSso_AssumesTheRoleWithTheCredentialsOfTheSsoRole()
    {
        // Arrange (a permission set of the user, and a role that it may assume)
        var world = new World(
            $"[profile dev]\nrole_arn = {Role}\nsource_profile = sso\n[profile sso]\nsso_start_url = https://corp.awsapps.com/start\nsso_region = eu-west-1\nsso_account_id = 123456789012\nsso_role_name = ReadOnly\n",
            variables: [("USERPROFILE", "/home/dev")]);
        world.AddSsoToken("https://corp.awsapps.com/start");

        // Act
        await world.Provider("dev").GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(world.Signers()).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task TryInvalidate_AssumedRole_AssumesItAgain()
    {
        // Arrange
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = dev\n{BaseKeys}");
        var provider = world.Provider("dev", ClientRegion);
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, credentials.AccessKeyId)).IsEqualTo((true, "ASIA2"));
    }

    [Test]
    [Arguments("", "[dev]\n" + BaseKeys, "the static keys of the credentials file")]
    [Arguments("[profile dev]\n" + BaseKeys, null, "the static keys of the config file")]
    [Arguments("[profile dev]\nrole_arn = " + Role + "\nweb_identity_token_file = " + TokenPath + "\n", null, "web identity federation into the role " + Role)]
    public async Task GetCredentialsAsync_Source_LogsWhereTheCredentialsComeFrom(string config, string? credentials, string source)
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World(config, credentials, logs: logs);

        // Act
        await world.Provider("dev", ClientRegion).GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single(entry => entry.EventId.Id == 26).Message).IsEqualTo($"The profile 'dev' gets its credentials from {source}.");
    }

    [Test]
    public async Task GetCredentialsAsync_SourceProfileWithARoleAndOnlyAnAccessKey_FailsAsIncomplete()
    {
        // Arrange (botocore counts either key as static keys, which then win over the role of the source profile)
        var world = new World(
            $"[profile dev]\nrole_arn = {Role}\nsource_profile = base\n[profile base]\nrole_arn = {Role}\nsource_profile = other\naws_access_key_id = AKIABASE\n[profile other]\n{BaseKeys}");

        // Act
        var act = async () => await world.Provider("dev", ClientRegion).GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessageContaining("The profile 'base' is incomplete: aws_access_key_id and aws_secret_access_key must both be set and not empty.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_AssumedRole_LogsTheRoleAndTheSource()
    {
        // Arrange
        using var logs = new CapturingLoggerFactory();
        var world = new World($"[profile dev]\nrole_arn = {Role}\nsource_profile = base\n[profile base]\n{BaseKeys}", logs: logs);

        // Act
        await world.Provider("dev", ClientRegion).GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(logs.Entries.Single(entry => entry.EventId.Id == 26).Message)
            .IsEqualTo($"The profile 'dev' gets its credentials from assuming the role {Role} with the credentials of the profile 'base'.");
    }

    /// <summary>The shared files, a token file, an environment and STS, which answers each call with the next key: <c>ASIA1</c>, <c>ASIA2</c>, ...</summary>
    private sealed class World
    {
        private readonly CredentialsDependencies _dependencies;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _files = new(StringComparer.Ordinal);
        private int _issued;

        public World(
            string config,
            string? credentials = null,
            ProcessCredentialsProviderTests.FakeRunner? runner = null,
            CapturingLoggerFactory? logs = null,
            (string Name, string Value)[]? variables = null,
            Task? container = null)
        {
            Handler = new StubHandler(async (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.StartsWith("/v2/credentials/", StringComparison.Ordinal))
                {
                    await (container ?? Task.CompletedTask).ConfigureAwait(false);
                    return new StubResponse { Body = ContainerJson(Time.GetUtcNow().AddHours(1)) };
                }

                if (request.RequestUri!.AbsolutePath == "/federation/credentials")
                {
                    return SsoCredentialsProviderTests.RoleCredentials(Time.GetUtcNow().AddHours(1));
                }

                var issued = Interlocked.Increment(ref _issued);
                return new StubResponse { Body = AssumeRoleCredentialsProviderTests.StsSuccess($"ASIA{issued}", Time.GetUtcNow().AddHours(1)) };
            });
            _files[ConfigPath] = config;
            _files[CredentialsPath] = credentials ?? string.Empty;
            _files[TokenPath] = "the-token\n";
            _dependencies = new CredentialsDependencies(
                Env([("AWS_CONFIG_FILE", ConfigPath), ("AWS_SHARED_CREDENTIALS_FILE", CredentialsPath), .. variables ?? []]),
                path => _files.GetValueOrDefault(path),
                NoWrites,
                runner ?? new ProcessCredentialsProviderTests.FakeRunner(new ProcessResult(ProcessEnd.NotStarted, 0, [])),
                Handler,
                Time,
                (Microsoft.Extensions.Logging.ILoggerFactory?)logs ?? NullLoggerFactory.Instance);
        }

        public ManualTimeProvider Time { get; } = new();

        public StubHandler Handler { get; }

        public ProfileCredentialsProvider Provider(string profile, AwsRegion? region = null) => new(_dependencies, profile, region ?? ClientRegion);

        public ProfileCredentialsProvider ProviderWithoutRegion(string profile) => new(_dependencies, profile, null);

        /// <summary>Caches an SSO token as `aws sso login` does, for a session or a start URL, in the home folder <c>/home/dev</c>.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "The name of the cache file of the AWS CLI.")]
        public void AddSsoToken(string name)
        {
            var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(name)));
            _files[Path.Combine("/home/dev", ".aws", "sso", "cache", key + ".json")] = SsoCredentialsProviderTests.Token(Time.GetUtcNow().AddHours(8));
        }

        /// <summary>Gets the access keys that signed the requests, in order.</summary>
        public string Signers() => string.Join(',', Handler.Requests.Where(request => request.BodyText.StartsWith("Action=AssumeRole&", StringComparison.Ordinal))
            .Select(request => request.Headers.First(header => header.Key == "Authorization").Value.Split('=', '/')[1]));

        /// <summary>Gets the roles of the requests, in order.</summary>
        public string Roles() => string.Join(',', Handler.Requests.Select(request => Uri.UnescapeDataString(request.BodyText.Split('&')[2]["RoleArn=".Length..])));

        /// <summary>Gets the actions of the requests, in order.</summary>
        public string Actions() => string.Join(',', Handler.Requests.Select(request => request.BodyText.Split('&')[0]["Action=".Length..]));
    }
}
