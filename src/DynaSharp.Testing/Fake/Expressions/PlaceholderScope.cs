namespace DynaSharp.Testing.Fake;

/// <summary>
/// The <c>ExpressionAttributeNames</c> and <c>ExpressionAttributeValues</c> of one request, and which of them its expressions used. DynamoDB rejects a placeholder that is not in the
/// maps and an entry of the maps that no expression of the request uses.
/// </summary>
/// <remarks>
/// The parser resolves each placeholder through <see cref="ResolveName"/> and <see cref="ResolveValue"/>, which records it as used. The operation calls <see cref="ThrowIfUnused"/>
/// once, after the last expression of the request, because DynamoDB counts a placeholder used in any of them: <c>:v</c> of a <c>ConditionExpression</c> and <c>#n</c> of an
/// <c>UpdateExpression</c> share the maps.
/// </remarks>
/// <param name="names">The names, or <see langword="null"/> when the request has none.</param>
/// <param name="values">The values, in their stored form, or <see langword="null"/> when the request has none.</param>
internal sealed class PlaceholderScope(IReadOnlyDictionary<string, string>? names, IReadOnlyDictionary<string, AttributeValue>? values)
{
    private const string NamesMember = "ExpressionAttributeNames";
    private const string ValuesMember = "ExpressionAttributeValues";

    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedValues = new(StringComparer.Ordinal);

    /// <summary>Gets the names.</summary>
    public IReadOnlyDictionary<string, string>? Names { get; } = names;

    /// <summary>Gets the values.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? Values { get; } = values;

    /// <summary>Resolves a name placeholder and records it as used.</summary>
    /// <param name="placeholder">The placeholder: <c>#n</c>.</param>
    /// <param name="member">The member of the request the expression is in, which the message names.</param>
    /// <returns>The attribute name it stands for.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: <c>An expression attribute name used in the document path is not defined; attribute name: #n</c>.</exception>
    public string ResolveName(string placeholder, string member)
    {
        if (Names is null || !Names.TryGetValue(placeholder, out var name))
        {
            throw FakeError.Validation($"Invalid {member}: An expression attribute name used in the document path is not defined; attribute name: {placeholder}");
        }

        _usedNames.Add(placeholder);
        return name;
    }

    /// <summary>Resolves a value placeholder and records it as used.</summary>
    /// <param name="placeholder">The placeholder: <c>:v</c>.</param>
    /// <param name="member">The member of the request the expression is in, which the message names.</param>
    /// <returns>The value it stands for.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: <c>An expression attribute value used in expression is not defined; attribute value: :v</c>.</exception>
    public AttributeValue ResolveValue(string placeholder, string member)
    {
        if (Values is null || !Values.TryGetValue(placeholder, out var value))
        {
            throw FakeError.Validation($"Invalid {member}: An expression attribute value used in expression is not defined; attribute value: {placeholder}");
        }

        _usedValues.Add(placeholder);
        return value;
    }

    /// <summary>Throws for a map that DynamoDB rejects as a whole: one that is empty, has a key that is no placeholder, or has an entry that no expression used. Names are checked first.</summary>
    /// <exception cref="FakeError">
    /// A <c>ValidationException</c>: <c>ExpressionAttributeNames must not be empty</c>, <c>ExpressionAttributeNames contains invalid key: Syntax error; key: "a"</c>, or
    /// <c>Value provided in ExpressionAttributeNames unused in expressions: keys: {#a}</c>.
    /// </exception>
    public void ThrowIfUnused()
    {
        ThrowIfInvalid(Names, _usedNames, NamesMember, '#');
        ThrowIfInvalid(Values, _usedValues, ValuesMember, ':');
    }

    private static void ThrowIfInvalid<T>(IReadOnlyDictionary<string, T>? map, HashSet<string> used, string member, char prefix)
    {
        if (map is null)
        {
            return;
        }

        if (map.Count == 0)
        {
            throw FakeError.Validation($"{member} must not be empty");
        }

        foreach (var key in map.Keys)
        {
            if (!IsPlaceholder(key, prefix))
            {
                throw FakeError.Validation($"{member} contains invalid key: Syntax error; key: \"{key}\"");
            }
        }

        var unused = map.Keys.Where(key => !used.Contains(key)).ToList();
        if (unused.Count > 0)
        {
            throw FakeError.Validation($"Value provided in {member} unused in expressions: keys: {{{string.Join(", ", unused)}}}");
        }
    }

    // "#a1_b" and ":v" are placeholders, "a", "#", "#a-b" and ":v.w" are not: the tokenizer would never read them as one.
    private static bool IsPlaceholder(string key, char prefix)
    {
        if (key.Length < 2 || key[0] != prefix)
        {
            return false;
        }

        foreach (var character in key.AsSpan(1))
        {
            if (!ExpressionTokenizer.IsWordCharacter(character))
            {
                return false;
            }
        }

        return true;
    }
}
