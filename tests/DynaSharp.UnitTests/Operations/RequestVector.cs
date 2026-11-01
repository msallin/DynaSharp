namespace DynaSharp.UnitTests.Operations;

/// <summary>A request that botocore serialized: the JSON body and target it produced for some parameters.</summary>
/// <param name="Operation">The operation, for example <c>Query</c>.</param>
/// <param name="Name">The name of the vector, unique within the operation.</param>
/// <param name="Target">The <c>X-Amz-Target</c> header value botocore sent.</param>
/// <param name="Body">The exact JSON body botocore wrote.</param>
internal sealed record RequestVector(string Operation, string Name, string Target, string Body);
