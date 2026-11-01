; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA026 | DynaSharp.Mapping | Warning | Two entities share a discriminator
DYNA030 | DynaSharp.Mapping | Error | [OmitEmpty] is not valid here
DYNA116 | DynaSharp.Keys | Warning | Truncated timestamp at the end of a table key
DYNA117 | DynaSharp.Keys | Info | Key property starts empty
DYNA203 | DynaSharp.Commands | Error | Command is discarded
DYNA204 | DynaSharp.Commands | Warning | Command is used twice
DYNA205 | DynaSharp.Commands | Warning | Write to a key of the table
DYNA206 | DynaSharp.Commands | Warning | IfNotExists on a Put with a version
DYNA207 | DynaSharp.Commands | Warning | Index is not declared
DYNA215 | DynaSharp.Expressions | Warning | Handle of another type
DYNA216 | DynaSharp.Expressions | Warning | Text function on a number
