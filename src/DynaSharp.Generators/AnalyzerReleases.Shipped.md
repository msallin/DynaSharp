; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA001 | DynaSharp.Mapping | Error | Type is not partial
DYNA002 | DynaSharp.Mapping | Error | Entity has no partition key
DYNA003 | DynaSharp.Mapping | Error | Unsupported property type
DYNA004 | DynaSharp.Mapping | Error | Attribute name is used twice
DYNA005 | DynaSharp.Mapping | Error | No usable constructor
DYNA006 | DynaSharp.Mapping | Error | Constructor parameter has no property
DYNA007 | DynaSharp.Mapping | Error | Type cannot be mapped
DYNA008 | DynaSharp.Mapping | Error | Converter is not usable
DYNA009 | DynaSharp.Mapping | Error | Key is declared twice
DYNA010 | DynaSharp.Mapping | Error | Attribute name is invalid
DYNA011 | DynaSharp.Mapping | Error | [Version] is not valid here
DYNA012 | DynaSharp.Mapping | Error | Attribute is not valid on a document
DYNA013 | DynaSharp.Mapping | Error | Both entity and document
DYNA014 | DynaSharp.Mapping | Error | Generated member conflicts
DYNA015 | DynaSharp.Mapping | Error | C# version is too low
DYNA016 | DynaSharp.Mapping | Error | Type is not accessible
DYNA017 | DynaSharp.Mapping | Error | Property cannot be a key
DYNA018 | DynaSharp.Mapping | Error | Index is invalid
DYNA019 | DynaSharp.Mapping | Error | Type is outside a nullable context
DYNA020 | DynaSharp.Mapping | Error | Entity has no discriminator
DYNA021 | DynaSharp.Mapping | Error | Plain key is ignored
DYNA022 | DynaSharp.Mapping | Error | Discriminator is invalid
DYNA023 | DynaSharp.Mapping | Error | [Version] property is part of a key
DYNA024 | DynaSharp.Mapping | Error | Enum has no members
DYNA025 | DynaSharp.Mapping | Error | Type has the name of a generated class
DYNA100 | DynaSharp.Keys | Error | Key template syntax error
DYNA101 | DynaSharp.Keys | Error | Key template literal is not ASCII
DYNA102 | DynaSharp.Keys | Error | Unknown property in a key template
DYNA103 | DynaSharp.Keys | Error | Type cannot be used in a key
DYNA104 | DynaSharp.Keys | Error | Format does not fit the type
DYNA105 | DynaSharp.Keys | Error | Format is out of range
DYNA106 | DynaSharp.Keys | Error | Placeholders cannot be told apart
DYNA107 | DynaSharp.Keys | Error | Nullable input in a key
DYNA108 | DynaSharp.Keys | Warning | Number without a fixed format in a sort key
DYNA109 | DynaSharp.Keys | Error | [KeyOnly] is not valid here
DYNA110 | DynaSharp.Keys | Warning | Sparse index without nullable inputs
DYNA111 | DynaSharp.Keys | Error | Property is repeated in a key template
DYNA112 | DynaSharp.Keys | Error | Key on the wrong target
DYNA113 | DynaSharp.Keys | Error | Enum names are not one-to-one
DYNA114 | DynaSharp.Keys | Error | Computed property in an index template
DYNA115 | DynaSharp.Keys | Info | String before a delimiter in a sort key
DYNA201 | DynaSharp.Commands | Warning | Command outlives its expression
DYNA202 | DynaSharp.Commands | Warning | Read command is not awaited
DYNA210 | DynaSharp.Expressions | Warning | String where a name belongs
DYNA211 | DynaSharp.Expressions | Error | Placeholder in an expression
DYNA212 | DynaSharp.Expressions | Warning | Expression syntax error
DYNA213 | DynaSharp.Expressions | Warning | Path matches no attribute
DYNA214 | DynaSharp.Expressions | Warning | Attribute path is not valid
