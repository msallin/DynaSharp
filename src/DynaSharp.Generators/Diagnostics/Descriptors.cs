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
        "Entity '{0}' has no discriminator. Add [Discriminator(\"{1}\")]. For a table written by other tools, opt out with [Discriminator(OptOut = true)], or with [assembly: DynamoDbDefaults(DiscriminatorOptOut = true)] for every entity of the project.",
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

    public static readonly DiagnosticDescriptor InvalidDefaultConverter = new(
        "DYNA029", "[DynamoDbDefaultConverter] is not valid", "[DynamoDbDefaultConverter] for '{0}' is not valid: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor InvalidOmitEmpty = new(
        "DYNA030", "[OmitEmpty] is not valid here", "[OmitEmpty] on '{0}' is not valid: {1}", Mapping, DiagnosticSeverity.Error, true, helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor EntityInsideEntity = new(
        "DYNA031",
        "Entity stored inside an entity",
        "Property '{0}' stores the entity '{1}' as a map, with key and index attributes that nothing reads. Use a [DynamoDbDocument] type for it.",
        Mapping,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor UnnamedComputedSource = new(
        "DYNA032",
        "Getter reads a property that [ComputedFrom] does not name",
        "The getter of '{0}' reads '{1}', which [ComputedFrom] does not name, so an update of '{1}' leaves a key built from '{0}' behind. Name it in [ComputedFrom].",
        Mapping,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor IneffectiveEnumAttribute = new(
        "DYNA033",
        "[DynamoDbEnum] has no effect",
        "Enum '{0}' asks for an unknown name to count as missing, but {1}",
        Mapping,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor UnreachableSetter = new(
        "DYNA034",
        "Property of a base type is not stored",
        "'{0}' of '{1}' has a setter that '{2}' cannot reach, so '{2}' does not store it. Make the setter protected, or mark the property [DynamoDbIgnore] if no type is to store it.",
        Mapping,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: EntityHelp);

    public static readonly DiagnosticDescriptor NullReadsBackAsInitialValue = new(
        "DYNA035",
        "Nullable property does not read back null",
        "'{0}' is nullable, but a write leaves null out of the item and a read of an item without the attribute keeps {1}, so a null that was written reads back as that value. {2}.",
        Mapping,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: EntityHelp);

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
        "Computed property '{0}' cannot be used in an index template, because an update cannot keep the index current. Add [ComputedFrom] naming the stored properties it reads, or use a stored property.",
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
        "Timestamp '{0}' ends the key of the table and is truncated, so two items close in time get one key and the second replaces the first. Add a placeholder after it that breaks the tie, or drop the format: {{{0}}} keeps ticks, all seven digits.",
        KeyGrammar,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor EmptyDefaultOfKey = new(
        "DYNA117",
        "Key property starts empty",
        "Key property '{0}' starts empty and nothing has to set it, so a write that forgets it fails at run time. Make it required, or set it in a constructor.",
        KeyGrammar,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor RawNotLast = new(
        "DYNA118",
        "Raw value does not end the template",
        "'{0}' has the format raw, which reads to the end of the key, so nothing can follow it in the template",
        KeyGrammar,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor InvalidComputedFrom = new(
        "DYNA119",
        "[ComputedFrom] is not valid here",
        "[ComputedFrom] on '{0}' is not valid: {1}",
        KeyGrammar,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor RawBesideStrict = new(
        "DYNA120",
        "Raw value is strict in another template",
        "'{0}' is raw in '{1}' and strict in '{2}'{4}, so a value with a delimiter of the second ('{3}') fits the first and throws KeyFormatException at the second. Use raw in both, at the end of each template, or in neither.",
        KeyGrammar,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor RawThroughGetter = new(
        "DYNA121",
        "Raw value reaches a strict template through a getter",
        "'{0}' is raw in '{1}', and '{2}', computed from it, is strict in '{3}', so a value with a delimiter of the second ('{4}') throws KeyFormatException there, unless the getter of '{2}' takes the delimiter out",
        KeyGrammar,
        DiagnosticSeverity.Info,
        true,
        helpLinkUri: KeyHelp);

    public static readonly DiagnosticDescriptor ConstantNoKeyHolds = new(
        "DYNA122",
        "Constant that no key can hold",
        "The value of '{0}' {1}, so this call throws KeyFormatException every time",
        KeyGrammar,
        DiagnosticSeverity.Warning,
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
        "A read is not awaited; end the chain with ItemsAsync, ValuesAsync or another terminal it has",
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

    public static readonly DiagnosticDescriptor ProjectionLeavesOutRequired = new(
        "DYNA208",
        "Project leaves out a required member",
        "This Project leaves out '{0}' of '{1}', and a read of the entity cannot do without it, so the read throws DynamoDbMappingException. Include it{2}.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor SelectOfKeyInput = new(
        "DYNA209",
        "Select of an attribute that a read of the entity can do without",
        "'{0}' of '{1}' {2}",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor WriteOfComputedInputSource = new(
        "DYNA219",
        "Write to what a key of an index is computed from",
        "'{0}' {1}, so this update throws when it ends. Put the changed entity instead.",
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

    public static readonly DiagnosticDescriptor NestedConflictRetry = new(
        "DYNA217",
        "Nested RetryOnConflictAsync",
        "RetryOnConflictAsync runs inside the delegate of another RetryOnConflictAsync, so the runs multiply and an exhausted inner retry counts as a conflict of the outer one. Move the follow-up write after the outer call.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor VersionReadBeforeRetry = new(
        "DYNA218",
        "Version read before the retry",
        "'{0}' was read before RetryOnConflictAsync started, so every run sends the same version and fails the same way. Read it inside the delegate.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor ReservedWordInExpression = new(
        "DYNA220",
        "Reserved word as a name",
        "'{0}' is a reserved word of DynamoDB, which rejects it as a bare name in an expression. Write the name in Attr(...) or use a handle, which DynaSharp sends as a #name placeholder.",
        Expressions,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: ExpressionHelp);

    public static readonly DiagnosticDescriptor FilterOnLosslessKey = new(
        "DYNA221",
        "Filter on what the key condition decides",
        "'{0}' is held by the key '{1}' that this query reads, so the key condition decides what this filter tests{2}. Test it there.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor FilterOnLossyKey = new(
        "DYNA222",
        "Filter on what a key holds in part",
        "'{0}' is held in part by the key '{1}' that this query reads, so the key condition can narrow the read before this filter. A filter finer than the key is fine.",
        Commands,
        DiagnosticSeverity.Info,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor OverlappingUpdatePaths = new(
        "DYNA223",
        "Two actions on one path",
        "This {0} changes '{1}', and an earlier action of this update changes '{2}', so DynamoDB rejects the request for paths that overlap. Keep one action per path.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor KeyOfAnotherAttribute = new(
        "DYNA224",
        "Key of another attribute",
        "{0} builds a key of the attribute {1}, and this query of {2} reads {3}, so {4}",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor IncludedKeyDiffers = new(
        "DYNA225",
        "Included entity has another key",
        "'{0}' has the key {1}, and '{2}' has {3}, so they cannot share a table and Including throws ArgumentException",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor IndexKeyWithoutAllInputs = new(
        "DYNA226",
        "Index key without all its inputs",
        "This update of '{0}' sets {1} and not {2}, so it cannot write the key {3} of the index {4} and throws when it is sent, because an update does not read the item and cannot tell what it holds for {2}. {5}.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor ValueWithoutFound = new(
        "DYNA227",
        "Value without Found",
        "Value of this SelectResult throws InvalidOperationException when the read found no item, and this member never looks at Found. Test Found first, or use GetValueOrDefault().",
        Commands,
        DiagnosticSeverity.Info,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor KeyOfAnotherEntity = new(
        "DYNA230",
        "Key that does not fit the table",
        "'{0}' is {1}, and {2}. {3}.",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor NotAnEntity = new(
        "DYNA231",
        "Type is not an entity",
        "'{0}' {1}. {2}.",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor ValueOfAnotherType = new(
        "DYNA232",
        "Value of another type than its handle",
        "The handle '{0}' holds '{1}', and this value is '{2}'. Pass a value of '{1}', or the handle of the attribute you meant.",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor AddWithoutNumberOrSet = new(
        "DYNA233",
        "Add on a handle that is no number or set",
        "Add adds to a number or a set, and '{0}' holds '{1}'. Use Set to write the value.",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor ItemMethodOnRead = new(
        "DYNA234",
        "Method of the items called on a read",
        "'{0}' works on the items that a read streams, not on the read. Call {1}() first, as in {1}().{0}(...).",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor IfVersionOnPut = new(
        "DYNA235",
        "IfVersion on a Put",
        "{0}. {1}.",
        Commands,
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor VersionedBatchWrite = new(
        "DYNA236",
        "Batch of an entity with a version",
        "'{0}' has a [Version], and a batch cannot carry the condition of a version, so this batch throws InvalidOperationException when it is sent. Call IgnoreVersion() on the batch to write the items as they are, with the version each holds, or write them one by one with Put.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly DiagnosticDescriptor DateTimeWithoutKind = new(
        "DYNA237",
        "DateTime without a kind",
        "'{0}' is a DateTime of kind Unspecified, which names no instant, so {1}. Pass DateTimeKind.Utc, as in new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), or use DateOnly for a date.",
        Commands,
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: CommandHelp);

    public static readonly IReadOnlyList<DiagnosticDescriptor> All =
    [
        NotPartial, NoPartitionKey, UnsupportedType, DuplicateAttributeName, NoUsableConstructor, UnboundParameter, UnmappableType,
        InvalidConverter, DuplicateKey, InvalidAttributeName, InvalidVersion, NotValidOnDocument, EntityAndDocument, MemberConflict,
        LanguageVersionTooLow, InaccessibleType, InvalidKeyType, InvalidIndex, NullableContextDisabled, NoDiscriminator, IgnoredPlainKey,
        InvalidDiscriminator, VersionInKey, EnumWithoutMembers, NamedLikeGeneratedClass, DuplicateDiscriminator, InvalidDefaultConverter, InvalidOmitEmpty, EntityInsideEntity, UnnamedComputedSource, IneffectiveEnumAttribute, TemplateSyntax, LiteralNotAscii, UnknownProperty, UnsupportedKeyInput, FormatDoesNotFit, FormatOutOfRange,
        NoLiteralBetween, NullableInput, NumberWithoutFixedFormat, InvalidKeyOnly, SparseWithoutNullableInput, RepeatedPlaceholder,
        MisplacedKey, EnumNamesNotUnique, ComputedInIndex, StringBeforeDelimiter, TruncatedTimestampEndsTableKey, EmptyDefaultOfKey, RawNotLast, InvalidComputedFrom, RawBesideStrict, RawThroughGetter, ConstantNoKeyHolds,
        CommandOutlivesExpression, ReadCommandNotAwaitable, CommandDiscarded, CommandReused, WriteOfTableKeyInput, IfNotExistsOnVersionedPut, UndeclaredIndex, ProjectionLeavesOutRequired, SelectOfKeyInput, StringInNamePosition, PlaceholderInExpression, ExpressionSyntaxError, PathMatchesNoAttribute, InvalidPath, HandleOfAnotherType, TextFunctionOnNumber, NestedConflictRetry, VersionReadBeforeRetry, WriteOfComputedInputSource,
        ReservedWordInExpression, FilterOnLosslessKey, FilterOnLossyKey, OverlappingUpdatePaths, KeyOfAnotherAttribute, IncludedKeyDiffers, IndexKeyWithoutAllInputs, ValueWithoutFound,
        KeyOfAnotherEntity, NotAnEntity, ValueOfAnotherType, AddWithoutNumberOrSet, ItemMethodOnRead,
        UnreachableSetter, NullReadsBackAsInitialValue, IfVersionOnPut, VersionedBatchWrite, DateTimeWithoutKind,
    ];

    public static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> ById = All.ToDictionary(descriptor => descriptor.Id);

    private const string Mapping = "DynaSharp.Mapping";

    private const string KeyGrammar = "DynaSharp.Keys";

    private const string Commands = "DynaSharp.Commands";

    private const string Expressions = "DynaSharp.Expressions";

    private const string EntityHelp = "https://msallin.github.io/DynaSharp/diagnostics.html#entities";

    private const string KeyHelp = "https://msallin.github.io/DynaSharp/diagnostics.html#key-templates";

    private const string CommandHelp = "https://msallin.github.io/DynaSharp/diagnostics.html#commands";

    private const string ExpressionHelp = "https://msallin.github.io/DynaSharp/diagnostics.html#expressions";
}
