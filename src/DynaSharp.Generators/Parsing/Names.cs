using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.Generators;

/// <summary>How symbols become code, and how names are derived.</summary>
internal static class Names
{
    private static readonly SymbolDisplayFormat CodeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // The class that holds the constants (CS0542), and what it inherits from object (CS0108). An index name starts with a capital here, so a keyword cannot come up.
    private static readonly HashSet<string> ReservedConstants =
    [
        "Indexes", nameof(Equals), "Finalize", nameof(GetHashCode), nameof(GetType), nameof(MemberwiseClone), nameof(ReferenceEquals), nameof(ToString),
    ];

    /// <summary>A type as code, with its nullability: <c>global::Shop.Address?</c>, <c>decimal</c>.</summary>
    public static string Of(ITypeSymbol type) => type.ToDisplayString(CodeFormat);

    /// <summary>A name as code, which needs an <c>@</c> when it is a keyword.</summary>
    public static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    /// <summary>The builder parameter of a property: <c>CreatedAt</c> gives <c>createdAt</c>, <c>Class</c> gives <c>@class</c>.</summary>
    public static string Parameter(string property) => Identifier(char.ToLowerInvariant(property[0]) + property.Substring(1));

    /// <summary>A parameter beside others that are named after properties: <c>key</c> gives <c>key_</c> when a placeholder <c>Key</c> took the name.</summary>
    public static string Free(string name, IEnumerable<string> taken) => taken.Contains(name) ? Free(name + "_", taken) : name;

    /// <summary>
    /// The builder prefix of an index, a C# identifier however the index is named.
    /// <code>
    /// GSI1           Gsi1
    /// StatusIndex    StatusIndex
    /// orders-by_date OrdersByDate
    /// 2024           Index2024
    /// ---            Index
    /// </code>
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns>The prefix.</returns>
    public static string Builder(string indexName)
    {
        var builder = new StringBuilder();
        foreach (var word in indexName.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word.Substring(1);
            builder.Append(char.ToUpperInvariant(word[0])).Append(IsAllCaps(word) ? Lower(rest) : rest);
        }

        return builder.Length == 0 || char.IsDigit(builder[0]) ? "Index" + builder.ToString() : builder.ToString();
    }

    /// <summary>
    /// The constant of an index in <c>Indexes</c>: the builder prefix, unless a class cannot declare that name without a warning.
    /// <code>
    /// by-status ByStatus
    /// indexes   IndexIndexes
    /// to-string IndexToString
    /// </code>
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns>The name of the constant.</returns>
    public static string IndexConstant(string indexName)
    {
        var name = Builder(indexName);
        return ReservedConstants.Contains(name) ? "Index" + name : name;
    }

    /// <summary>The default attribute of an index key: index <c>GSI1</c> and suffix <c>pk</c> give <c>gsi1pk</c>.</summary>
    public static string IndexAttribute(string indexName, string suffix) => Lower(indexName) + suffix;

    private static bool IsAllCaps(string word) => !word.Any(char.IsLower);

    private static string Lower(string text) => new([.. text.Select(char.ToLowerInvariant)]);
}
