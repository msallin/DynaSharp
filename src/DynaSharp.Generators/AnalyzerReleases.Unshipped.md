; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA044 | DynaSharp.Mapping | Error | Enum stored name is not one-to-one
DYNA045 | DynaSharp.Mapping | Error | Enum stored name is invalid
DYNA046 | DynaSharp.Mapping | Warning | [DynamoDbEnumName] has no effect
DYNA125 | DynaSharp.Keys | Error | Enum stored name does not fit the key
