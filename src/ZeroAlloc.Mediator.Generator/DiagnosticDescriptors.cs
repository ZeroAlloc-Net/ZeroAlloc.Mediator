#nullable enable
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Generator
{
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor NoHandler = new DiagnosticDescriptor(
            "ZAM001",
            "No registered handler",
            "Request type '{0}' has no registered IRequestHandler",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHandler = new DiagnosticDescriptor(
            "ZAM002",
            "Duplicate request handler",
            "Request type '{0}' has multiple handlers: {1}",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ClassRequest = new DiagnosticDescriptor(
            "ZAM003",
            "Request type is a class",
            "Request type '{0}' is a class; use 'readonly record struct' for zero-allocation dispatch",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingBehaviorHandleMethod = new DiagnosticDescriptor(
            "ZAM005",
            "Missing behavior Handle method",
            "Pipeline behavior '{0}' is missing a public static Handle<TRequest, TResponse> method",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateBehaviorOrder = new DiagnosticDescriptor(
            "ZAM006",
            "Duplicate behavior order",
            "Pipeline behaviors {0} have the same Order value {1}; execution order is ambiguous",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HandlerMissingParameterlessConstructor =
            new DiagnosticDescriptor(
                "ZAM008",
                "Handler has no parameterless constructor",
                "Handler '{0}' has no parameterless constructor; static Mediator.Send/Publish/CreateStream will throw at runtime unless a factory is registered via Mediator.Configure(...) or services.AddMediator(). Inject IMediator instead for ASP.NET / hosted apps.",
                "ZeroAlloc.Mediator",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

        // A Warning, not an Error: such a type compiled without a diagnostic before ZAM009.
        public static readonly DiagnosticDescriptor BehaviorMissingPipelineBehaviorInterface =
            new DiagnosticDescriptor(
                "ZAM009",
                "Pipeline behavior does not implement IPipelineBehavior",
                "Pipeline behavior '{0}' does not implement IPipelineBehavior and never runs; {1}",
                "ZeroAlloc.Mediator",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);
    
        // An Error: every instance of such a type shares one cache entry, so all but the first
        // request get another request's response. A type without instance state is not reported.
        public static readonly DiagnosticDescriptor CachedRequestWithoutValueKey =
            new DiagnosticDescriptor(
                "ZAM010",
                "Cached request type has no per-instance cache key",
                "Request type '{0}' has [CacheResponse], but its cache key is its type name, so every instance shares one cache entry and gets the first response; make it a record, override ToString, or implement ISpanFormattable so the key identifies the request",
                "ZeroAlloc.Mediator",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);

        // An Error, like ZAM010: requests that differ only in such a member share one cache entry,
        // so all but the first get another request's response.
        public static readonly DiagnosticDescriptor CachedRecordMemberWithoutValueText =
            new DiagnosticDescriptor(
                "ZAM011",
                "Cached record request has a member that prints as its type name",
                "Member '{0}' of [CacheResponse] request '{1}' has type '{2}', which the record's ToString prints as its type name, so requests that differ only in it share one cache entry and get the first response; override ToString or PrintMembers on the request to format it, or use a type whose ToString prints its value",
                "ZeroAlloc.Mediator",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);
    }
}
