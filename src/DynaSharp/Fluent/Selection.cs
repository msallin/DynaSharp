namespace DynaSharp.Fluent;

/// <summary>What the commands that offer <c>Select</c> ask of a handle before they use up theirs.</summary>
internal static class Selection
{
    /// <summary>Stops what no read of one attribute can do, at the call.</summary>
    /// <param name="attribute">The path of the handle.</param>
    /// <exception cref="ArgumentException">The handle is a default value, or its path has a list index: DynamoDB returns a shortened list, in which the position of an element is not known.</exception>
    public static void ThrowIfNoAttribute(string attribute)
    {
        ArgumentException.ThrowIfNullOrEmpty(attribute);
        if (attribute.Contains('[', StringComparison.Ordinal))
        {
            throw new ArgumentException($"The attribute path \"{attribute}\" has a list index, and a single list element cannot be selected.", nameof(attribute));
        }
    }
}
