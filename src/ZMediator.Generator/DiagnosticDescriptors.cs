#nullable enable
using Microsoft.CodeAnalysis;

namespace ZMediator.Generator
{
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor NoHandler = new DiagnosticDescriptor(
            "ZM001",
            "No registered handler",
            "Request type '{0}' has no registered IRequestHandler",
            "ZMediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHandler = new DiagnosticDescriptor(
            "ZM002",
            "Duplicate request handler",
            "Request type '{0}' has multiple handlers: {1}",
            "ZMediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ClassRequest = new DiagnosticDescriptor(
            "ZM003",
            "Request type is a class",
            "Request type '{0}' is a class; use 'readonly record struct' for zero-allocation dispatch",
            "ZMediator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ZM004: The C# compiler already enforces correct method signatures via interface implementation.
        public static readonly DiagnosticDescriptor InvalidHandlerSignature = new DiagnosticDescriptor(
            "ZM004",
            "Invalid handler signature",
            "Handler '{0}' has an invalid Handle method signature for IRequestHandler<{1}, {2}>",
            "ZMediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingBehaviorHandleMethod = new DiagnosticDescriptor(
            "ZM005",
            "Missing behavior Handle method",
            "Pipeline behavior '{0}' is missing a public static Handle<TRequest, TResponse> method",
            "ZMediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateBehaviorOrder = new DiagnosticDescriptor(
            "ZM006",
            "Duplicate behavior order",
            "Pipeline behaviors {0} have the same Order value {1}; execution order is ambiguous",
            "ZMediator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ZM007: The C# compiler already enforces correct return types via interface implementation.
        public static readonly DiagnosticDescriptor StreamHandlerWrongReturnType = new DiagnosticDescriptor(
            "ZM007",
            "Stream handler wrong return type",
            "Stream handler '{0}' Handle method must return IAsyncEnumerable<{1}>",
            "ZMediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
