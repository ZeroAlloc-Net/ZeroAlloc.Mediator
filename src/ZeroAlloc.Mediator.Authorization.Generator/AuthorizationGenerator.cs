using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Authorization.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class AuthorizationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compProvider = context.CompilationProvider;
        context.RegisterSourceOutput(compProvider, static (spc, comp) =>
        {
            var policies = PolicyDiscovery.Discover(comp);
            var requests = RequestDiscovery.Discover(comp);

            if (policies.Count == 0 && requests.Count == 0) return;

            var lookupSrc = LookupEmitter.Emit(policies, requests);
            spc.AddSource("GeneratedAuthorizationLookup.g.cs", lookupSrc);
        });
    }
}
