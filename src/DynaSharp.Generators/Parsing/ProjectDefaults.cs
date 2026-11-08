using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// What <c>[assembly: DynamoDbDefaults]</c> and <c>[assembly: DynamoDbDefaultConverter]</c> say for the mapped types of an assembly. It lives only inside the transform, and it is read for the assembly that declares a type,
/// so that an analyzer that meets a type from metadata gets the names that type was built with. The transform runs on every compilation change, so an edit of these attributes reaches every model, in any file.
/// </summary>
internal sealed class ProjectDefaults
{
    private const string NamingArgument = "Naming";

    private const string DiscriminatorOptOutArgument = "DiscriminatorOptOut";

    private const string OmitEmptyArgument = "OmitEmpty";

    private readonly Dictionary<ITypeSymbol, ITypeSymbol> _converters = new(SymbolEqualityComparer.Default);

    private readonly DiagnosticBag _bag = new();

    private readonly NamingPolicy _naming;

    private ProjectDefaults(IAssemblySymbol assembly)
    {
        if (AttributeReader.Find(assembly, WellKnownNames.DefaultsAttribute) is { } defaults)
        {
            _naming = (NamingPolicy)AttributeReader.Number(defaults, NamingArgument);
            DiscriminatorOptOut = AttributeReader.Flag(defaults, DiscriminatorOptOutArgument);
            OmitEmpty = AttributeReader.Flag(defaults, OmitEmptyArgument);
            Table = AttributeReader.Named(defaults, WellKnownNames.TableArgument);
            if (Table is not null && ResourceNames.Problem(Table, "table") is { } problem)
            {
                _bag.Report(Descriptors.InvalidTableName, Spot.Argument(defaults, WellKnownNames.TableArgument, default), Table, problem);
            }
        }

        foreach (var attribute in AttributeReader.FindAll(assembly, WellKnownNames.DefaultConverterAttribute))
        {
            Add(attribute);
        }
    }

    /// <summary>Gets a value indicating whether an entity without <c>[Discriminator]</c> opts out.</summary>
    public bool DiscriminatorOptOut { get; }

    /// <summary>Gets a value indicating whether a type without <c>[OmitEmpty]</c> behaves as if it had it.</summary>
    public bool OmitEmpty { get; }

    /// <summary>Gets the table of an entity that declares none, or null.</summary>
    public string? Table { get; }

    public static ProjectDefaults Of(IAssemblySymbol assembly) => new(assembly);

    /// <summary>What is wrong with the attributes, as one value, or nothing for what has no problem, which is how a clean compilation stays out of the diagnostic path.</summary>
    /// <param name="assembly">The assembly of the compilation.</param>
    /// <returns>Nothing, or the problems.</returns>
    public static IEnumerable<EquatableArray<DiagnosticInfo>> Problems(IAssemblySymbol assembly)
    {
        var problems = Of(assembly)._bag.ToEquatableArray();
        return problems.Count == 0 ? [] : [problems];
    }

    /// <summary>The attribute of a property that has no name of its own.</summary>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The name of the attribute.</returns>
    public string AttributeNameOf(string propertyName) => _naming switch
    {
        NamingPolicy.CamelCase => NameCasing.ToCamelCase(propertyName),
        NamingPolicy.SnakeCase => NameCasing.ToSnakeCase(propertyName),
        _ => propertyName,
    };

    /// <summary>The default converter of a type, which a problem of the attribute leaves out: the build fails on the report, and another failure here would only add to it.</summary>
    /// <param name="type">A type without nullability.</param>
    /// <returns>The converter, or null.</returns>
    public ITypeSymbol? ConverterOf(ITypeSymbol type) => _converters.TryGetValue(type, out var converter) ? converter : null;

    private static string? Problem(ITypeSymbol? value)
    {
        if (value is null)
        {
            return "it names no type";
        }

        if (Types.Unwrap(value).IsNullable)
        {
            return "name the type without its ?, which covers the nullable form too";
        }

        return value is INamedTypeSymbol { IsUnboundGenericType: true } ? "a default is for one closed type" : null;
    }

    private void Add(AttributeData attribute)
    {
        // An argument list that does not bind is the error of the compiler.
        if (attribute.ConstructorArguments.Length != 2)
        {
            return;
        }

        var spot = Spot.Of(attribute, default);
        var value = attribute.ConstructorArguments[0].Value as ITypeSymbol;
        if (Problem(value) is { } problem)
        {
            // The code fix drops the ? of a nullable type.
            var fix = value is not null && Types.Unwrap(value).IsNullable ? DefaultConverterCodeFix.UnwrapKey : null;
            _bag.Report(Descriptors.InvalidDefaultConverter, spot, fix, [value?.ToDisplayString() ?? "null", problem]);
            return;
        }

        // A type that does not resolve is the compiler's as well. An open generic comes first, because its missing arguments look like such a type.
        if (Types.HasError(value!))
        {
            return;
        }

        var converter = attribute.ConstructorArguments[1].Value;
        if (!ConverterResolver.IsUsable(converter, value!))
        {
            _bag.Report(Descriptors.InvalidConverter, spot, converter?.ToString() ?? "null", value!.ToDisplayString());
        }
        else if (_converters.ContainsKey(value!))
        {
            _bag.Report(Descriptors.InvalidDefaultConverter, spot, DefaultConverterCodeFix.RemoveKey, [value!.ToDisplayString(), "another default names the same type"]);
        }
        else
        {
            _converters.Add(value!, (ITypeSymbol)converter!);
        }
    }
}
