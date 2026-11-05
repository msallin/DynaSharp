using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.Generators;

/// <summary>
/// Turns an attributed type into its value model and diagnostics. It runs for every matching type on every compilation change, so it
/// stays cheap: it walks the type and its bases and nothing else. Nothing it returns may refer to a symbol or a syntax tree.
/// </summary>
internal sealed class ModelBuilder
{
    private const string DefaultDiscriminatorAttribute = "_type";

    private const string Serialization = "global::DynaSharp.Serialization.";

    private static readonly string[] KeyAttributesOnType =
    [
        WellKnownNames.PartitionKeyAttribute, WellKnownNames.SortKeyAttribute, WellKnownNames.GlobalIndexKeyAttribute,
        WellKnownNames.LocalIndexKeyAttribute, WellKnownNames.DiscriminatorAttribute,
    ];

    private readonly INamedTypeSymbol _type;

    private readonly MappedKind _kind;

    private readonly Compilation _compilation;

    private readonly Spot _spot;

    private readonly DiagnosticBag _bag = new();

    private readonly EnumRegistry _enums = new();

    private readonly ProjectDefaults _defaults;

    private ModelBuilder(INamedTypeSymbol type, MappedKind kind, Compilation compilation, Spot spot)
    {
        _type = type;
        _defaults = ProjectDefaults.Of(type.ContainingAssembly);
        _kind = kind;
        _compilation = compilation;
        _spot = spot;
    }

    public static BuildResult Build(GeneratorAttributeSyntaxContext context, MappedKind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var type = (INamedTypeSymbol)context.TargetSymbol;

        // The attributed part, which a partial type need not list first.
        var spot = Spot.Of(((TypeDeclarationSyntax)context.TargetNode).Identifier.GetLocation());
        var builder = new ModelBuilder(type, kind, context.SemanticModel.Compilation, spot);
        var model = TypeRules.Check(context, type, kind, builder._bag, builder._spot) ? builder.Analyze() : null;
        return new BuildResult(builder._bag.HasErrors ? null : model, builder._bag.ToEquatableArray());
    }

    /// <summary>The model of a type that an analyzer met, which may come from metadata. Its diagnostics are the generator's to report.</summary>
    /// <param name="type">The type.</param>
    /// <param name="kind">What the type is mapped as.</param>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The model, or null when an error makes the type unusable.</returns>
    public static TypeModel? ModelOf(INamedTypeSymbol type, MappedKind kind, Compilation compilation) =>
        new ModelBuilder(type, kind, compilation, new Spot(string.Empty, default)).Analyze();

    private static string Keyword(INamedTypeSymbol type)
    {
        if (type.IsRecord)
        {
            return type.IsValueType ? "record struct" : "record";
        }

        if (type.TypeKind == TypeKind.Interface)
        {
            return "interface";
        }

        return type.IsValueType ? "struct" : "class";
    }

    private static bool HasRequiredMember(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(static member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return true;
            }
        }

        return false;
    }

    private static MissingBehavior MissingOf(PropertyFacts property)
    {
        // The item is one to adopt, which a read that threw could not reach, also for a required version.
        if (property.AllowsMissingVersion)
        {
            return MissingBehavior.KeepCurrent;
        }

        if (Types.Unwrap(property.Type).IsNullable)
        {
            // Null is left out on write, so a read that throws here would break the round trip of a `required string?`.
            return MissingBehavior.KeepCurrent;
        }

        if (property.Resolved?.Model.EmptySet is not null)
        {
            return property.Parameter is null && StartsEmpty(property.Symbol) ? MissingBehavior.KeepCurrent : MissingBehavior.EmptySet;
        }

        return property.Symbol.IsRequired || property.Parameter is { HasExplicitDefaultValue: false } ? MissingBehavior.Throw : MissingBehavior.KeepCurrent;
    }

    // A set the initializer leaves empty is already what a missing attribute means, so a read need not throw it away for another. A filled
    // one must not survive: an empty set is not stored, so the read would give back what was never written. A property of a base type in another
    // assembly shows no syntax and gets the empty set.
    private static bool StartsEmpty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(static declaration => IsEmptyCollection(declaration.Initializer?.Value));

    private static bool IsEmptyCollection(ExpressionSyntax? value) => value switch
    {
        CollectionExpressionSyntax collection => collection.Elements.Count == 0,
        BaseObjectCreationExpressionSyntax creation => creation.ArgumentList is not { Arguments.Count: > 0 } && creation.Initializer is not { Expressions.Count: > 0 },
        _ => false,
    };

    // What holds a Length goes by that, and everything else that [OmitEmpty] applies to has a Count.
    private static WriteCondition EmptinessTest(ITypeSymbol type) =>
        Types.Unwrap(type).Inner is IArrayTypeSymbol or { SpecialType: SpecialType.System_String } ? WriteCondition.WhenNotEmptyLength : WriteCondition.WhenNotEmptyCount;

    // The converter of a handle up to its last type argument, the converter of the property: a string has a wrapper of its own, and a collection has to say what it enumerates.
    private static string? OmitEmptyConverter(PropertyFacts property)
    {
        if (!property.OmitsEmpty)
        {
            return null;
        }

        var inner = Types.Unwrap(property.Type).Inner;
        if (inner.SpecialType == SpecialType.System_String)
        {
            return Serialization + "OmitEmptyConverter<";
        }

        var wrapper = property.Resolved!.IsAddable ? "OmitEmptySetConverter" : "OmitEmptyCollectionConverter";
        return $"{Serialization}{wrapper}<{Names.Of(inner)}, {ConverterResolver.ElementOf(inner)}, ";
    }

    private static PropertyModel ToModel(PropertyFacts property, string setFlag)
    {
        var write = property.OmitsEmpty ? EmptinessTest(property.Type) : property.Resolved?.Write ?? WriteCondition.Never;
        return new PropertyModel(
            property.Name,
            Names.Identifier(property.Name),
            Names.Of(property.Type),
            property.AttributeName,
            Encoding.UTF8.GetByteCount(property.AttributeName),
            property.Resolved?.Model,
            write,
            MissingOf(property),
            property.Parameter is not null,
            property.IsComputed,
            property.RestoredFromKey,
            property.FallbackKey,
            property.ParameterName,
            property.Symbol.DeclaredAccessibility == Accessibility.Public && Types.Effective(property.Type) == Accessibility.Public && property.Resolved?.IsPublic == true,
            OmitEmptyConverter(property),
            property.AllowsMissingVersion,
            setFlag);
    }

    // Only where the throw is certain. A required input of a key that holds it without loss falls back on that key first, which may hold a name the enum knows.
    private static string? WhyAnUnknownNameThrows(PropertyFacts property, EntityModel? entity)
    {
        if (entity is not null && entity.Keys().Any(key => key.PlainProperty == property.Name))
        {
            return $"the property '{property.Name}' is a key, which a read takes as it is stored, so an unknown name throws there. A key that has to hold names the enum does not know is a string.";
        }

        if (MissingOf(property) != MissingBehavior.Throw || property.FallbackKey is not null)
        {
            return null;
        }

        return property.Symbol.IsRequired
            ? $"the property '{property.Name}' is required, so an unknown name throws there, as a missing attribute does. Make the property nullable, or not required with an initial value that is safe to keep."
            : $"the property '{property.Name}' is set by a constructor parameter without a default value, so an unknown name throws there, as a missing attribute does. Make the property nullable, or give the parameter a default value that is safe to keep.";
    }

    // The flags sit beside the values in __Fields, so a flag must not take the name of a property, as that of X would for a property X__set.
    private static Dictionary<string, string> SetFlags(List<PropertyFacts> mapped)
    {
        var taken = new HashSet<string>(mapped.Select(static property => property.Name));
        var flags = new Dictionary<string, string>();
        foreach (var property in mapped)
        {
            var flag = property.Name + "__set";
            while (!taken.Add(flag))
            {
                flag += "_";
            }

            flags.Add(property.Name, flag);
        }

        return flags;
    }

    private TypeModel? Analyze()
    {
        var properties = PropertyCollector.Collect(_type, _compilation, _spot);
        var constructor = ConstructorBinder.Bind(_type, properties, _bag, _spot);
        if (constructor is null)
        {
            return null;
        }

        ReportUnreachableSetters(properties);
        ReportNullsThatReadBackAsInitialValues(properties);
        var discriminator = _kind == MappedKind.Entity ? ReadDiscriminator() : null;
        if (_kind == MappedKind.Document)
        {
            RejectEntityAttributes(properties);
        }

        var reserved = discriminator?.Value is null ? null : discriminator.AttributeName;
        var claims = new Dictionary<string, AttributeClaim>();
        var resolver = new ConverterResolver(_enums, _bag, _defaults, _kind == MappedKind.Entity);
        foreach (var property in properties)
        {
            MapProperty(property, reserved, claims, resolver);
        }

        ReadOmitEmpty(properties);
        var entity = discriminator is null ? null : new EntityKeyBuilder(_type, _spot, properties, claims, reserved, _enums, _bag).Build(discriminator);
        KeepWhatAReadNeeds(properties);
        ReportUnknownNamesThatThrow(properties, entity);
        return _bag.HasErrors ? null : Compose(properties, constructor, entity);
    }

    // DYNA034. A base type stores a property with a private setter, which the generated code of a derived type cannot assign, so the derived type would drop it without a word.
    private void ReportUnreachableSetters(List<PropertyFacts> properties)
    {
        foreach (var property in properties.Where(property => property.Symbol.SetMethod is not null && property.IsComputed && !property.IsStored && !property.IsIgnored && property.KeyOnly is null))
        {
            _bag.Report(Descriptors.UnreachableSetter, _spot, property.Name, property.Symbol.ContainingType.Name, _type.Name);
        }
    }

    // DYNA035. Null is left out on write, and a read of an item without the attribute keeps the initial value, so a nullable property that starts with a value cannot read back null.
    private void ReportNullsThatReadBackAsInitialValues(List<PropertyFacts> properties)
    {
        foreach (var property in properties)
        {
            if (NullableDefault.Of(property, _type, _compilation, _spot) is var (kept, fix, spot))
            {
                _bag.Report(Descriptors.NullReadsBackAsInitialValue, spot, property.Name, kept, fix);
            }
        }
    }

    private DiscriminatorInfo ReadDiscriminator()
    {
        if (AttributeReader.Find(_type, WellKnownNames.DiscriminatorAttribute) is not { } attribute)
        {
            // The type is generated as if it opted out, so the build fails on this error and not on every use of the type as well.
            if (!_defaults.DiscriminatorOptOut)
            {
                _bag.ReportKeepingTheModel(Descriptors.NoDiscriminator, _spot, _type.Name, NameCasing.ToSnakeCase(_type.Name).ToUpperInvariant());
            }

            return new DiscriminatorInfo(null, DefaultDiscriminatorAttribute);
        }

        var spot = Spot.Of(attribute, _spot);
        var value = AttributeReader.Text(attribute, 0);
        var optOut = AttributeReader.Flag(attribute, "OptOut");
        if (value is null == !optOut || value?.Length == 0)
        {
            _bag.Report(Descriptors.InvalidDiscriminator, spot, _type.Name, value?.Length == 0 ? "the value is empty" : "set either a value or OptOut");
        }

        var name = AttributeReader.Named(attribute, WellKnownNames.AttributeNameArgument) ?? DefaultDiscriminatorAttribute;
        if (AttributeNames.Problem(name, null, AttributeNames.MaxLength) is { } problem)
        {
            _bag.Report(Descriptors.InvalidAttributeName, Spot.Argument(attribute, WellKnownNames.AttributeNameArgument, _spot), name, problem);
        }

        return new DiscriminatorInfo(value, name);
    }

    private void RejectEntityAttributes(List<PropertyFacts> properties)
    {
        foreach (var attribute in _type.GetAttributes().Where(static attribute => KeyAttributesOnType.Any(name => AttributeReader.IsNamed(attribute.AttributeClass, name))))
        {
            RejectOnDocument(attribute);
        }

        foreach (var property in properties)
        {
            foreach (var attribute in new[] { property.PartitionKey, property.SortKey, property.KeyOnly, property.Version, property.TimeToLive, property.ComputedFrom }.OfType<AttributeData>())
            {
                RejectOnDocument(attribute);
            }
        }
    }

    private void RejectOnDocument(AttributeData attribute)
    {
        var name = attribute.AttributeClass!.Name;
        _bag.Report(Descriptors.NotValidOnDocument, Spot.Of(attribute, _spot), name.Substring(0, name.Length - nameof(Attribute).Length));
    }

    // What feeds a key is not known yet, so the keys of an entity take that back in EntityKeyBuilder. A property says more than its type, and a type more than the assembly.
    private void ReadOmitEmpty(List<PropertyFacts> properties)
    {
        var onType = AttributeReader.Find(_type, WellKnownNames.OmitEmptyAttribute) is { } typeAttribute ? AttributeReader.IsEnabled(typeAttribute) : _defaults.OmitEmpty;
        foreach (var property in properties)
        {
            var isAsked = property.OmitEmpty is { } attribute ? AttributeReader.IsEnabled(attribute) : onType;
            if (property.OmitEmpty is not null && isAsked && !property.CanBeEmpty)
            {
                _bag.Report(Descriptors.InvalidOmitEmpty, Spot.Of(property.OmitEmpty, property.Spot), property.Name, "it is no string, list, set, dictionary or array");
            }

            property.OmitsEmpty = property.IsStored && property.CanBeEmpty && isAsked;
        }
    }

    // An empty value that is not written could not be read back where a read throws without one. On a type that goes the way of a key input, and on the property it is a mistake.
    private void KeepWhatAReadNeeds(List<PropertyFacts> properties)
    {
        foreach (var property in properties.Where(static property => property.OmitsEmpty && MissingOf(property) == MissingBehavior.Throw))
        {
            property.OmitsEmpty = false;
            if (property.OmitEmpty is { } attribute)
            {
                _bag.Report(Descriptors.InvalidOmitEmpty, Spot.Of(attribute, property.Spot), property.Name, "a read throws without it, so an empty value could not be read back");
            }
        }
    }

    // DYNA033 on a property. The attribute on the enum makes a name that is no member read as a missing attribute, which changes nothing where a missing attribute throws as well, or where a key
    // is read as it is stored. The enum itself is fine, so this is reported for each property, beside what EnumAttributeAnalyzer reports once on the attribute.
    private void ReportUnknownNamesThatThrow(List<PropertyFacts> properties, EntityModel? entity)
    {
        foreach (var property in properties.Where(static property => property.Resolved is not null && ConverterResolver.CountsUnknownNameAsMissing(property)))
        {
            if (WhyAnUnknownNameThrows(property, entity) is { } reason)
            {
                _bag.Report(Descriptors.IneffectiveEnumAttribute, property.Spot, Types.Unwrap(property.Type).Inner.Name, reason);
            }
        }
    }

    private void MapProperty(PropertyFacts property, string? reserved, Dictionary<string, AttributeClaim> claims, ConverterResolver resolver)
    {
        if (!property.IsStored && property.KeyOnly is null)
        {
            return;
        }

        if (Types.HasError(property.Type))
        {
            _bag.MarkBroken();
            return;
        }

        if (!Types.IsAccessible(property.Type))
        {
            _bag.Report(Descriptors.InaccessibleType, property.Spot, property.Type.ToDisplayString(), property.Name);
            return;
        }

        if (property.IsStored)
        {
            ResolveAttributeName(property, reserved, claims);
            property.Resolved = resolver.Resolve(property);
        }
    }

    private void ResolveAttributeName(PropertyFacts property, string? reserved, Dictionary<string, AttributeClaim> claims)
    {
        var keyAttribute = property.PartitionKey ?? property.SortKey;
        var keyName = keyAttribute is null ? null : AttributeReader.Named(keyAttribute, WellKnownNames.AttributeNameArgument);
        var attributeName = property.AttributeAttribute is null ? null : AttributeReader.Text(property.AttributeAttribute, 0);
        var spot = property.AttributeAttribute is null ? property.Spot : Spot.Argument(property.AttributeAttribute, 0, property.Spot);
        if (attributeName is not null && keyName is not null && attributeName != keyName)
        {
            _bag.Report(Descriptors.InvalidAttributeName, spot, keyName, "it differs from [DynamoDbAttribute] on the same property");
        }

        var name = attributeName ?? keyName ?? _defaults.AttributeNameOf(property.Name);
        var limit = keyAttribute is null ? AttributeNames.MaxLength : AttributeNames.MaxKeyLength;
        if (AttributeNames.Problem(name, reserved, limit) is { } problem)
        {
            _bag.Report(Descriptors.InvalidAttributeName, spot, name, problem);
        }

        property.AttributeName = name;
        if (claims.TryGetValue(name, out var owner))
        {
            _bag.Report(Descriptors.DuplicateAttributeName, property.Spot, name, owner.Label, property.Name);
        }
        else
        {
            claims.Add(name, new AttributeClaim(property.Name, null));
        }
    }

    private TypeModel Compose(List<PropertyFacts> properties, IMethodSymbol constructor, EntityModel? entity)
    {
        var mapped = properties.Where(static property => property.IsStored || property.KeyOnly is not null).ToList();
        var parameters = constructor.Parameters
            .Select(parameter => new ConstructorParameter(mapped.Find(property => SymbolEqualityComparer.Default.Equals(property.Parameter, parameter))!.Name, DefaultLiterals.Of(parameter)));

        // A plain `new` would have to set `required` members, however they are mapped; the generated constructor declares it does.
        var needsFields = mapped.Exists(static property => property.Parameter is null && !property.IsComputed) || HasRequiredMember(_type);
        var declaresDefault = needsFields && !_type.IsValueType && constructor.IsImplicitlyDeclared;

        var setFlags = SetFlags(mapped);
        var outer = new Stack<ContainingType>();
        var hintName = new Stack<string>([_type.Name]);
        for (var current = _type.ContainingType; current is not null; current = current.ContainingType)
        {
            outer.Push(new ContainingType(Keyword(current), Names.Identifier(current.Name)));
            hintName.Push(current.Name);
        }

        var @namespace = _type.ContainingNamespace.IsGlobalNamespace ? null : _type.ContainingNamespace.ToDisplayString();
        if (@namespace is not null)
        {
            // A hint name may not hold the @ of a namespace that is a keyword.
            hintName.Push(@namespace.Replace("@", string.Empty));
        }

        return new TypeModel(
            @namespace,
            _type.Name,
            Names.Identifier(_type.Name),
            Names.Of(_type),
            Keyword(_type),
            string.Join(".", hintName) + ".g.cs",
            new EquatableArray<ContainingType>([.. outer]),
            new ConstructorModel(new EquatableArray<ConstructorParameter>([.. parameters]), declaresDefault, needsFields),
            new EquatableArray<PropertyModel>([.. mapped.Select(property => ToModel(property, setFlags[property.Name]))]),
            entity,
            _enums.ToEquatableArray(),
            _kind == MappedKind.Entity && TypeRules.HidesKeys(_type),
            TypeRules.HidesAttrs(_type),
            _kind == MappedKind.Entity && TypeRules.HidesIndexes(_type));
    }
}
