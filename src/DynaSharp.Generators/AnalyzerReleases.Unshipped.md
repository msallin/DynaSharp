; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA026 | DynaSharp.Mapping | Warning | Two entities share a discriminator
DYNA029 | DynaSharp.Mapping | Error | [DynamoDbDefaultConverter] is not valid
DYNA030 | DynaSharp.Mapping | Error | [OmitEmpty] is not valid here
DYNA031 | DynaSharp.Mapping | Warning | Entity stored inside an entity
DYNA032 | DynaSharp.Mapping | Warning | Getter reads a property that [ComputedFrom] does not name
DYNA116 | DynaSharp.Keys | Warning | Truncated timestamp at the end of a table key
DYNA117 | DynaSharp.Keys | Warning | Key property starts empty
DYNA118 | DynaSharp.Keys | Error | Raw value does not end the template
DYNA119 | DynaSharp.Keys | Error | [ComputedFrom] is not valid here
DYNA120 | DynaSharp.Keys | Warning | Raw value is strict in another template
DYNA121 | DynaSharp.Keys | Info | Raw value reaches a strict template through a getter
DYNA122 | DynaSharp.Keys | Warning | Constant that no key can hold
DYNA203 | DynaSharp.Commands | Error | Command is discarded
DYNA204 | DynaSharp.Commands | Warning | Command is used twice
DYNA205 | DynaSharp.Commands | Warning | Write to a key of the table
DYNA206 | DynaSharp.Commands | Warning | IfNotExists on a Put with a version
DYNA207 | DynaSharp.Commands | Warning | Index is not declared
DYNA208 | DynaSharp.Commands | Warning | Project leaves out a required member
DYNA209 | DynaSharp.Commands | Warning | Select of an attribute that a read of the entity can do without
DYNA215 | DynaSharp.Expressions | Warning | Handle of another type
DYNA216 | DynaSharp.Expressions | Warning | Text function on a number
DYNA217 | DynaSharp.Commands | Warning | Nested RetryOnConflictAsync
DYNA218 | DynaSharp.Commands | Warning | Version read before the retry
DYNA219 | DynaSharp.Commands | Warning | Write to what a key of an index is computed from
