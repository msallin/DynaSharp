; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DYNA041 | DynaSharp.Mapping | Error | Property is outside a nullable context
DYNA042 | DynaSharp.Mapping | Error | Constructor parameter is passed by reference
DYNA043 | DynaSharp.Mapping | Error | A generated class takes over a type name
DYNA239 | DynaSharp.Commands | Error | Write by key that does not say how it treats the version
