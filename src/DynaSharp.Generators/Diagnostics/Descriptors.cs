using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>
/// Every diagnostic of the generator. When adding one: declare it here, add it to <see cref="All"/>, list it in
/// <c>AnalyzerReleases.Unshipped.md</c>, and test it positive and negative.
/// </summary>
internal static class Descriptors
{
    public static readonly DiagnosticDescriptor NotPartial = new(
        "DYNA001", "Type is not partial", "Type '{0}' must be partial, as must its containing types", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NoPartitionKey = new(
        "DYNA002",
        "Entity has no partition key",
        "Entity '{0}' has no partition key. Add [PartitionKey], or use [DynamoDbDocument] for a nested type.",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "DYNA003", "Unsupported property type", "Property '{0}' has unsupported type '{1}'. {2}.", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor DuplicateAttributeName = new(
        "DYNA004", "Attribute name is used twice", "Attribute name '{0}' is used by both '{1}' and '{2}'", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NoUsableConstructor = new(
        "DYNA005",
        "No usable constructor",
        "Type '{0}' has no single usable constructor. Mark one with [DynamoDbConstructor].",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor UnboundParameter = new(
        "DYNA006", "Constructor parameter has no property", "Constructor parameter '{0}' has no matching mapped property", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor UnmappableType = new(
        "DYNA007", "Type cannot be mapped", "Type '{0}' cannot be mapped: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidConverter = new(
        "DYNA008",
        "Converter is not usable",
        "Converter '{0}' does not implement IDynamoDbConverter<{1}> or has no accessible definition",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor DuplicateKey = new(
        "DYNA009", "Key is declared twice", "Key '{0}' is declared more than once", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidAttributeName = new(
        "DYNA010", "Attribute name is invalid", "Attribute name '{0}' is invalid: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidVersion = new(
        "DYNA011", "[Version] is not valid here", "[Version] requires a single integer property without a converter; '{0}' is not valid", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NotValidOnDocument = new(
        "DYNA012", "Attribute is not valid on a document", "[{0}] is not valid on a document", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor EntityAndDocument = new(
        "DYNA013", "Both entity and document", "Type '{0}' cannot be both an entity and a document", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor MemberConflict = new(
        "DYNA014", "Generated member conflicts", "Generated member '{0}' conflicts with a member of '{1}'", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor LanguageVersionTooLow = new(
        "DYNA015", "C# version is too low", "DynaSharp generated code needs C# 11 or later; this project uses {0}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InaccessibleType = new(
        "DYNA016", "Type is not accessible", "Type '{0}' used by '{1}' is not accessible to generated code", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidKeyType = new(
        "DYNA017",
        "Property cannot be a key",
        "Property '{0}' of type '{1}' cannot be a DynamoDB key, which must be a string, number or binary",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidIndex = new(
        "DYNA018", "Index is invalid", "Index '{0}' is invalid: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NullableContextDisabled = new(
        "DYNA019",
        "Type is outside a nullable context",
        "Type '{0}' must be in an enabled nullable context, otherwise nullable properties cannot be told from others",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NoDiscriminator = new(
        "DYNA020",
        "Entity has no discriminator",
        "Entity '{0}' has no discriminator. Add [Discriminator(\"{1}\")], or [Discriminator(OptOut = true)] for a table written by other tools.",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor IgnoredPlainKey = new(
        "DYNA021", "Plain key is ignored", "Property '{0}' is a plain key, so it cannot be [DynamoDbIgnore]", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidDiscriminator = new(
        "DYNA022", "Discriminator is invalid", "[Discriminator] on '{0}' is invalid: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor VersionInKey = new(
        "DYNA023",
        "[Version] property is part of a key",
        "[Version] property '{0}' is part of a key. A write changes the version, and a key cannot follow it.",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor EnumWithoutMembers = new(
        "DYNA024",
        "Enum has no members",
        "Property '{0}' has enum type '{1}', which has no members, so every write throws. Add a member, make the property nullable, or add a [DynamoDbConverter].",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NamedLikeGeneratedClass = new(
        "DYNA025",
        "Type has the name of a generated class",
        "Type '{0}' has the name of a class that is generated into it, which C# does not allow. Rename the type.",
        Mapping,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor DuplicateDiscriminator = new(
        "DYNA026",
        "Two entities share a discriminator",
        "Entity '{0}' has the discriminator '{1}' in the attribute '{2}', as '{3}' has, so on one table their items cannot be told apart. Give one of them another value, unless they never share a table.",
        Mapping,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: EntityHelp,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor InvalidOmitEmpty = new(
        "DYNA030", "[OmitEmpty] is not valid here", "[OmitEmpty] on '{0}' is not valid: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor TemplateSyntax = new(
        "DYNA100", "Key template syntax error", "Key template '{0}' has a syntax error at position {1}: {2}", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor LiteralNotAscii = new(
        "DYNA101", "Key template literal is not ASCII", "Key template literal must be printable ASCII", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor UnknownProperty = new(
        "DYNA102", "Unknown property in a key template", "Key template references unknown property '{0}'", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor UnsupportedKeyInput = new(
        "DYNA103", "Type cannot be used in a key", "Property '{0}' of type '{1}' cannot be used in a key", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor FormatDoesNotFit = new(
        "DYNA104", "Format does not fit the type", "Format '{0}' does not fit type '{1}'. It takes {2}.", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor FormatOutOfRange = new(
        "DYNA105", "Format is out of range", "Format '{0}' is out of range", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor NoLiteralBetween = new(
        "DYNA106",
        "Placeholders cannot be told apart",
        "'{0}' is variable-width and '{1}' follows with no literal between them, so the key cannot be parsed",
        KeyGrammar,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor NullableInput = new(
        "DYNA107", "Nullable input in a key", "Nullable '{0}' is used in a key that is not Sparse", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor NumberWithoutFixedFormat = new(
        "DYNA108",
        "Number without a fixed format in a sort key",
        "Number '{0}' has no fixed format in a sort key, so 10 sorts before 9. Use d<w> or n<i>, or {{{0}:plain}}.",
        KeyGrammar,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor InvalidKeyOnly = new(
        "DYNA109", "[KeyOnly] is not valid here", "[KeyOnly] on '{0}' is invalid: {1}", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor SparseWithoutNullableInput = new(
        "DYNA110",
        "Sparse index without nullable inputs",
        "Index '{0}' is Sparse, but all its inputs are non-nullable, so every item is indexed",
        KeyGrammar,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor RepeatedPlaceholder = new(
        "DYNA111", "Property is repeated in a key template", "Property '{0}' appears twice in a key template", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor MisplacedKey = new(
        "DYNA112", "Key on the wrong target", "A template belongs on the type, a plain key on a property", KeyGrammar, DiagnosticSeverity.Error, true, helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor EnumNamesNotUnique = new(
        "DYNA113",
        "Enum names are not one-to-one",
        "Enum '{0}' has [Flags] or aliased members, so its names are not one-to-one in a key. Use d<w>.",
        KeyGrammar,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor ComputedInIndex = new(
        "DYNA114",
        "Computed property in an index template",
        "Computed property '{0}' cannot be used in an index template, because an update cannot keep the index current",
        KeyGrammar,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor StringBeforeDelimiter = new(
        "DYNA115",
        "String before a delimiter in a sort key",
        "String '{0}' is followed by '{1}' in a sort key, so values with a character below '{1}' sort out of segment order",
        KeyGrammar,
        DiagnosticSeverity.Info,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor TruncatedTimestampEndsTableKey = new(
        "DYNA116",
        "Truncated timestamp at the end of a table key",
        "Timestamp '{0}' ends the key of the table and is truncated, so two items close in time get one key and the second replaces the first. Add a placeholder after it that breaks the tie, or use {{{0}:ticks}}.",
        KeyGrammar,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor EmptyDefaultOfKey = new(
        "DYNA117",
        "Key property starts empty",
        "Key property '{0}' starts empty and nothing has to set it, so a write that forgets it fails at run time. Make it required, or set it in a constructor.",
        KeyGrammar,
        DiagnosticSeverity.Info,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor CommandOutlivesExpression = new(
        "DYNA201",
        "Command outlives its expression",
        "A DynaSharp command is built and sent in one expression. Compute values first, or move the chain into a method.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor ReadCommandNotAwaitable = new(
        "DYNA202",
        "Read command is not awaited",
        "Query and Scan are not awaited; end the chain with ItemsAsync, ItemAsync, PageAsync, PagesAsync or CountAsync",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor CommandDiscarded = new(
        "DYNA203",
        "Command is discarded",
        "This DynaSharp command is discarded, so it is never sent, and the handle it came from is used up. Send it, or use what the call returns.",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor CommandReused = new(
        "DYNA204",
        "Command is used twice",
        "This command was used by an earlier call, so this one throws at run time. Assign what each call returns to the variable, or build the command again.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor WriteOfTableKeyInput = new(
        "DYNA205",
        "Write to a key of the table",
        "'{0}' is part of the key of the table of '{1}', and DynamoDB cannot change a key. Delete the item and put a new one.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor IfNotExistsOnVersionedPut = new(
        "DYNA206",
        "IfNotExists on a Put with a version",
        "This Put of '{0}' holds a version above 0, and an item that does not exist has no version, so IfNotExists can never hold and the command throws. For a first write leave the version at 0, or call IgnoreVersion.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor UndeclaredIndex = new(
        "DYNA207",
        "Index is not declared",
        "'{0}' declares no index '{1}'. Take the name from {0}.Indexes.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor StringInNamePosition = new(
        "DYNA210",
        "String where a name belongs",
        "String '{0}' is a value here, but this position needs a name. Wrap it in Attr(...).",
        Expressions,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor PlaceholderInExpression = new(
        "DYNA211",
        "Placeholder in an expression",
        "DynaSharp generates placeholders; write the name in Attr(...) and the value in a hole, not '{0}'",
        Expressions,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor ExpressionSyntaxError = new(
        "DYNA212", "Expression syntax error", "The expression is not valid: {0}", Expressions, DiagnosticSeverity.Warning, true, helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor PathMatchesNoAttribute = new(
        "DYNA213",
        "Path matches no attribute",
        "Path '{0}' matches no attribute of '{1}'. Check the spelling, unless the attribute is outside the entity.",
        Expressions,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor InvalidPath = new(
        "DYNA214",
        "Attribute path is not valid",
        "Attribute path '{0}' is not valid: it must be names joined by dots, with list indexes in brackets, such as a.b[2].c",
        Expressions,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor HandleOfAnotherType = new(
        "DYNA215",
        "Handle of another type",
        "'{0}' is an attribute of '{1}', and this command is on '{2}', so DynamoDB looks for it on items that do not have it. Use a handle of that type, or a raw path for an attribute outside it.",
        Expressions,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor TextFunctionOnNumber = new(
        "DYNA216",
        "Text function on a number",
        "{0} never matches a number or a boolean, so this condition holds for no item. Compare the attribute, or give it a type that stores text.",
        Expressions,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly IReadOnlyList<DiagnosticDescriptor> All =
    [
        NotPartial, NoPartitionKey, UnsupportedType, DuplicateAttributeName, NoUsableConstructor, UnboundParameter, UnmappableType,
        InvalidConverter, DuplicateKey, InvalidAttributeName, InvalidVersion, NotValidOnDocument, EntityAndDocument, MemberConflict,
        LanguageVersionTooLow, InaccessibleType, InvalidKeyType, InvalidIndex, NullableContextDisabled, NoDiscriminator, IgnoredPlainKey,
        InvalidDiscriminator, VersionInKey, EnumWithoutMembers, NamedLikeGeneratedClass, DuplicateDiscriminator, InvalidOmitEmpty, TemplateSyntax, LiteralNotAscii, UnknownProperty, UnsupportedKeyInput, FormatDoesNotFit, FormatOutOfRange,
        NoLiteralBetween, NullableInput, NumberWithoutFixedFormat, InvalidKeyOnly, SparseWithoutNullableInput, RepeatedPlaceholder,
        MisplacedKey, EnumNamesNotUnique, ComputedInIndex, StringBeforeDelimiter, TruncatedTimestampEndsTableKey, EmptyDefaultOfKey,
        CommandOutlivesExpression, ReadCommandNotAwaitable, CommandDiscarded, CommandReused, WriteOfTableKeyInput, IfNotExistsOnVersionedPut, UndeclaredIndex, StringInNamePosition, PlaceholderInExpression, ExpressionSyntaxError, PathMatchesNoAttribute, InvalidPath, HandleOfAnotherType, TextFunctionOnNumber,
    ];

    public static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> ById = All.ToDictionary(descriptor => descriptor.Id);

    private const string Mapping = "DynaSharp.Mapping";

    private const string KeyGrammar = "DynaSharp.Keys";

    private const string Commands = "DynaSharp.Commands";

    private const string Expressions = "DynaSharp.Expressions";

    private const string EntityHelp = "https://github.com/msallin/DynaSharp/blob/main/spec/diagnostics.md#entities";

    private const string KeyHelp = "https://github.com/msallin/DynaSharp/blob/main/spec/diagnostics.md#key-templates";

    private const string CommandHelp = "https://github.com/msallin/DynaSharp/blob/main/spec/diagnostics.md#commands";

    private const string ExpressionHelp = "https://github.com/msallin/DynaSharp/blob/main/spec/diagnostics.md#expressions";
}
