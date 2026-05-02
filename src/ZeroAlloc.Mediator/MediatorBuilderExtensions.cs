using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroAlloc.Mediator;

/// <summary>
/// Extension methods on <see cref="IMediatorBuilder"/> for assembly-scan based
/// handler registration. Hand-written (not generator-emitted) because handler
/// scanning happens once at startup and the abstractions package should be
/// usable without forcing the generator to know which assemblies to scan.
/// </summary>
public static class MediatorBuilderExtensions
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for concrete public classes implementing
    /// <see cref="IRequestHandler{TRequest,TResponse}"/>, <see cref="INotificationHandler{TNotification}"/>,
    /// or <see cref="IStreamRequestHandler{TRequest,TResponse}"/> and registers each as a service keyed
    /// by its concrete type using <paramref name="defaultLifetime"/>, unless the handler is decorated
    /// with <see cref="HandlerLifetimeAttribute"/> in which case the attribute's lifetime wins.
    /// Registrations use <c>TryAdd</c> semantics — calling twice is a no-op for handlers already registered.
    /// </summary>
    /// <remarks>
    /// Uses reflection over <paramref name="assembly"/> and is therefore not safe under
    /// trimming or AOT publish. AOT consumers should register handlers explicitly via
    /// <see cref="IServiceCollection"/> instead of using this scanner.
    /// </remarks>
    [RequiresUnreferencedCode("Assembly scanning enumerates types via reflection; handler types may be removed when trimming.")]
    [RequiresDynamicCode("Assembly scanning constructs ServiceDescriptor entries reflectively.")]
    public static IMediatorBuilder RegisterHandlersFromAssembly(
        this IMediatorBuilder builder,
        Assembly assembly,
        ServiceLifetime defaultLifetime = ServiceLifetime.Transient)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));

        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract || !type.IsPublic || type.IsGenericTypeDefinition) continue;
            if (!ImplementsHandlerInterface(type)) continue;

            var attr = type.GetCustomAttribute<HandlerLifetimeAttribute>();
            var lifetime = attr?.Lifetime ?? defaultLifetime;

            builder.Services.TryAdd(new ServiceDescriptor(type, type, lifetime));
        }
        return builder;
    }

    /// <summary>
    /// Convenience overload for scanning multiple assemblies. Each assembly is scanned independently
    /// using the default <see cref="ServiceLifetime.Transient"/> lifetime; call
    /// <see cref="RegisterHandlersFromAssembly"/> per-assembly if you need a different default per assembly.
    /// </summary>
    [RequiresUnreferencedCode("Assembly scanning enumerates types via reflection; handler types may be removed when trimming.")]
    [RequiresDynamicCode("Assembly scanning constructs ServiceDescriptor entries reflectively.")]
    public static IMediatorBuilder RegisterHandlersFromAssemblies(
        this IMediatorBuilder builder,
        params Assembly[] assemblies)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (assemblies is null) throw new ArgumentNullException(nameof(assemblies));
        foreach (var asm in assemblies)
        {
            if (asm is null)
                throw new ArgumentNullException(nameof(assemblies), "Array contains a null Assembly.");
            RegisterHandlersFromAssembly(builder, asm);
        }
        return builder;
    }

    private static bool ImplementsHandlerInterface(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        foreach (var i in type.GetInterfaces())
        {
            if (!i.IsGenericType) continue;
            var def = i.GetGenericTypeDefinition();
            if (def == typeof(IRequestHandler<,>)
                || def == typeof(INotificationHandler<>)
                || def == typeof(IStreamRequestHandler<,>))
                return true;
        }
        return false;
    }
}
