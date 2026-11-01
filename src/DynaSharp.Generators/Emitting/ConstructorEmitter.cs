namespace DynaSharp.Generators;

/// <summary>
/// Writes how a read creates an instance. A constructor inside the type may assign <c>init</c> and private setters conditionally, and with
/// <c>[SetsRequiredMembers]</c> also <c>required</c> members, so one mechanism covers them all and keeps property initializers when an attribute
/// is missing, which an object initializer could not.
/// </summary>
internal static class ConstructorEmitter
{
    public static void Emit(IndentedCode code, TypeModel type)
    {
        var constructor = type.Constructor;
        if (constructor.DeclareDefaultConstructor)
        {
            code.Blank();
            code.Line($"public {type.Identifier}()");
            code.Open();
            code.Close();
        }

        if (!constructor.NeedsFieldsConstructor)
        {
            return;
        }

        code.Blank();
        code.Line("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        code.Line($"private {type.Identifier}(in __Fields fields)");
        code.Line($"    : this({Arguments(type)})");
        code.Open();
        foreach (var property in type.Properties)
        {
            if (!property.IsConstructorParameter && !property.IsWriteOnly)
            {
                Assign(code, property);
            }
        }

        code.Close();
    }

    /// <summary>The expression that <c>ReadAttributes</c> returns, once the checks for missing attributes have passed.</summary>
    public static string Creation(TypeModel type) =>
        type.Constructor.NeedsFieldsConstructor ? $"new {type.FullName}(in fields)" : $"new {type.FullName}({Arguments(type)})";

    private static string Arguments(TypeModel type) =>
        string.Join(", ", type.Constructor.Parameters.Select(parameter => Argument(parameter, type.Property(parameter.PropertyName))));

    private static string Argument(ConstructorParameter parameter, PropertyModel property)
    {
        // A field of a __Fields that started as default is maybe-null to the compiler, but the checks for missing attributes have passed.
        var value = $"fields.{property.Identifier}{(property.ValueType.EndsWith("?", StringComparison.Ordinal) ? string.Empty : "!")}";
        var fallback = property.Missing == MissingBehavior.EmptySet ? property.Converter!.EmptySet : parameter.DefaultLiteral;
        return fallback is null ? value : $"fields.{property.Name}__set ? {value} : {fallback}";
    }

    private static void Assign(IndentedCode code, PropertyModel property)
    {
        var (name, value) = (property.Identifier, $"fields.{property.Identifier}");
        var isSet = $"fields.{property.Name}__set";
        code.Line(property.Missing switch
        {
            MissingBehavior.Throw => $"this.{name} = {value};",
            MissingBehavior.EmptySet => $"this.{name} = {isSet} ? {value} : {property.Converter!.EmptySet};",
            _ => $"if ({isSet}) {{ this.{name} = {value}; }}",
        });
    }
}
