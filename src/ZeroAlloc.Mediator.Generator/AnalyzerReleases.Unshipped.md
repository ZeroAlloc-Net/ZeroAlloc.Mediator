; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category           | Severity | Notes
--------|--------------------|----------|--------------------------------------------------
ZAM010  | ZeroAlloc.Mediator | Error    | Cached request type has no per-instance cache key
ZAM011  | ZeroAlloc.Mediator | Error    | Cached record request has a member that prints as its type name
