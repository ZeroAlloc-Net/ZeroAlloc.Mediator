using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Authorization.Generator;

internal sealed record PolicyModel(
    string Name,
    string FullyQualifiedTypeName,
    Location Location);

internal static class PolicyDiscovery
{
    private const string AuthorizationPolicyAttributeFqn = "ZeroAlloc.Authorization.AuthorizationPolicyAttribute";

    public static List<PolicyModel> Discover(Compilation compilation)
    {
        var results = new List<PolicyModel>();
        foreach (var type in EnumerateAllTypes(compilation.GlobalNamespace))
        {
            // Only types declared in the user's compilation can be DI-registered. References-side
            // policies would not be auto-registered, and runtime resolution falls back to whichever
            // assembly's DI registration ships them. Filter to in-source types only for v1.
            if (type.Locations.Length == 0 || !type.Locations[0].IsInSource) continue;

            foreach (var attr in type.GetAttributes())
            {
                var attrFqn = attr.AttributeClass?.ToDisplayString();
                if (attrFqn != AuthorizationPolicyAttributeFqn) continue;
                if (attr.ConstructorArguments.Length == 0) continue;
                var nameArg = attr.ConstructorArguments[0];
                if (nameArg.Value is not string name || string.IsNullOrEmpty(name)) continue;

                var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? type.Locations[0];
                results.Add(new PolicyModel(name, fqn, location));
            }
        }
        return results;
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateAllTypes(INamespaceSymbol root)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    stack.Push(ns);
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    // Recurse into nested types so [AuthorizationPolicy] on a nested class is found.
                    foreach (var nested in type.GetTypeMembers())
                    {
                        stack.Push(nested);
                    }
                }
            }
        }
    }
}
