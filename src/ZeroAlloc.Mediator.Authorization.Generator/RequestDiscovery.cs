using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Mediator.Authorization.Generator;

internal sealed record RequestModel(
    string FullyQualifiedTypeName,
    string ResponseTypeFqn,
    bool IsResultPath,
    IReadOnlyList<string> PolicyNames,
    IReadOnlyList<Location> AuthorizeAttributeLocations);

internal static class RequestDiscovery
{
    private const string AuthorizeAttributeFqn = "ZeroAlloc.Authorization.AuthorizeAttribute";
    private const string IRequestFqn = "ZeroAlloc.Mediator.IRequest<TResponse>";
    private const string IAuthorizedRequestFqn = "ZeroAlloc.Mediator.Authorization.IAuthorizedRequest<TResponse>";
    private const string INotificationFqn = "ZeroAlloc.Mediator.INotification";

    public static List<RequestModel> Discover(Compilation compilation)
    {
        var results = new List<RequestModel>();
        foreach (var type in PolicyDiscovery.EnumerateAllTypes(compilation.GlobalNamespace))
        {
            if (type.Locations.Length == 0 || !type.Locations[0].IsInSource) continue;

            // Read [Authorize] attributes in source order. AttributeData has no source-order
            // guarantee across multiple AllowMultiple attributes, but ApplicationSyntaxReference
            // gives us the syntax span which we can sort by file+offset.
            var authorizeAttrs = type.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == AuthorizeAttributeFqn)
                .Select(a => new
                {
                    Attr = a,
                    Syntax = a.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax,
                })
                .Where(x => x.Syntax != null)
                .OrderBy(x => x.Syntax!.SyntaxTree.FilePath, System.StringComparer.Ordinal)
                .ThenBy(x => x.Syntax!.SpanStart)
                .ToList();

            if (authorizeAttrs.Count == 0) continue;

            // Determine request shape: walk AllInterfaces. IAuthorizedRequest<T> implies IRequest<Result<T,F>>,
            // so a type implementing IAuthorizedRequest<T> ALSO appears in IRequest<...>. We detect Result-path
            // when IAuthorizedRequest<> is in AllInterfaces.
            var isResultPath = false;
            string? responseTypeFqn = null;
            foreach (var iface in type.AllInterfaces)
            {
                var defFqn = iface.OriginalDefinition.ToDisplayString();
                if (defFqn == IAuthorizedRequestFqn && iface.TypeArguments.Length == 1)
                {
                    isResultPath = true;
                    responseTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    break;
                }
            }
            if (responseTypeFqn == null)
            {
                foreach (var iface in type.AllInterfaces)
                {
                    var defFqn = iface.OriginalDefinition.ToDisplayString();
                    if (defFqn == IRequestFqn && iface.TypeArguments.Length == 1)
                    {
                        responseTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        break;
                    }
                }
            }

            if (responseTypeFqn == null)
            {
                // Authorize on a non-IRequest/non-IAuthorizedRequest type: skip from emission;
                // diagnostic ZAMA004/ZAMA006 surfaces it.
                continue;
            }

            var policyNames = new List<string>();
            foreach (var x in authorizeAttrs)
            {
                if (x.Attr.ConstructorArguments.Length == 0) continue;
                if (x.Attr.ConstructorArguments[0].Value is not string n || string.IsNullOrEmpty(n)) continue;
                policyNames.Add(n);
            }

            if (policyNames.Count == 0) continue;

            var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var locations = authorizeAttrs.Select(x => x.Syntax!.GetLocation()).ToList();
            results.Add(new RequestModel(fqn, responseTypeFqn, isResultPath, policyNames, locations));
        }
        return results;
    }
}
