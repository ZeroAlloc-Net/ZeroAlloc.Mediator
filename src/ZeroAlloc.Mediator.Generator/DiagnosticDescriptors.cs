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

        // ZAM004: The C# compiler already enforces correct method signatures via interface implementation.
        public static readonly DiagnosticDescriptor InvalidHandlerSignature = new DiagnosticDescriptor(
            "ZAM004",
            "Invalid handler signature",
            "Handler '{0}' has an invalid Handle method signature for IRequestHandler<{1}, {2}>",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
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

        // ZAM007: The C# compiler already enforces correct return types via interface implementation.
        public static readonly DiagnosticDescriptor StreamHandlerWrongReturnType = new DiagnosticDescriptor(
            "ZAM007",
            "Stream handler wrong return type",
            "Stream handler '{0}' Handle method must return IAsyncEnumerable<{1}>",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HandlerMissingParameterlessConstructor =
            new DiagnosticDescriptor(
                "ZAM008",
                "Handler has no parameterless constructor",
                "Handler '{0}' has no parameterless constructor; static Mediator.Send/Publish/CreateStream will throw at runtime unless a factory is registered via Mediator.Configure(...) or services.AddMediator().RegisterHandlersFromAssembly(...). Inject IMediator instead for ASP.NET / hosted apps.",
                "ZeroAlloc.Mediator",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);
    }
}
