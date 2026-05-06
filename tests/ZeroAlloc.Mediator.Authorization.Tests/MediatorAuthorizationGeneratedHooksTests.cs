using System;
using ZeroAlloc.Mediator.Authorization;

namespace ZeroAlloc.Mediator.Authorization.Tests;

/// <summary>
/// Tests around <see cref="MediatorAuthorizationGeneratedHooks"/>'s first-write-wins
/// semantics. The first <c>Configure(...)</c> call comes from the generator-emitted
/// <c>[ModuleInitializer]</c> at test-assembly load time, so by the time these tests
/// run the hooks are already wired. Subsequent <c>Configure(...)</c> calls must be
/// no-ops to prevent user code (or a second loaded compilation's initializer) from
/// clobbering the runtime's view.
/// </summary>
public sealed class MediatorAuthorizationGeneratedHooksTests
{
    [Fact]
    public void Configure_FirstWriteWins_SubsequentCallsAreNoOps()
    {
        // The first call comes from the generator-emitted ModuleInitializer at process start.
        // Capture the currently-installed delegate so we can confirm it survives a second Configure.
        var originalGetPolicies = MediatorAuthorizationGeneratedHooks.GetPoliciesForRequestType;

        // Try to replace it with a marker delegate. If first-write-wins works, this is a no-op.
        MediatorAuthorizationGeneratedHooks.Configure(
            registerDiscoveredPolicies: null,
            getPolicyTypes: null,
            getPoliciesForRequestType: static _ => new[] { "MARKER" },
            resolvePolicy: null);

        // The original delegate must still be in place; the second Configure was a no-op.
        Assert.Same(originalGetPolicies, MediatorAuthorizationGeneratedHooks.GetPoliciesForRequestType);
    }

    [Fact]
    public void Configure_RejectsNullGetPoliciesForRequestType()
    {
        // ArgumentNullException is checked BEFORE the first-write-wins guard so misuse from
        // the ModuleInitializer surfaces fast rather than silently no-op'ing.
        Assert.Throws<ArgumentNullException>(() =>
            MediatorAuthorizationGeneratedHooks.Configure(
                registerDiscoveredPolicies: null,
                getPolicyTypes: null,
                getPoliciesForRequestType: null!,
                resolvePolicy: null));
    }
}
