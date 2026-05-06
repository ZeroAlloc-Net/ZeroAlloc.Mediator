using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Mediator.Authorization.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class AuthorizationGenerator : IIncrementalGenerator
{
    private const string AuthorizeAttributeFqn = "ZeroAlloc.Authorization.AuthorizeAttribute";
    private const string IRequestFqn = "ZeroAlloc.Mediator.IRequest<TResponse>";
    private const string IAuthorizedRequestFqn = "ZeroAlloc.Mediator.Authorization.IAuthorizedRequest<TResponse>";
    private const string INotificationFqn = "ZeroAlloc.Mediator.INotification";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compProvider = context.CompilationProvider;
        context.RegisterSourceOutput(compProvider, static (spc, comp) =>
        {
            var policies = PolicyDiscovery.Discover(comp);
            var requests = RequestDiscovery.Discover(comp);

            ReportDiagnostics(spc, comp, policies);

            if (policies.Count == 0 && requests.Count == 0) return;

            var lookupSrc = LookupEmitter.Emit(policies, requests);
            spc.AddSource("GeneratedAuthorizationLookup.g.cs", lookupSrc);

            var emitDI = policies.Count > 0;
            if (emitDI)
            {
                var diSrc = LookupEmitter.EmitDIExtensions(policies);
                spc.AddSource("GeneratedAuthorizationDIExtensions.g.cs", diSrc);
            }

            // Emit a [ModuleInitializer] that wires the generated lookup/DI methods into
            // the runtime's static delegate hooks. This is the only way the runtime
            // sub-package can reach the user-compilation-local generated code.
            var initSrc = LookupEmitter.EmitModuleInitializer(emitLookup: true, emitDI: emitDI);
            spc.AddSource("GeneratedAuthorizationModuleInitializer.g.cs", initSrc);
        });
    }

    private static void ReportDiagnostics(
        SourceProductionContext spc,
        Compilation compilation,
        IReadOnlyList<PolicyModel> policies)
    {
        // ZAMA002: duplicate [AuthorizationPolicy] names. Report once per name.
        var byName = policies.GroupBy(p => p.Name, System.StringComparer.Ordinal);
        foreach (var group in byName)
        {
            var members = group.ToList();
            if (members.Count <= 1) continue;
            var typeNames = string.Join(", ", members.Select(m => m.FullyQualifiedTypeName));
            // Report at each duplicate's site so the user can navigate to all.
            foreach (var p in members)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DuplicatePolicyName,
                    p.Location,
                    group.Key, typeNames));
            }
        }

        var declaredPolicyNames = new HashSet<string>(policies.Select(p => p.Name), System.StringComparer.Ordinal);

        // Walk types once and inspect [Authorize] attributes for ZAMA001/ZAMA003/ZAMA004/ZAMA005/ZAMA006.
        foreach (var type in PolicyDiscovery.EnumerateAllTypes(compilation.GlobalNamespace))
        {
            if (type.Locations.Length == 0 || !type.Locations[0].IsInSource) continue;

            var authorizeAttrs = type.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == AuthorizeAttributeFqn)
                .ToList();

            // ZAMA003: IAuthorizedRequest<T> without any [Authorize].
            var implementsAuthorizedRequest = type.AllInterfaces.Any(
                i => i.OriginalDefinition.ToDisplayString() == IAuthorizedRequestFqn);
            if (implementsAuthorizedRequest && authorizeAttrs.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.AuthorizedRequestWithoutAuthorize,
                    type.Locations[0],
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            if (authorizeAttrs.Count == 0) continue;

            // Pre-compute interface flags once per type.
            var implementsRequest = type.AllInterfaces.Any(
                i => i.OriginalDefinition.ToDisplayString() == IRequestFqn);
            var implementsNotification = type.AllInterfaces.Any(
                i => i.ToDisplayString() == INotificationFqn);

            foreach (var attr in authorizeAttrs)
            {
                var attrSyntax = attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
                var attrLocation = attrSyntax?.GetLocation() ?? type.Locations[0];

                // ZAMA006: notification — no need to also fire ZAMA004; notification is more specific.
                if (implementsNotification)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.AuthorizeOnNotification,
                        attrLocation,
                        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    continue;
                }

                // ZAMA004: [Authorize] on a type that is neither IRequest<> nor a notification.
                if (!implementsRequest)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.AuthorizeOnNonRequest,
                        attrLocation,
                        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    continue;
                }

                // ZAMA005: detect ANY named argument on the attribute. The v1 [Authorize] attribute
                // only knows the positional policy-name; a named arg means the contract has shipped
                // a future property (Mode, etc.) that this generator predates.
                if (attrSyntax?.ArgumentList != null)
                {
                    foreach (var arg in attrSyntax.ArgumentList.Arguments)
                    {
                        if (arg.NameEquals != null)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                Diagnostics.UnsupportedAttributeProperty,
                                arg.GetLocation(),
                                arg.NameEquals.Name.Identifier.Text));
                        }
                    }
                }

                // ZAMA001: unknown policy name.
                if (attr.ConstructorArguments.Length > 0
                    && attr.ConstructorArguments[0].Value is string name
                    && !string.IsNullOrEmpty(name)
                    && !declaredPolicyNames.Contains(name))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.UnknownPolicy,
                        attrLocation,
                        name));
                }
            }
        }
    }
}
