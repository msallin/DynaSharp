; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA036 | DynaSharp.Mapping | Error | [TimeToLive] is not valid here
DYNA037 | DynaSharp.Mapping | Info | No IncludingAll() for the entities of this assembly
DYNA038 | DynaSharp.Mapping | Error | Type has a name the generated class DynamoDbEntities needs
DYNA123 | DynaSharp.Keys | Info | Raw table key input without [KeyOnly]
