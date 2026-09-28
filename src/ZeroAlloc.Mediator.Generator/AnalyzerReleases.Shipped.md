; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.4

### New Rules

Rule ID | Category           | Severity | Notes
--------|--------------------|----------|-----------------------------------
ZAM001  | ZeroAlloc.Mediator | Error    | No registered handler
ZAM002  | ZeroAlloc.Mediator | Error    | Duplicate request handler
ZAM003  | ZeroAlloc.Mediator | Warning  | Request type is a class
ZAM004  | ZeroAlloc.Mediator | Error    | Invalid handler signature
ZAM005  | ZeroAlloc.Mediator | Error    | Missing behavior Handle method
ZAM006  | ZeroAlloc.Mediator | Warning  | Duplicate behavior order
ZAM007  | ZeroAlloc.Mediator | Error    | Stream handler wrong return type

## Release 4.0.0

### New Rules

Rule ID | Category           | Severity | Notes
--------|--------------------|----------|-----------------------------------------
ZAM008  | ZeroAlloc.Mediator | Warning  | Handler has no parameterless constructor

## Release 6.0.0

### Removed Rules

Rule ID | Category           | Severity | Notes
--------|--------------------|----------|-----------------------------------
ZAM004  | ZeroAlloc.Mediator | Error    | Invalid handler signature
ZAM007  | ZeroAlloc.Mediator | Error    | Stream handler wrong return type
