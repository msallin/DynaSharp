using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators;

/// <summary>Shapes, members and constructors: what the model says about a type, the emitters' fixed start.</summary>
public sealed class ModelBuilderTests
{
    private const string Keyed = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E")]""";

    [Test]
    public void Build_SectionFourOrder_MatchesTheSnapshot()
    {
        // Arrange, Act
        var model = Analysis.ModelOfWholeFile(TestSources.SectionFourOrder, "global::Shop.Order");

        // Assert
        SnapshotComparer.Verify("Order.model", ModelText.Of(model));
    }

    [Test]
    public void Build_SectionFourAddress_MatchesTheSnapshot()
    {
        // Arrange, Act
        var model = Analysis.ModelOfWholeFile(TestSources.SectionFourOrder, "global::Shop.Address");

        // Assert
        SnapshotComparer.Verify("Address.model", ModelText.Of(model));
    }

    [Test]
    [Arguments("= [];", nameof(MissingBehavior.KeepCurrent))]
    [Arguments("= new();", nameof(MissingBehavior.KeepCurrent))]
    [Arguments("= new HashSet<string>();", nameof(MissingBehavior.KeepCurrent))]
    [Arguments("= new HashSet<string> { };", nameof(MissingBehavior.KeepCurrent))]
    [Arguments("""= ["a"];""", nameof(MissingBehavior.EmptySet))]
    [Arguments("""= new() { "a" };""", nameof(MissingBehavior.EmptySet))]
    [Arguments("= new(StringComparer.Ordinal);", nameof(MissingBehavior.EmptySet))]
    [Arguments("= null!;", nameof(MissingBehavior.EmptySet))]
    [Arguments("", nameof(MissingBehavior.EmptySet))]
    public async Task Build_InitializerOfASet_OnlyAnEmptyOneReplacesTheEmptySetOfAMissingAttribute(string initializer, string expected)
    {
        // Arrange (a set that the initializer leaves empty is what a missing attribute means; a filled one must not come back from an item without the set)
        var source = $"[DynamoDbDocument] public partial class T {{ public HashSet<string> Tags {{ get; set; }} {initializer} }}";

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(model.Property("Tags").Missing.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_SetBoundToAConstructorParameter_IsTheEmptySetOfAMissingAttribute()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial record T(HashSet<string> Tags);";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(model.Property("Tags").Missing).IsEqualTo(MissingBehavior.EmptySet);
    }

    [Test]
    [Arguments("[DynamoDbDocument] public partial class T { }", "class")]
    [Arguments("[DynamoDbDocument] public partial struct T { }", "struct")]
    [Arguments("[DynamoDbDocument] public partial record T { }", "record")]
    [Arguments("[DynamoDbDocument] public partial record class T { }", "record")]
    [Arguments("[DynamoDbDocument] public partial record struct T { }", "record struct")]
    [Arguments("[DynamoDbDocument] public readonly partial struct T { }", "struct")]
    [Arguments("[DynamoDbDocument] public readonly partial record struct T { }", "record struct")]
    [Arguments("[DynamoDbDocument] public sealed partial class T { }", "class")]
    public async Task Build_Kind_GivesTheKeyword(string source, string keyword)
    {
        // Arrange, Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(model.Keyword).IsEqualTo(keyword);
    }

    [Test]
    public async Task Build_NestedTypes_GivesContainersOutermostFirstAndTheHintName()
    {
        // Arrange
        const string Source = """
            public partial class Outer { public partial record struct Inner { [DynamoDbDocument] public partial class T { } } }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.Outer.Inner.T");

        // Assert
        await Assert.That((string.Join(" ", model.ContainingTypes.Select(type => $"{type.Keyword}:{type.Identifier}")), model.HintName, model.Namespace, model.Name))
            .IsEqualTo(("class:Outer record struct:Inner", "Shop.Outer.Inner.T.g.cs", (string?)"Shop", "T"));
    }

    [Test]
    public async Task Build_GlobalNamespace_HasNoNamespace()
    {
        // Arrange
        const string Source = "[DynaSharp.Mapping.DynamoDbDocument] public partial class T { }";

        // Act
        var model = Analysis.ModelOfWholeFile(Source, "global::T");

        // Assert
        await Assert.That((model.Namespace, model.HintName)).IsEqualTo(((string?)null, "T.g.cs"));
    }

    [Test]
    public async Task Build_KeywordNames_AreEscapedAsCode()
    {
        // Arrange
        const string Source = """
            public enum Kind { @default, Other }
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{class}#{Class}")] public partial class @event
            {
                public string @class { get; set; } = "";
                public string Class { get; set; } = "";
                public Kind @enum { get; set; }
            }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.@event");
        var placeholders = model.Entity!.PartitionKey.Template!.Segments.OfType<PlaceholderSegment>().Select(placeholder => placeholder.ParameterName);

        // Assert
        await Assert.That((model.Identifier, model.Name, model.Properties[0].Identifier, model.Properties[1].Identifier, string.Join(" ", placeholders), model.Enums[0].Members[0].Identifier))
            .IsEqualTo(("@event", "event", "@class", "Class", "@class @class_", "@default"));
    }

    [Test]
    [Arguments("public string A { get; set; } = \"\";", "A")]
    [Arguments("public string A { get; init; } = \"\";", "A")]
    [Arguments("public string A { get; private set; } = \"\";", "A")]
    [Arguments("public string A { private get; set; } = \"\";", "A")]
    [Arguments("public string A { get; } = \"\";", "")]
    [Arguments("public string A => \"x\";", "")]
    [Arguments("public string A { set { } }", "")]
    [Arguments("public static string A { get; set; } = \"\";", "")]
    [Arguments("public string this[int index] => \"\";", "")]
    [Arguments("internal string A { get; set; } = \"\";", "")]
    [Arguments("private string A { get; set; } = \"\";", "")]
    [Arguments("[DynamoDbIgnore] public string A { get; set; } = \"\";", "")]
    [Arguments("public int a;", "")]
    [Arguments("[DynamoDbAttribute(\"a\")] private string A { get; set; } = \"\";", "A")]
    public async Task Build_Member_IsMappedByTheRules(string member, string expected)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class T {{ {member} }}";

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(string.Join(" ", model.Properties.Select(property => property.Name))).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_ComputedPropertyForcedIntoTheItem_IsWriteOnlyAndNeedsNoConstructor()
    {
        // Arrange
        const string Source = """[DynamoDbDocument] public partial class T { [DynamoDbAttribute("c")] public string C => "x"; }""";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That((model.Properties[0].IsWriteOnly, model.Properties[0].AttributeName, model.Constructor.NeedsFieldsConstructor)).IsEqualTo((true, "c", false));
    }

    [Test]
    public async Task Build_BaseProperties_ComeFirstAndAnOverrideReplacesThem()
    {
        // Arrange
        const string Source = """
            public class Root { public virtual string V { get; set; } = ""; public string R { get; set; } = ""; public string Hidden { get; set; } = ""; }
            [DynamoDbDocument] public partial class T : Root
            {
                public string Own { get; set; } = "";
                public override string V { get; set; } = "";
                public new int Hidden { get; set; }
            }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(string.Join(" ", model.Properties.Select(property => $"{property.Name}:{property.ValueType}")))
            .IsEqualTo("R:string Own:string V:string Hidden:int");
    }

    [Test]
    public async Task Build_PrivateSetterInABaseOfAnotherAssembly_CountsAsGetOnly()
    {
        // Arrange
        var library = Analysis.Library("""
            namespace Lib
            {
                public class Root
                {
                    public string Locked { get; private set; } = "";
                    public string Open { get; set; } = "";
                }
            }
            """);

        // Act
        var model = Analysis.Model("[DynamoDbDocument] public partial class T : Lib.Root { }", "global::Shop.T", library);

        // Assert
        await Assert.That(string.Join(" ", model.Properties.Select(property => property.Name))).IsEqualTo("Open");
    }

    [Test]
    [Arguments("[DynamoDbIgnore]", "Id")]
    [Arguments("[DynamoDbAttribute(\"v\")]", "Id v")]
    [Arguments("[DynamoDbAttribute(\"v\"), OmitEmpty]", "Id v:WhenNotEmptyLength")]
    [Arguments("[DynamoDbConverter(typeof(Upper))]", "Id V:Always:global::Shop.Upper")]
    public async Task Build_OverrideWithoutTheAttributesOfTheBase_MapsLikeTheBase(string attributes, string expected)
    {
        // Arrange (the mapping attributes are not inherited, so an override that leaves them out used to store a secret, or under another name)
        var source = $$"""
            public class Root { {{attributes}} public virtual string V { get; set; } = ""; }
            public class Middle : Root { public override string V { get; set; } = ""; }
            [DynamoDbEntity, Discriminator("T"), PartitionKey("T#{Id}")]
            public partial class T : Middle { public string Id { get; set; } = ""; public override string V { get; set; } = ""; }

            public readonly struct Upper : IDynamoDbConverter<string>
            {
                public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value.ToUpperInvariant());

                public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();
            }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(string.Join(" ", model.Properties.Select(Describe))).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_OverrideOfAKeyOnlyProperty_IsRestoredFromTheKey()
    {
        // Arrange
        const string Source = """
            public class Root { [KeyOnly] public virtual string V { get; set; } = ""; }
            [DynamoDbEntity, Discriminator("T"), PartitionKey("T#{V}")]
            public partial class T : Root { public override string V { get; set; } = ""; }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(Describe(model.Properties.Single())).IsEqualTo("V<-pk");
    }

    [Test]
    public async Task Build_OverrideWithAnAttributeOfItsOwn_WinsOverTheBase()
    {
        // Arrange
        const string Source = """
            public class Root { [DynamoDbAttribute("v"), OmitEmpty] public virtual string V { get; set; } = ""; }
            [DynamoDbDocument] public partial class T : Root { [DynamoDbAttribute("w")] public override string V { get; set; } = ""; }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(Describe(model.Properties.Single())).IsEqualTo("w:WhenNotEmptyLength");
    }

    [Test]
    [Arguments("[PartitionKey]", "V", null)]
    [Arguments("[SortKey]", "Id", "V")]
    public async Task Build_OverrideOfAPlainKey_StaysThePlainKey(string attribute, string partition, string? sort)
    {
        // Arrange
        var source = $$"""
            public class Root { {{attribute}} public virtual string V { get; set; } = ""; }
            [DynamoDbEntity, Discriminator("T")]
            public partial class T : Root { {{(sort is null ? string.Empty : "[PartitionKey] ")}}public string Id { get; set; } = ""; public override string V { get; set; } = ""; }
            """;

        // Act
        var entity = Analysis.Model(source, "global::Shop.T").Entity!;

        // Assert
        await Assert.That((entity.PartitionKey.PlainProperty, entity.SortKey?.PlainProperty)).IsEqualTo((partition, sort));
    }

    [Test]
    public async Task Build_OverrideOfAVersion_StaysTheVersion()
    {
        // Arrange
        const string Source = """
            public class Root { [Version] public virtual long Revision { get; set; } }
            [DynamoDbEntity, Discriminator("T"), PartitionKey("T#{Id}")]
            public partial class T : Root { public string Id { get; set; } = ""; public override long Revision { get; set; } }
            """;

        // Act
        var entity = Analysis.Model(Source, "global::Shop.T").Entity!;

        // Assert
        await Assert.That(entity.VersionProperty).IsEqualTo("Revision");
    }

    [Test]
    public async Task Build_PropertiesThatTheFlagOfAnotherWouldName_GetFlagsOfTheirOwn()
    {
        // Arrange (the flags sit beside the values in __Fields, so X__set must not be the flag of X)
        const string Source = """[DynamoDbDocument] public partial class T { public string X { get; set; } = ""; public string X__set { get; set; } = ""; public string X__set_ { get; set; } = ""; }""";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(string.Join(" ", model.Properties.Select(static property => $"{property.Name}:{property.SetFlag}"))).IsEqualTo("X:X__set__ X__set:X__set__set X__set_:X__set___set");
    }

    [Test]
    [Arguments("public required string A { get; set; }", "Throw")]
    [Arguments("public required string? A { get; set; }", "KeepCurrent")]
    [Arguments("public string A { get; set; } = \"\";", "KeepCurrent")]
    [Arguments("public string? A { get; set; }", "KeepCurrent")]
    [Arguments("public int A { get; set; }", "KeepCurrent")]
    [Arguments("public required HashSet<string> A { get; set; }", "EmptySet")]
    public async Task Build_Property_HasTheMissingBehavior(string member, string expected)
    {
        // Arrange
        var source = $"[DynamoDbDocument] public partial class T {{ {member} }}";

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(model.Properties[0].Missing.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("string name", "Throw", null)]
    [Arguments("int name", "Throw", null)]
    [Arguments("string? name", "KeepCurrent", null)]
    [Arguments("int? name", "KeepCurrent", null)]
    [Arguments("int name = 5", "KeepCurrent", "5")]
    public async Task Build_ConstructorParameter_HasTheMissingBehavior(string parameter, string expected, string? literal)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(parameter);
        var type = TypeOf(parameter);
        var source = $"[DynamoDbDocument] public partial class T {{ public T({parameter}) {{ }} public {type} Name {{ get; }} = default!; }}";

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That((model.Properties[0].Missing.ToString(), model.Constructor.Parameters[0].DefaultLiteral, model.Properties[0].IsConstructorParameter)).IsEqualTo((expected, literal, true));
    }

    [Test]
    [Arguments("int V = 5", "5")]
    [Arguments("int V = -5", "-5")]
    [Arguments("long V = 5", "5")]
    [Arguments("string V = \"a\\\"b\"", "\"a\\\"b\"")]
    [Arguments("string? V = null", "default")]
    [Arguments("Guid V = default", "default")]
    [Arguments("bool V = true", "true")]
    [Arguments("decimal V = 1.5m", "1.5m")]
    [Arguments("float V = 1.5f", "1.5f")]
    [Arguments("double V = 1.5", "1.5")]
    [Arguments("double V = double.NaN", "double.NaN")]
    [Arguments("double V = double.PositiveInfinity", "double.PositiveInfinity")]
    [Arguments("float V = float.NegativeInfinity", "float.NegativeInfinity")]
    [Arguments("Color V = Color.Green", "(global::Shop.Color)(1)")]
    [Arguments("Color V = (Color)(-1)", "(global::Shop.Color)(-1)")]
    [Arguments("Color? V = Color.Green", "(global::Shop.Color?)(1)")]
    public async Task Build_ConstructorDefault_BecomesALiteral(string parameter, string literal)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(parameter);
        var source = $$"""
            public enum Color { Red, Green }
            [DynamoDbDocument] public partial class T { public T({{parameter}}) { } public {{TypeOf(parameter)}} V { get; } = default!; }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(model.Constructor.Parameters[0].DefaultLiteral).IsEqualTo(literal);
    }

    [Test]
    public async Task Build_ImplicitConstructorWithMappedProperties_DeclaresTheDefaultOne()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { public int A { get; set; } }";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That((constructor.DeclareDefaultConstructor, constructor.NeedsFieldsConstructor, constructor.Parameters.Count)).IsEqualTo((true, true, 0));
    }

    [Test]
    public async Task Build_ExplicitParameterlessConstructorOfAnyAccessibility_IsChosen()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { private T() { } public T(int a) { A = a; } public int A { get; set; } }";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That((constructor.DeclareDefaultConstructor, constructor.NeedsFieldsConstructor, constructor.Parameters.Count)).IsEqualTo((false, true, 0));
    }

    [Test]
    public async Task Build_MarkedConstructor_WinsOverTheParameterlessOne()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { public T() { } [DynamoDbConstructor] public T(int a) { A = a; } public int A { get; } }";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That((constructor.Parameters[0].PropertyName, constructor.NeedsFieldsConstructor)).IsEqualTo(("A", false));
    }

    [Test]
    public async Task Build_SingleConstructor_BindsByNameIgnoringCase()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { public T(int a, string b) { A = a; B = b; } public int A { get; } public string B { get; } public int C { get; set; } }";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That((string.Join(" ", constructor.Parameters.Select(parameter => parameter.PropertyName)), constructor.NeedsFieldsConstructor, constructor.DeclareDefaultConstructor))
            .IsEqualTo(("A B", true, false));
    }

    [Test]
    public async Task Build_ExactNameMatch_WinsOverACaseInsensitiveOne()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { public T(int value) { } public int Value { get; } public int value { get; } }";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(model.Constructor.Parameters[0].PropertyName).IsEqualTo("value");
    }

    [Test]
    public async Task Build_PositionalRecord_UsesThePrimaryConstructorNotTheCopyConstructor()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial record T(string A, int B = 3);";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That((string.Join(" ", constructor.Parameters.Select(parameter => parameter.PropertyName)), constructor.Parameters[1].DefaultLiteral, constructor.NeedsFieldsConstructor))
            .IsEqualTo(("A B", (string?)"3", false));
    }

    [Test]
    public async Task Build_PositionalRecordStruct_UsesThePrimaryConstructorNotTheImplicitOne()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial record struct T(string A);";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That(constructor.Parameters.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Build_StructWithOnlyTheImplicitConstructor_DoesNotDeclareOne()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial struct T { public int A { get; set; } }";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That((constructor.DeclareDefaultConstructor, constructor.NeedsFieldsConstructor)).IsEqualTo((false, true));
    }

    [Test]
    public async Task Build_StructWithAnExplicitParameterlessConstructor_ChoosesIt()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial struct T { public T() { } public T(int a) { } public int A { get; set; } }";

        // Act
        var constructor = Analysis.Model(Source, "global::Shop.T").Constructor;

        // Assert
        await Assert.That(constructor.Parameters.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_Enums_ListCanonicalMembersAndAliases()
    {
        // Arrange
        const string Source = """
            public enum Mode : byte { A, Aé, B = A }
            [DynamoDbDocument] public partial class T { public Mode M { get; set; } public List<Mode> Many { get; set; } = []; }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");
        var mode = model.Enums.Single();

        // Assert
        await Assert.That((mode.FullName, mode.ConverterName, mode.UnderlyingType, mode.StoreAsNumber, string.Join(" ", mode.Members.Select(member => $"{member.Name}:{member.Utf8Length}:{member.IsCanonical}"))))
            .IsEqualTo(("global::Shop.Mode", "Shop_ModeConverter", "byte", false, "A:1:True Aé:3:True B:1:False"));
    }

    [Test]
    public async Task Build_FlagsEnum_IsStoredAsANumber()
    {
        // Arrange
        const string Source = """
            [Flags] public enum Mode { A = 1, B = 2 }
            [DynamoDbDocument] public partial class T { public Mode M { get; set; } }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(model.Enums.Single().StoreAsNumber).IsTrue();
    }

    [Test]
    public async Task Build_TwoEnumsWhoseNamesFlattenAlike_GetDifferentConverters()
    {
        // Arrange
        const string Source = """
            namespace N.A { public enum Color { Red } }
            namespace N_A { public enum Color { Green } }
            namespace Z { [DynaSharp.Mapping.DynamoDbDocument] public partial class T { public N.A.Color? X { get; set; } public N_A.Color Y { get; set; } } }
            """;

        // Act
        var model = Analysis.ModelOfWholeFile(Source, "global::Z.T");

        // Assert
        await Assert.That(string.Join(" ", model.Enums.Select(type => type.ConverterName))).IsEqualTo("N_A_ColorConverter N_A_ColorConverter2");
    }

    [Test]
    public async Task Build_Discriminator_HasADefaultAttribute()
    {
        // Arrange
        var source = Keyed + " public partial class E { }";

        // Act
        var entity = Analysis.Model(source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That((entity.Discriminator, entity.DiscriminatorAttribute)).IsEqualTo(((string?)"E", "_type"));
    }

    [Test]
    public async Task Build_DiscriminatorWithAttributeName_UsesIt()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E", AttributeName = "kind"), PartitionKey("E")] public partial class E { }""";

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That(entity.DiscriminatorAttribute).IsEqualTo("kind");
    }

    [Test]
    public async Task Build_DiscriminatorOptOut_HasNoValue()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("E")] public partial class E { }""";

        // Act
        var entity = Analysis.Model(Source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That(entity.Discriminator).IsNull();
    }

    [Test]
    public async Task Build_Version_NamesTheProperty()
    {
        // Arrange
        var source = Keyed + " public partial class E { [Version] public int Revision { get; set; } }";

        // Act
        var entity = Analysis.Model(source, "global::Shop.E").Entity!;

        // Assert
        await Assert.That(entity.VersionProperty).IsEqualTo("Revision");
    }

    [Test]
    [Arguments("[Version]", false)]
    [Arguments("[Version(Missing = VersionMissing.Reject)]", false)]
    [Arguments("[Version(Missing = VersionMissing.Allow)]", true)]
    public async Task Build_VersionMissing_SaysHowTheVersionReadsAnItemWithoutOne(string attribute, bool expected)
    {
        // Arrange
        var source = Keyed + " public partial class E { " + attribute + " public int Revision { get; set; } }";

        // Act
        var property = Analysis.Model(source, "global::Shop.E").Property("Revision");

        // Assert
        await Assert.That(property.ReadsMissingAsZero).IsEqualTo(expected);
    }

    [Test]
    [Arguments("public required int Revision { get; set; }", false, "Throw")]
    [Arguments("public required int Revision { get; set; }", true, "KeepCurrent")]
    public async Task Build_RequiredVersion_ThrowsOnAnItemWithoutOneUnlessItAllowsThat(string property, bool allow, string expected)
    {
        // Arrange
        var attribute = allow ? "[Version(Missing = VersionMissing.Allow)]" : "[Version]";
        var source = Keyed + " public partial class E { " + attribute + " " + property + " }";

        // Act
        var model = Analysis.Model(source, "global::Shop.E").Property("Revision");

        // Assert
        await Assert.That(model.Missing.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_DocumentWithAKeysMember_IsFine()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { public int Keys { get; set; } }";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That((model.Entity, model.HidesInheritedKeys, model.HidesInheritedAttrs)).IsEqualTo(((EntityModel?)null, false, false));
    }

    [Test]
    public async Task Build_EntityDerivedFromAnEntity_HidesBothGeneratedClasses()
    {
        // Arrange
        var source = Keyed + " public partial class Root { } " + Keyed.Replace("\"E\"", "\"D\"", StringComparison.Ordinal) + " public partial class D : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.D");

        // Assert
        await Assert.That((model.HidesInheritedKeys, model.HidesInheritedAttrs)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Build_BaseWithAMemberOfThatName_HidesOnlyThatClass()
    {
        // Arrange
        var source = "public class Root { public int Attrs { get; set; } } " + Keyed + " public partial class E : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.E");

        // Assert
        await Assert.That((model.HidesInheritedKeys, model.HidesInheritedAttrs)).IsEqualTo((false, true));
    }

    [Test]
    public async Task Build_EntityDerivedFromAnEntityWithAnIndex_HidesItsIndexes()
    {
        // Arrange
        var source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("Gsi", PartitionKey = "A")] public partial class Root { } """ + Keyed.Replace("\"E\"", "\"D\"", StringComparison.Ordinal) + " public partial class D : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.D");

        // Assert
        await Assert.That(model.HidesInheritedIndexes).IsTrue();
    }

    [Test]
    public async Task Build_EntityDerivedFromAnEntityWithoutAnIndex_HidesNoIndexes()
    {
        // Arrange (such a base has no Indexes, and a needless new is a warning)
        var source = Keyed + " public partial class Root { } " + Keyed.Replace("\"E\"", "\"D\"", StringComparison.Ordinal) + " public partial class D : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.D");

        // Assert
        await Assert.That(model.HidesInheritedIndexes).IsFalse();
    }

    [Test]
    public async Task Build_BaseWithAnIndexAttributeThatIsNoEntity_HidesNoIndexes()
    {
        // Arrange (nothing generates for it)
        var source = """[GlobalIndexKey("Gsi", PartitionKey = "A")] public class Root { } """ + Keyed + " public partial class E : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.E");

        // Assert
        await Assert.That(model.HidesInheritedIndexes).IsFalse();
    }

    [Test]
    public async Task Build_BaseEntityWithALocalIndex_HidesItsIndexes()
    {
        // Arrange
        var source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), LocalIndexKey("Lsi", "A")] public partial class Root { } """ + Keyed.Replace("\"E\"", "\"D\"", StringComparison.Ordinal) + " public partial class D : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.D");

        // Assert
        await Assert.That(model.HidesInheritedIndexes).IsTrue();
    }

    [Test]
    public async Task Build_BaseWithAMemberNamedIndexes_HidesIt()
    {
        // Arrange
        var source = "public class Root { public int Indexes { get; set; } } " + Keyed + " public partial class E : Root { }";

        // Act
        var model = Analysis.Model(source, "global::Shop.E");

        // Assert
        await Assert.That(model.HidesInheritedIndexes).IsTrue();
    }

    [Test]
    public async Task Build_DocumentDerivedFromADocument_HidesAttrsAndHasNoKeys()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class Root { } [DynamoDbDocument] public partial class T : Root { }";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That((model.HidesInheritedKeys, model.HidesInheritedAttrs)).IsEqualTo((false, true));
    }

    [Test]
    public async Task Build_DocumentWithABaseMemberNamedAttrs_HidesIt()
    {
        // Arrange
        const string Source = "public class Root { public int Attrs { get; set; } } [DynamoDbDocument] public partial class T : Root { }";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(model.HidesInheritedAttrs).IsTrue();
    }

    [Test]
    [Arguments("public int X { get; set; }", true)]
    [Arguments("[DynamoDbAttribute(\"x\")] internal int X { get; set; }", false)]
    [Arguments("public List<Visible> X { get; set; } = null!;", true)]
    [Arguments("[DynamoDbConverter(typeof(Open))] public int X { get; set; }", true)]
    [Arguments("[DynamoDbConverter(typeof(Closed))] public int X { get; set; }", false)]
    public async Task Build_Property_SaysWhetherAPublicHandleCanNameIt(string property, bool expected)
    {
        // Arrange
        var source = $$"""
            public enum Visible { A }
            public readonly struct Open : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter writer, int value) { }
                public static int Read(ref DynamoDbReader reader, AttributeType type) => 0;
            }
            internal readonly struct Closed : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter writer, int value) { }
                public static int Read(ref DynamoDbReader reader, AttributeType type) => 0;
            }
            [DynamoDbDocument] public partial class T { {{property}} }
            """;

        // Act
        var model = Analysis.Model(source, "global::Shop.T");

        // Assert
        await Assert.That(model.Properties[0].IsPublic).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_InternalElementConverterOfASet_MakesTheHandleInternal()
    {
        // Arrange
        const string Source = """
            internal readonly struct Element : IDynamoDbSetElementConverter<Code>
            {
                public static AttributeType SetType => AttributeType.StringSet;
                public static void WriteElement(DynamoDbWriter writer, Code value) { }
                public static Code ReadElement(ref DynamoDbReader reader) => default;
            }
            [DynamoDbConverter(typeof(Element))] public readonly struct Code { }
            [DynamoDbDocument] public partial class T { public HashSet<Code> X { get; set; } = null!; }
            """;

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(model.Properties[0].IsPublic).IsFalse();
    }

    [Test]
    public async Task Build_TwoPropertiesOneParameterName_KeepsThemApart()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class T { public int Id { get; set; } public int id { get; set; } }";

        // Act
        var model = Analysis.Model(Source, "global::Shop.T");

        // Assert
        await Assert.That(string.Join(" ", model.Properties.Select(static property => property.ParameterName))).IsEqualTo("id id_");
    }

    // "int name = 5" gives "int".
    private static string TypeOf(string parameter) => parameter.Split(' ')[0];

    // The attribute where it is not the name, the write where it is not Always or the converter where it is not the built-in, and the key it is restored from.
    private static string Describe(PropertyModel property)
    {
        var name = property.AttributeName == property.Name ? property.Name : property.AttributeName;
        if (property.RestoredFromKey is { } key)
        {
            return $"{name}<-{key}";
        }

        var converter = property.Converter!.WriteConverter.StartsWith("global::Shop.", StringComparison.Ordinal) ? ":" + property.Converter.WriteConverter : string.Empty;
        var write = property.Write == WriteCondition.Always && converter.Length == 0 ? string.Empty : ":" + property.Write;
        return name + write + converter;
    }
}
