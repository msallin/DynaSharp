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
DYNA026 | DynaSharp.Mapping | Warning | Two entities share a discriminator
DYNA029 | DynaSharp.Mapping | Error | [DynamoDbDefaultConverter] is not valid
DYNA030 | DynaSharp.Mapping | Error | [OmitEmpty] is not valid here
DYNA031 | DynaSharp.Mapping | Warning | Entity stored inside an entity
DYNA032 | DynaSharp.Mapping | Warning | Getter reads a property that [ComputedFrom] does not name
DYNA033 | DynaSharp.Mapping | Warning | [DynamoDbEnum] has no effect
DYNA034 | DynaSharp.Mapping | Warning | Property of a base type is not stored
DYNA035 | DynaSharp.Mapping | Warning | Nullable property does not read back null
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
DYNA116 | DynaSharp.Keys | Warning | Truncated timestamp at the end of a table key
DYNA117 | DynaSharp.Keys | Warning | Key property starts empty
DYNA118 | DynaSharp.Keys | Error | Raw value does not end the template
DYNA119 | DynaSharp.Keys | Error | [ComputedFrom] is not valid here
DYNA120 | DynaSharp.Keys | Warning | Raw value is strict in another template
DYNA121 | DynaSharp.Keys | Info | Raw value reaches a strict template through a getter
DYNA122 | DynaSharp.Keys | Warning | Constant that no key can hold
DYNA201 | DynaSharp.Commands | Warning | Command outlives its expression
DYNA202 | DynaSharp.Commands | Warning | Read command is not awaited
DYNA203 | DynaSharp.Commands | Error | Command is discarded
DYNA204 | DynaSharp.Commands | Warning | Command is used twice
DYNA205 | DynaSharp.Commands | Warning | Write to a key of the table
DYNA206 | DynaSharp.Commands | Warning | IfNotExists on a Put with a version
DYNA207 | DynaSharp.Commands | Warning | Index is not declared
DYNA208 | DynaSharp.Commands | Warning | Project leaves out a required member
DYNA209 | DynaSharp.Commands | Warning | Select of an attribute that a read of the entity can do without
DYNA210 | DynaSharp.Expressions | Warning | String where a name belongs
DYNA211 | DynaSharp.Expressions | Error | Placeholder in an expression
DYNA212 | DynaSharp.Expressions | Warning | Expression syntax error
DYNA213 | DynaSharp.Expressions | Warning | Path matches no attribute
DYNA214 | DynaSharp.Expressions | Warning | Attribute path is not valid
DYNA215 | DynaSharp.Expressions | Warning | Handle of another type
DYNA216 | DynaSharp.Expressions | Warning | Text function on a number
DYNA217 | DynaSharp.Commands | Warning | Nested RetryOnConflictAsync
DYNA218 | DynaSharp.Commands | Warning | Version read before the retry
DYNA219 | DynaSharp.Commands | Warning | Write to what a key of an index is computed from
DYNA220 | DynaSharp.Expressions | Warning | Reserved word as a name
DYNA221 | DynaSharp.Commands | Warning | Filter on what the key condition decides
DYNA222 | DynaSharp.Commands | Info | Filter on what a key holds in part
DYNA223 | DynaSharp.Commands | Warning | Two actions on one path
DYNA224 | DynaSharp.Commands | Warning | Key of another attribute
DYNA225 | DynaSharp.Commands | Warning | Included entity has another key
DYNA226 | DynaSharp.Commands | Warning | Index key without all its inputs
DYNA227 | DynaSharp.Commands | Info | Value without Found
DYNA230 | DynaSharp.Commands | Error | Key that does not fit the table
DYNA231 | DynaSharp.Commands | Error | Type is not an entity
DYNA232 | DynaSharp.Commands | Error | Value of another type than its handle
DYNA233 | DynaSharp.Commands | Error | Add on a handle that is no number or set
DYNA234 | DynaSharp.Commands | Error | Method of the items called on a read
DYNA235 | DynaSharp.Commands | Error | IfVersion on a Put
DYNA236 | DynaSharp.Commands | Warning | Batch of an entity with a version
DYNA237 | DynaSharp.Commands | Warning | DateTime without a kind

## Release 0.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA036 | DynaSharp.Mapping | Error | [TimeToLive] is not valid here
DYNA123 | DynaSharp.Keys | Info | Raw table key input without [KeyOnly]

## Release 0.4.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA037 | DynaSharp.Mapping | Info | No IncludingAll() for the entities of this assembly
DYNA038 | DynaSharp.Mapping | Error | Type has a name the generated class DynamoDbEntities needs
