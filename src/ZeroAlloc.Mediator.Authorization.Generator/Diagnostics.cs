using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Authorization.Generator;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor UnknownPolicy = new(
        id: "ZAMA001",
        title: "Unknown authorization policy",
        messageFormat: "[Authorize(\"{0}\")] references a policy with no [AuthorizationPolicy(\"{0}\")] declaration in the compilation",
        category: "ZeroAlloc.Mediator.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicatePolicyName = new(
        id: "ZAMA002",
        title: "Duplicate authorization policy name",
        messageFormat: "Policy name '{0}' is declared by more than one [AuthorizationPolicy] type ({1})",
        category: "ZeroAlloc.Mediator.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AuthorizedRequestWithoutAuthorize = new(
        id: "ZAMA003",
        title: "IAuthorizedRequest<T> without [Authorize]",
        messageFormat: "Type '{0}' implements IAuthorizedRequest<T> but has no [Authorize] attribute; the Result-shape opt-in has no policies to evaluate",
        category: "ZeroAlloc.Mediator.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AuthorizeOnNonRequest = new(
        id: "ZAMA004",
        title: "[Authorize] on non-IRequest type",
        messageFormat: "Type '{0}' has [Authorize] but does not implement IRequest<T> or IAuthorizedRequest<T>",
        category: "ZeroAlloc.Mediator.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedAttributeProperty = new(
        id: "ZAMA005",
        title: "Unsupported [Authorize] property",
        messageFormat: "[Authorize] uses named property '{0}' which this version of ZeroAlloc.Mediator.Authorization does not understand; upgrade the host package to a version that supports it",
        category: "ZeroAlloc.Mediator.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AuthorizeOnNotification = new(
        id: "ZAMA006",
        title: "[Authorize] on INotification",
        messageFormat: "Type '{0}' is a notification (INotification); [Authorize] on notifications is not supported in v1",
        category: "ZeroAlloc.Mediator.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
