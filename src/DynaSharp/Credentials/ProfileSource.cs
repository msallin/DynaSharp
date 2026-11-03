namespace DynaSharp.Credentials;

/// <summary>Where a profile gets its credentials from, as <see cref="ProfileChain"/> resolved it.</summary>
/// <param name="ProfileName">The profile.</param>
/// <param name="Description">The source, for the log, for example <c>its credential_process</c>. Never contains a secret.</param>
/// <param name="Provider">The provider of the credentials.</param>
internal sealed record ProfileSource(string ProfileName, string Description, ICredentialsProvider Provider);
