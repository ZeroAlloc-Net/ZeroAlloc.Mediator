# Generated Handler Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The generated `AddMediator()` registers every handler the generator knows, so the DI path is trim- and AOT-safe without `RegisterHandlersFromAssembly`. That scanner becomes `[Obsolete]`.

**Architecture:**
- The three handler info models (request, notification, stream) gain the lifetime from `[HandlerLifetime]`, read at compile time.
- `GenerateServiceCollectionExtensions` emits `AddMediator(ServiceLifetime defaultHandlerLifetime)`. It registers each distinct handler type with `TryAdd`, as its concrete type, which `MediatorService` already resolves.
- `AddMediator()` forwards to it with Transient.

**Tech Stack:** a C# incremental source generator (netstandard2.0), Microsoft.Extensions.DependencyInjection, and xUnit tests in `tests/ZeroAlloc.Mediator.Tests`.

**Spec:** `docs/plans/2026-10-09-generated-handler-registration-design.md`. Issue #275.

## Global Constraints

- The generated `AddMediator` overloads stay `internal`, in the existing `internal static partial class MediatorServiceCollectionExtensions` in namespace `Microsoft.Extensions.DependencyInjection`.
- Registrations use `TryAdd` with the concrete handler type as both the service type and the implementation type.
- The lifetime is `[HandlerLifetime]` when present, otherwise the `defaultHandlerLifetime` parameter. `AddMediator()` passes `ServiceLifetime.Transient`.
- Each handler type is registered once, even if it implements several handler interfaces. Emit them in ordinal order of the fully qualified type name.
- The obsolete message is exactly: `AddMediator() now registers every handler in this assembly at compile time; use AddMediator(ServiceLifetime) to change the default lifetime. This reflection-based scanner is not trim- or AOT-safe and will be removed in the next major version.` It is a warning, not an error.
- The repo builds with TreatWarningsAsErrors. Every remaining use of the obsolete scanner must either move to `AddMediator()` or suppress CS0618 locally.
- Keep each file's existing line endings. Emitted generator strings use `\r\n`, matching the existing code.
- Commits:
  - Use a `feat:` header for the generator and runtime change, and non-release headers for the rest.
  - Body lines must be 100 characters or fewer, with no nested parentheses.
  - End each commit with a `Co-Authored-By:` trailer naming the model that wrote it. Do not add session links.
- Test command: `dotnet test tests/ZeroAlloc.Mediator.Tests -c Release`. Build the whole solution with `dotnet build ZeroAlloc.Mediator.slnx -c Release`.

---

### Task 1: The generator registers handlers in AddMediator

**Files:**
- Modify:
  - `src/ZeroAlloc.Mediator.Generator/RequestHandlerInfo.cs`
  - `src/ZeroAlloc.Mediator.Generator/NotificationHandlerInfo.cs`
  - `src/ZeroAlloc.Mediator.Generator/StreamHandlerInfo.cs`
  - `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs`: the handler extraction at about lines 205–370, the source output at about line 155, and `GenerateServiceCollectionExtensions` at about line 561
- Create: `tests/ZeroAlloc.Mediator.Tests/GeneratorTests/HandlerRegistrationGeneratorTests.cs`

**Interfaces:**
- Produces:
  - `int? Lifetime` on all three info types. The value is the `ServiceLifetime` enum value from `[HandlerLifetime]` (0 Singleton, 1 Scoped, 2 Transient), or null when the attribute is absent.
  - The generated `AddMediator(this IServiceCollection services, ServiceLifetime defaultHandlerLifetime)`.

- [ ] **Step 1: Write the failing generator tests**

Create `tests/ZeroAlloc.Mediator.Tests/GeneratorTests/HandlerRegistrationGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

public class HandlerRegistrationGeneratorTests
{
    private const string Header = """
        using ZeroAlloc.Mediator;
        using Microsoft.Extensions.DependencyInjection;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        """;

    private static string Registration(string handler, string lifetime) =>
        $"services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({handler}), typeof({handler}), {lifetime}));";

    private static string Run(string body)
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(Header + body);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return output;
    }

    [Fact]
    public void AddMediator_RegistersEachHandlerKind_WithTheDefaultLifetime()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }

            public readonly record struct Happened(int X) : INotification;
            public class HappenedHandler : INotificationHandler<Happened>
            {
                public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
            }

            public readonly record struct Count(int To) : IStreamRequest<int>;
            public class CountHandler : IStreamRequestHandler<Count, int>
            {
                public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }
            }
            """);

        Assert.Contains(Registration("global::TestApp.PingHandler", "defaultHandlerLifetime"), output);
        Assert.Contains(Registration("global::TestApp.HappenedHandler", "defaultHandlerLifetime"), output);
        Assert.Contains(Registration("global::TestApp.CountHandler", "defaultHandlerLifetime"), output);
    }

    [Fact]
    public void AddMediator_ParameterlessOverload_ForwardsWithTransient()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains("AddMediator(services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)", output);
        Assert.Contains("global::Microsoft.Extensions.DependencyInjection.ServiceLifetime defaultHandlerLifetime)", output);
    }

    [Theory]
    [InlineData("Singleton")]
    [InlineData("Scoped")]
    [InlineData("Transient")]
    public void AddMediator_UsesHandlerLifetimeAttribute_OverTheDefault(string lifetime)
    {
        var output = Run($$"""
            public readonly record struct Ping(int X) : IRequest<int>;
            [HandlerLifetime(ServiceLifetime.{{lifetime}})]
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(
            Registration("global::TestApp.PingHandler", $"global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime}"),
            output);
    }

    [Fact]
    public void AddMediator_RegistersInternalHandlers()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            internal class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(Registration("global::TestApp.PingHandler", "defaultHandlerLifetime"), output);
    }

    [Fact]
    public void AddMediator_RegistersAClassHandlingSeveralMessagesOnce_InOrdinalOrder()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public readonly record struct Happened(int X) : INotification;
            public class Zeta : IRequestHandler<Ping, int>, INotificationHandler<Happened>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
                public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
            }

            public readonly record struct Other(int X) : IRequest<int>;
            public class Alpha : IRequestHandler<Other, int>
            {
                public ValueTask<int> Handle(Other request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        var zeta = Registration("global::TestApp.Zeta", "defaultHandlerLifetime");
        var alpha = Registration("global::TestApp.Alpha", "defaultHandlerLifetime");
        Assert.Equal(output.IndexOf(zeta, StringComparison.Ordinal), output.LastIndexOf(zeta, StringComparison.Ordinal));
        Assert.True(output.IndexOf(alpha, StringComparison.Ordinal) < output.IndexOf(zeta, StringComparison.Ordinal));
    }

    [Fact]
    public void AddMediator_WithoutHandlers_RegistersOnlyIMediator()
    {
        var output = Run("""
            public readonly record struct Happened(int X) : INotification;
            """);

        Assert.Contains("services.TryAddTransient<global::ZeroAlloc.Mediator.IMediator, global::ZeroAlloc.Mediator.MediatorService>();", output);
        Assert.DoesNotContain("services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor", output);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Mediator.Tests -c Release --filter "FullyQualifiedName~HandlerRegistrationGeneratorTests"`
Expected: FAIL. The registration strings and the lifetime overload are not emitted yet.

- [ ] **Step 3: Add `Lifetime` to the three info models**

For each of `RequestHandlerInfo`, `NotificationHandlerInfo` and `StreamHandlerInfo`:
- Add the property `public int? Lifetime { get; }`, with an XML doc: "The ServiceLifetime value from [HandlerLifetime] on the handler class, or null when the attribute is absent."
- Add a trailing constructor parameter `int? lifetime` and assign it.
- Include it in `Equals`: `&& Lifetime == other.Lifetime`.
- Include it in `GetHashCode`: `hash = hash * 31 + (Lifetime ?? -1);`.
- Pass it through in `WithoutLocation()`. For example, in `RequestHandlerInfo` that becomes `new RequestHandlerInfo(RequestTypeName, ResponseTypeName, HandlerTypeName, IsRequestValueType, HasParameterlessConstructor, null, Lifetime)`.

- [ ] **Step 4: Read the lifetime at extraction**

In `MediatorGenerator.cs`, add next to `IsAccessible`:

```csharp
        /// <summary>
        /// The ServiceLifetime value of [HandlerLifetime] on <paramref name="handler"/>, or null.
        /// Read from the attribute's constant argument, so registration needs no reflection.
        /// </summary>
        private static int? GetHandlerLifetime(INamedTypeSymbol handler)
        {
            foreach (var attribute in handler.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == "ZeroAlloc.Mediator.HandlerLifetimeAttribute"
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is int lifetime)
                {
                    return lifetime;
                }
            }

            return null;
        }
```

Pass `GetHandlerLifetime(symbol)` as the new last argument at the three `new ...HandlerInfo(` call sites. These are at about lines 231, 280 and 367.

- [ ] **Step 5: Emit the registrations**

Add this helper next to `GenerateServiceCollectionExtensions`:

```csharp
        /// <summary>
        /// Every handler type across the three kinds once, in ordinal order, with its
        /// [HandlerLifetime] value or null.
        /// </summary>
        private static List<KeyValuePair<string, int?>> CollectHandlerRegistrations(
            ImmutableArray<RequestHandlerInfo?> requestHandlers,
            ImmutableArray<NotificationHandlerInfo?> notificationHandlers,
            ImmutableArray<StreamHandlerInfo?> streamHandlers)
        {
            var byType = new SortedDictionary<string, int?>(StringComparer.Ordinal);
            foreach (var h in requestHandlers)
                if (h != null && !byType.ContainsKey(h.HandlerTypeName)) byType[h.HandlerTypeName] = h.Lifetime;
            foreach (var h in notificationHandlers)
                if (h != null && !byType.ContainsKey(h.HandlerTypeName)) byType[h.HandlerTypeName] = h.Lifetime;
            foreach (var h in streamHandlers)
                if (h != null && !byType.ContainsKey(h.HandlerTypeName)) byType[h.HandlerTypeName] = h.Lifetime;
            return byType.ToList();
        }

        private static string LifetimeExpression(int? lifetime) => lifetime switch
        {
            0 => "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton",
            1 => "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped",
            2 => "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient",
            _ => "defaultHandlerLifetime",
        };
```

Change `GenerateServiceCollectionExtensions(bool activateBehaviorState)` to `GenerateServiceCollectionExtensions(bool activateBehaviorState, List<KeyValuePair<string, int?>> handlers)`. Build the registrations:

```csharp
            var registrations = new StringBuilder();
            foreach (var handler in handlers)
            {
                registrations
                    .Append("            services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(")
                    .Append(handler.Key).Append("), typeof(").Append(handler.Key).Append("), ")
                    .Append(LifetimeExpression(handler.Value)).Append("));\r\n");
            }
```

Then replace the emitted method (the `/// <summary>` block through the closing `}` of `AddMediator`) with two overloads. Keep the existing `<remarks>` paragraph about the static dispatcher, but drop "Calling AddMediator() is optional; it only helps users who want to inject IMediator".

```csharp
                "        /// <summary>\r\n" +
                "        /// Registers <see cref=\"global::ZeroAlloc.Mediator.IMediator\"/> and every request, notification\r\n" +
                "        /// and stream handler in this assembly, each as its concrete type with a transient lifetime\r\n" +
                "        /// unless it carries <c>[HandlerLifetime]</c>. Returns an\r\n" +
                "        /// <see cref=\"global::ZeroAlloc.Mediator.IMediatorBuilder\"/> for chaining bridge-package\r\n" +
                "        /// registrations (<c>WithCache()</c>, <c>WithValidation()</c>, <c>WithResilience()</c>, etc.).\r\n" +
                "        /// </summary>\r\n" +
                "        /// <remarks>\r\n" +
                "        /// Registration is generated at compile time, so it is trim- and AOT-safe. Handlers are\r\n" +
                "        /// added with TryAdd, so a handler you registered yourself keeps your registration.\r\n" +
                "        /// The static <c>ZeroAlloc.Mediator.Mediator</c> dispatcher API is unaffected.\r\n" +
                "        /// </remarks>\r\n" +
                "        internal static global::ZeroAlloc.Mediator.IMediatorBuilder AddMediator(\r\n" +
                "            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)\r\n" +
                "            => AddMediator(services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient);\r\n" +
                "\r\n" +
                "        /// <summary>\r\n" +
                "        /// Same as <c>AddMediator()</c>, with <paramref name=\"defaultHandlerLifetime\"/> for handlers\r\n" +
                "        /// that do not carry <c>[HandlerLifetime]</c>.\r\n" +
                "        /// </summary>\r\n" +
                "        internal static global::ZeroAlloc.Mediator.IMediatorBuilder AddMediator(\r\n" +
                "            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,\r\n" +
                "            global::Microsoft.Extensions.DependencyInjection.ServiceLifetime defaultHandlerLifetime)\r\n" +
                "        {\r\n" +
                "            services.TryAddTransient<global::ZeroAlloc.Mediator.IMediator, global::ZeroAlloc.Mediator.MediatorService>();\r\n" +
                activationRegistration +
                registrations +
                "            return new global::ZeroAlloc.Mediator.MediatorBuilder(services);\r\n" +
                "        }\r\n" +
```

In the source output callback (about line 169), replace the `diSource` line with:

```csharp
                var handlerRegistrations = CollectHandlerRegistrations(requestInfos, notificationInfos, streamInfos);
                var diSource = GenerateServiceCollectionExtensions(activateBehaviorState, handlerRegistrations);
```

- [ ] **Step 6: Run the generator tests**

Run: `dotnet test tests/ZeroAlloc.Mediator.Tests -c Release --filter "FullyQualifiedName~GeneratorTests"`
Expected: the new tests pass, and existing generator tests, including `IncrementalityTests`, still pass. A failing existing assertion on the old `AddMediator` text, if one exists, is updated to the new text; record each such change in your report.

- [ ] **Step 7: Commit**

```bash
git add src/ZeroAlloc.Mediator.Generator tests/ZeroAlloc.Mediator.Tests/GeneratorTests
git commit -m "feat: register handlers from the generated AddMediator" -m "AddMediator now adds every request, notification and stream handler in the assembly, each as its
concrete type with TryAdd. HandlerLifetime wins over the default, which AddMediator(ServiceLifetime)
sets and AddMediator() leaves at Transient. Registration is compile time, so it is AOT-safe." -m "Refs #275" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 2: Runtime behaviour and messages

**Files:**
- Create: `tests/ZeroAlloc.Mediator.Tests/IntegrationTests/GeneratedRegistrationIntegrationTests.cs`
- Modify:
  - `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs`: the messages at about lines 1112 and 1338
  - `src/ZeroAlloc.Mediator.Generator/DiagnosticDescriptors.cs` line 52
  - `tests/ZeroAlloc.Mediator.Tests/IntegrationTests/RequestIntegrationTests.cs`, `NotificationIntegrationTests.cs`, `StreamIntegrationTests.cs` and `InterfaceRegisteredNotificationIntegrationTests.cs`

**Interfaces:**
- Consumes: the generated `AddMediator()` and `AddMediator(ServiceLifetime)` from Task 1.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/ZeroAlloc.Mediator.Tests/IntegrationTests/GeneratedRegistrationIntegrationTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public readonly record struct RegLifetimeProbe(int X) : IRequest<RegLifetimeProbeHandler>;

public class RegLifetimeProbeHandler : IRequestHandler<RegLifetimeProbe, RegLifetimeProbeHandler>
{
    public ValueTask<RegLifetimeProbeHandler> Handle(RegLifetimeProbe request, CancellationToken ct)
        => ValueTask.FromResult(this);
}

public readonly record struct RegSingletonProbe(int X) : IRequest<RegSingletonProbeHandler>;

[HandlerLifetime(ServiceLifetime.Singleton)]
public class RegSingletonProbeHandler : IRequestHandler<RegSingletonProbe, RegSingletonProbeHandler>
{
    public ValueTask<RegSingletonProbeHandler> Handle(RegSingletonProbe request, CancellationToken ct)
        => ValueTask.FromResult(this);
}

public readonly record struct RegInternalPing(int X) : IRequest<int>;

internal class RegInternalPingHandler : IRequestHandler<RegInternalPing, int>
{
    public ValueTask<int> Handle(RegInternalPing request, CancellationToken ct) => ValueTask.FromResult(request.X + 1);
}

public class GeneratedRegistrationIntegrationTests
{
    [Fact]
    public async Task AddMediator_Alone_ResolvesHandlers()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();

        var result = await sp.GetRequiredService<IMediator>().Send(new IntegrationPing("x"), CancellationToken.None);

        Assert.Equal("Pong: x", result);
    }

    [Fact]
    public async Task AddMediator_Alone_ResolvesInternalHandlers()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();

        Assert.Equal(2, await sp.GetRequiredService<IMediator>().Send(new RegInternalPing(1), CancellationToken.None));
    }

    [Fact]
    public async Task AddMediator_DefaultLifetime_IsTransient()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var first = await mediator.Send(new RegLifetimeProbe(1), CancellationToken.None);
        var second = await mediator.Send(new RegLifetimeProbe(2), CancellationToken.None);

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task AddMediator_WithScopedDefault_SharesWithinAScope_NotAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddMediator(ServiceLifetime.Scoped);
        using var sp = services.BuildServiceProvider();

        RegLifetimeProbeHandler a1, a2, b1;
        using (var scope = sp.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            a1 = await mediator.Send(new RegLifetimeProbe(1), CancellationToken.None);
            a2 = await mediator.Send(new RegLifetimeProbe(2), CancellationToken.None);
        }
        using (var scope = sp.CreateScope())
        {
            b1 = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegLifetimeProbe(3), CancellationToken.None);
        }

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b1);
    }

    [Fact]
    public async Task HandlerLifetimeAttribute_WinsOverTheDefault()
    {
        var services = new ServiceCollection();
        services.AddMediator(ServiceLifetime.Transient);
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        Assert.Same(
            await mediator.Send(new RegSingletonProbe(1), CancellationToken.None),
            await mediator.Send(new RegSingletonProbe(2), CancellationToken.None));
    }

    [Fact]
    public async Task CallerRegistration_BeforeAddMediator_IsKept()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RegLifetimeProbeHandler>();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        Assert.Same(
            await mediator.Send(new RegLifetimeProbe(1), CancellationToken.None),
            await mediator.Send(new RegLifetimeProbe(2), CancellationToken.None));
        Assert.Single(services, d => d.ServiceType == typeof(RegLifetimeProbeHandler));
    }
}
```

- [ ] **Step 2: Run them to verify the new behaviour**

Run: `dotnet test tests/ZeroAlloc.Mediator.Tests -c Release --filter "FullyQualifiedName~GeneratedRegistrationIntegrationTests"`
Expected: PASS, because Task 1 already generates the registrations. The real RED for this behaviour was Task 1's generator tests. Record this run as the confirmation in your report.

- [ ] **Step 3: Point the messages at AddMediator**

- `MediatorGenerator.cs`, the notification message at about line 1112: replace `Register them with services.AddMediator().RegisterHandlersFromAssembly(...), or register INotificationHandler<{0}> implementations directly.` with `Register them with services.AddMediator(), or register INotificationHandler<{0}> implementations directly.`
- `MediatorGenerator.cs`, the static-path message at about line 1338: replace `Inject IMediator (services.AddMediator().RegisterHandlersFromAssembly(...))` with `Inject IMediator (services.AddMediator())`.
- `DiagnosticDescriptors.cs:52`: replace `or services.AddMediator().RegisterHandlersFromAssembly(...)` with `or services.AddMediator()`.
- Update `RequestIntegrationTests.StaticSend_NoFactory_NoParameterlessCtor_Throws` and the generator test at `RequestDispatchGeneratorTests.cs:217` to assert `AddMediator` instead of `RegisterHandlersFromAssembly`.

- [ ] **Step 4: Keep the "known handler registered nowhere" path covered**

`InterfaceRegisteredNotificationIntegrationTests.Publish_StillThrows_WhenAKnownHandlerIsRegisteredNowhere` used `services.AddMediator()`, which now registers the handler. Change its setup so `IMediator` is registered without the handlers:

```csharp
        var services = new ServiceCollection();
        services.AddTransient<IMediator, MediatorService>();
```

`MediatorService` is the generated internal type, visible in the test assembly. Update the test's comment to say why. Make the same change to any other test whose premise is "handler not registered while using AddMediator()"; grep for `No handler registered` and `ThrowsAsync<InvalidOperationException>` in IntegrationTests.

- [ ] **Step 5: Move the integration tests off the scanner**

In `RequestIntegrationTests.cs`, `NotificationIntegrationTests.cs` and `StreamIntegrationTests.cs`, replace every `services.AddMediator()\n    .RegisterHandlersFromAssembly(typeof(X).Assembly);` with `services.AddMediator();`. In `RequestIntegrationTests.cs` lines 77 and 100, change the suppression justification text from `RegisterHandlersFromAssembly` to `AddMediator`.

- [ ] **Step 6: Run the whole test project**

Run: `dotnet test tests/ZeroAlloc.Mediator.Tests -c Release`
Expected: everything passes except `RegisterHandlersFromAssemblyTests`. Task 3 handles those, because `AddMediator()` now pre-registers handlers. If they fail here, note which ones and leave them for Task 3; fix any other failure now.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "test: cover generated handler registration and point messages at AddMediator" -m "Refs #275" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 3: Obsolete the scanner and move every caller

**Files:**
- Modify:
  - `src/ZeroAlloc.Mediator/MediatorBuilderExtensions.cs`
  - `tests/ZeroAlloc.Mediator.Tests/RegisterHandlersFromAssemblyTests.cs`
  - `tests/ZeroAlloc.Mediator.Benchmarks/Program.cs`
  - `tests/ZeroAlloc.Mediator.Benchmarks.Dispatch/Program.cs`
  - `samples/ZeroAlloc.Mediator.AspNetSample/Program.cs`

- [ ] **Step 1: Mark the scanner obsolete**

On `RegisterHandlersFromAssembly` in `MediatorBuilderExtensions.cs`, add above the existing `[RequiresUnreferencedCode]`:

```csharp
    [Obsolete("AddMediator() now registers every handler in this assembly at compile time; use AddMediator(ServiceLifetime) to change the default lifetime. This reflection-based scanner is not trim- or AOT-safe and will be removed in the next major version.")]
```

Then extend its XML `<remarks>` with one sentence: "Obsolete: the generated AddMediator() registers handlers at compile time."

- [ ] **Step 2: Make the scanner tests test the scanner alone**

`RegisterHandlersFromAssemblyTests` builds with `services.AddMediator().RegisterHandlersFromAssembly(...)`. `AddMediator()` now pre-registers the same handlers, so the scanner's lifetime and inclusion assertions no longer test the scanner. Change every such setup to construct the builder directly:

```csharp
        new MediatorBuilder(services).RegisterHandlersFromAssembly(asm);
```

Use `new MediatorBuilder(services).RegisterHandlersFromAssembly(asm, ServiceLifetime.X)` where a lifetime was passed. Keep the test that asserts the method returns an `IMediatorBuilder`. Put `#pragma warning disable CS0618 // Tests the obsolete scanner itself` at the top of the file, after the usings.

- [ ] **Step 3: Move the benchmarks and the sample to AddMediator**

- `tests/ZeroAlloc.Mediator.Benchmarks/Program.cs` lines 34–35: replace the `MediatorBuilderExtensions.RegisterHandlersFromAssembly(services.AddMediator(), ...)` call with `services.AddMediator();`.
- `tests/ZeroAlloc.Mediator.Benchmarks.Dispatch/Program.cs` line 51 is only a comment. Change it to describe that `AddMediator()` registers the handlers.
- `samples/ZeroAlloc.Mediator.AspNetSample/Program.cs` lines 8–9: `builder.Services.AddMediator();`. Line 45, justification text: `services.AddMediator()`.

- [ ] **Step 4: Build everything and run the tests**

Run: `dotnet build ZeroAlloc.Mediator.slnx -c Release && dotnet test tests/ZeroAlloc.Mediator.Tests -c Release`
Expected:
- The build shows 0 warnings. Any CS0618 left over is a missed caller; move it to `AddMediator()`.
- All tests pass.

If an AOT smoke project exists, under `samples/` or `tests/` with `PublishAot`, run its publish and execute the output, then record the result. Find it with `grep -rl "PublishAot" --include=*.csproj samples tests`.

- [ ] **Step 5: Commit**

```bash
git add src tests samples
git commit -m "refactor: obsolete RegisterHandlersFromAssembly in favour of the generated AddMediator" -m "Refs #275" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 4: Docs

**Files:**
- Modify: `docs/dependency-injection.md`, `docs/diagnostics.md` lines 171 and 186, `README.md`, `docs/getting-started.md`

- [ ] **Step 1: Rewrite the DI quickstart**

In `docs/dependency-injection.md`:
- **Quickstart.** Replace `builder.Services.AddMediator()\n    .RegisterHandlersFromAssembly(typeof(Program).Assembly);` with `builder.Services.AddMediator();`. Change the lead-in sentence "Register the mediator and scan the entry-assembly for handlers:" to "Register the mediator. The generated `AddMediator()` also registers every handler in the assembly:".
- **Worker example.** Make the same change wherever it registers handlers.
- **New section, "Handler lifetimes",** after the quickstarts:
  - Handlers are transient by default.
  - `AddMediator(ServiceLifetime.Scoped)` changes the default.
  - `[HandlerLifetime(ServiceLifetime.X)]` on a handler wins over the default.
  - Handlers are registered with TryAdd, so a registration you made before `AddMediator()` is kept.
  - Internal handlers are registered too.
  - Include a short code sample.
- **New section, "Migrating from RegisterHandlersFromAssembly":**
  - Delete the `.RegisterHandlersFromAssembly(...)` call.
  - If you passed a lifetime, pass it to `AddMediator(lifetime)` instead.
  - The scanner is `[Obsolete]`, is not trim- or AOT-safe, and will be removed in the next major version.
- **Existing mentions.** Update any other mention of the scanner in this file to match.

- [ ] **Step 2: Update the remaining docs**

- `docs/diagnostics.md` lines 171 and 186: use `services.AddMediator()`.
- `README.md` and `docs/getting-started.md`: if they show handler registration, use `AddMediator()`. If they don't, leave them unchanged. Run `grep -rn "RegisterHandlersFromAssembly" README.md docs --include=*.md | grep -v docs/plans/`; the only hits left should be in the migration section.

- [ ] **Step 3: Commit**

```bash
git add README.md docs
git commit -m "docs: register handlers with the generated AddMediator" -m "Refs #275" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

The controller pushes and opens the PR after the final review. The PR body carries a "Behaviour change" section with the spec's text:
- A call to `RegisterHandlersFromAssembly(asm, ServiceLifetime.Scoped)` after `AddMediator()` now yields Transient handlers; the fix is `AddMediator(ServiceLifetime.Scoped)`.
- Internal handlers are now registered too, which matters for `ValidateOnBuild`.

It also carries `BEGIN_COMMIT_OVERRIDE` with `feat: register handlers from the generated AddMediator` and `Closes #275`. The controller files a `next-major` issue for removing the scanner.
