#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Mediator.Generator
{
    [Generator]
    public sealed class MediatorGenerator : IIncrementalGenerator
    {
        private static readonly SymbolDisplayFormat FullyQualifiedFormat =
            SymbolDisplayFormat.FullyQualifiedFormat;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var requestHandlers = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds && cds.BaseList != null,
                transform: static (ctx, ct) => GetRequestHandlerInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var notificationHandlers = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds && cds.BaseList != null,
                transform: static (ctx, ct) => GetNotificationHandlerInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var streamHandlers = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds && cds.BaseList != null,
                transform: static (ctx, ct) => GetStreamHandlerInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var pipelineBehaviors = context.CompilationProvider.Select(static (compilation, _) =>
                PipelineBehaviorDiscoverer.Discover(compilation)
                    .OrderBy(b => b.Order)
                    .ToImmutableArray());

            var requestTypes = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax tds && tds.BaseList != null,
                transform: static (ctx, ct) => GetRequestTypeInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var combined = requestHandlers
                .Combine(notificationHandlers)
                .Combine(streamHandlers)
                .Combine(pipelineBehaviors)
                .Combine(requestTypes);

            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var requestInfos = data.Left.Left.Left.Left;
                var notificationInfos = data.Left.Left.Left.Right;
                var streamInfos = data.Left.Left.Right;
                var pipelineInfos = data.Left.Right;
                var requestTypeInfos = data.Right;

                // Report diagnostics
                ReportDiagnostics(spc, requestInfos, pipelineInfos, requestTypeInfos);

                var source = GenerateMediatorClass(requestInfos, notificationInfos, streamInfos, pipelineInfos);
                spc.AddSource("ZeroAlloc.Mediator.g.cs", source);
            });
        }

        private static bool IsAccessible(INamedTypeSymbol symbol)
        {
            var current = symbol;
            while (current != null)
            {
                if (current.DeclaredAccessibility == Accessibility.Private
                    || current.DeclaredAccessibility == Accessibility.Protected
                    || current.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                {
                    return false;
                }
                current = current.ContainingType;
            }
            return true;
        }

        private static RequestHandlerInfo? GetRequestHandlerInfo(
            GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
            if (symbol == null) return null;
            if (!IsAccessible(symbol)) return null;

            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "ZeroAlloc.Mediator.IRequestHandler<TRequest, TResponse>"
                    && iface.TypeArguments.Length == 2)
                {
                    var requestType = iface.TypeArguments[0].ToDisplayString(FullyQualifiedFormat);
                    var responseType = iface.TypeArguments[1].ToDisplayString(FullyQualifiedFormat);
                    var handlerType = symbol.ToDisplayString(FullyQualifiedFormat);
                    var isValueType = iface.TypeArguments[0].IsValueType;
                    return new RequestHandlerInfo(requestType, responseType, handlerType, isValueType);
                }
            }

            return null;
        }

        private static NotificationHandlerInfo? GetNotificationHandlerInfo(
            GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
            if (symbol == null) return null;
            if (!IsAccessible(symbol)) return null;

            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "ZeroAlloc.Mediator.INotificationHandler<TNotification>"
                    && iface.TypeArguments.Length == 1)
                {
                    var notificationSymbol = iface.TypeArguments[0];
                    var notificationType = notificationSymbol.ToDisplayString(FullyQualifiedFormat);
                    var handlerType = symbol.ToDisplayString(FullyQualifiedFormat);

                    // Check if notification type has [ParallelNotification]
                    var isParallel = notificationSymbol.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Mediator.ParallelNotificationAttribute");

                    // Detect base handler: TNotification is an interface or abstract class
                    var isBaseHandler = notificationSymbol.TypeKind == TypeKind.Interface
                        || notificationSymbol.IsAbstract;

                    // Collect all INotification-derived interfaces the notification type implements
                    var baseTypeNames = new List<string>();
                    if (!isBaseHandler)
                    {
                        foreach (var baseIface in notificationSymbol.AllInterfaces)
                        {
                            if (IsNotificationInterface(baseIface))
                            {
                                baseTypeNames.Add(baseIface.ToDisplayString(FullyQualifiedFormat));
                            }
                        }
                    }

                    return new NotificationHandlerInfo(
                        notificationType,
                        handlerType,
                        isParallel,
                        isBaseHandler,
                        string.Join(";", baseTypeNames));
                }
            }

            return null;
        }

        private static bool IsNotificationInterface(INamedTypeSymbol symbol)
        {
            if (symbol.TypeKind != TypeKind.Interface) return false;

            // Check if this interface is or derives from INotification
            if (symbol.ToDisplayString() == "ZeroAlloc.Mediator.INotification") return true;

            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.ToDisplayString() == "ZeroAlloc.Mediator.INotification") return true;
            }

            return false;
        }

        private static StreamHandlerInfo? GetStreamHandlerInfo(
            GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
            if (symbol == null) return null;
            if (!IsAccessible(symbol)) return null;

            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "ZeroAlloc.Mediator.IStreamRequestHandler<TRequest, TResponse>"
                    && iface.TypeArguments.Length == 2)
                {
                    var requestType = iface.TypeArguments[0].ToDisplayString(FullyQualifiedFormat);
                    var responseType = iface.TypeArguments[1].ToDisplayString(FullyQualifiedFormat);
                    var handlerType = symbol.ToDisplayString(FullyQualifiedFormat);
                    return new StreamHandlerInfo(requestType, responseType, handlerType);
                }
            }

            return null;
        }

        private static RequestTypeInfo? GetRequestTypeInfo(
            GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            var typeDecl = (TypeDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
            if (symbol == null) return null;

            // Only report for types defined in the current compilation's syntax trees
            foreach (var location in symbol.Locations)
            {
                if (!location.IsInSource) return null;
            }

            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "ZeroAlloc.Mediator.IRequest<TResponse>"
                    && iface.TypeArguments.Length == 1)
                {
                    var requestType = symbol.ToDisplayString(FullyQualifiedFormat);
                    var responseType = iface.TypeArguments[0].ToDisplayString(FullyQualifiedFormat);
                    return new RequestTypeInfo(requestType, responseType);
                }
            }

            return null;
        }

        private static void ReportDiagnostics(
            Microsoft.CodeAnalysis.SourceProductionContext spc,
            ImmutableArray<RequestHandlerInfo?> requestHandlers,
            ImmutableArray<PipelineBehaviorInfo> pipelineBehaviors,
            ImmutableArray<RequestTypeInfo?> requestTypes)
        {
            var validHandlers = requestHandlers.Where(x => x != null).Select(x => x!).ToList();

            // ZAM001: No registered handler for a request type
            var handledRequestTypes = new HashSet<string>(validHandlers.Select(h => h.RequestTypeName));
            var validRequestTypes = requestTypes.Where(x => x != null).Select(x => x!).ToList();
            foreach (var requestType in validRequestTypes)
            {
                if (!handledRequestTypes.Contains(requestType.RequestTypeName))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.NoHandler,
                        Location.None,
                        requestType.RequestTypeName));
                }
            }

            // ZAM002: Duplicate handlers for the same request type
            var grouped = validHandlers.GroupBy(h => h.RequestTypeName).ToList();
            foreach (var group in grouped)
            {
                if (group.Count() > 1)
                {
                    var handlerNames = string.Join(", ", group.Select(h => h.HandlerTypeName));
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateHandler,
                        Location.None,
                        group.Key,
                        handlerNames));
                }
            }

            // ZAM003: Request type is a class (not a value type)
            var seenRequestTypes = new HashSet<string>();
            foreach (var handler in validHandlers)
            {
                if (!handler.IsRequestValueType && seenRequestTypes.Add(handler.RequestTypeName))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ClassRequest,
                        Location.None,
                        handler.RequestTypeName));
                }
            }

            // ZAM005: Missing behavior Handle method (2 type params expected for Send pipeline)
            var validBehaviors = pipelineBehaviors.ToList();
            foreach (var behavior in PipelineDiagnosticRules.FindMissingHandleMethod(validBehaviors, expectedTypeParamCount: 2))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingBehaviorHandleMethod,
                    Location.None,
                    behavior.BehaviorTypeName));
            }

            // ZAM006: Duplicate behavior order
            foreach (var group in PipelineDiagnosticRules.FindDuplicateOrders(validBehaviors))
            {
                var behaviorNames = string.Join(", ", group.Select(b => b.BehaviorTypeName));
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateBehaviorOrder,
                    Location.None,
                    behaviorNames,
                    group.Key));
            }
        }

        private static string GenerateMediatorClass(
            ImmutableArray<RequestHandlerInfo?> requestHandlers,
            ImmutableArray<NotificationHandlerInfo?> notificationHandlers,
            ImmutableArray<StreamHandlerInfo?> streamHandlers,
            ImmutableArray<PipelineBehaviorInfo> pipelineBehaviors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine("namespace ZeroAlloc.Mediator");
            sb.AppendLine("{");
            sb.AppendLine("    public static partial class Mediator");
            sb.AppendLine("    {");

            var validRequests = requestHandlers.Where(x => x != null).Select(x => x!).ToList();
            var validNotifications = notificationHandlers.Where(x => x != null).Select(x => x!).ToList();
            var validStreams = streamHandlers.Where(x => x != null).Select(x => x!).ToList();
            var validPipelines = pipelineBehaviors.ToList();

            // Emit factory fields for request handlers
            foreach (var handler in validRequests)
            {
                var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
                sb.AppendLine(string.Format("        internal static Func<{0}>? {1};", handler.HandlerTypeName, fieldName));
            }

            // Emit factory fields for notification handlers
            foreach (var handler in validNotifications)
            {
                var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
                sb.AppendLine(string.Format("        internal static Func<{0}>? {1};", handler.HandlerTypeName, fieldName));
            }

            // Emit factory fields for stream handlers
            foreach (var handler in validStreams)
            {
                var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
                sb.AppendLine(string.Format("        internal static Func<{0}>? {1};", handler.HandlerTypeName, fieldName));
            }

            sb.AppendLine();

            // Emit Send methods
            foreach (var handler in validRequests)
            {
                EmitSendMethod(sb, handler, validPipelines);
            }

            // Emit Publish methods
            EmitPublishMethods(sb, validNotifications);

            // Emit CreateStream methods
            foreach (var handler in validStreams)
            {
                EmitCreateStreamMethod(sb, handler);
            }

            // Emit Configure method
            EmitConfigureMethod(sb, validRequests, validNotifications, validStreams);

            sb.AppendLine("    }");

            // Emit MediatorConfig class
            EmitMediatorConfigClass(sb, validRequests, validNotifications, validStreams);

            sb.AppendLine();

            // Emit IMediator interface
            EmitIMediatorInterface(sb, validRequests, validNotifications, validStreams);

            sb.AppendLine();

            // Emit MediatorService class
            EmitMediatorService(sb, validRequests, validNotifications, validStreams);

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void EmitSendMethod(
            StringBuilder sb,
            RequestHandlerInfo handler,
            List<PipelineBehaviorInfo> pipelines)
        {
            var applicablePipelines = pipelines
                .Where(p => p.AppliesTo == null || p.AppliesTo == handler.RequestTypeName)
                .ToList();

            sb.AppendLine(string.Format(
                "        public static ValueTask<{0}> Send({1} request, CancellationToken ct = default)",
                handler.ResponseTypeName, handler.RequestTypeName));
            sb.AppendLine("        {");

            if (applicablePipelines.Count == 0)
            {
                var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
                sb.AppendLine(string.Format(
                    "            var handler = {0}?.Invoke() ?? new {1}();",
                    fieldName, handler.HandlerTypeName));
                sb.AppendLine("            return handler.Handle(request, ct);");
            }
            else
            {
                var shape = new PipelineShape
                {
                    TypeArguments = new[] { handler.RequestTypeName, handler.ResponseTypeName },
                    OuterParameterNames = new[] { "request", "ct" },
                    LambdaParameterPrefixes = new[] { "r", "c" },
                    InnermostBodyTemplate = string.Format(
                        "{{ var handler = {0}?.Invoke() ?? new {1}(); return handler.Handle(r{2}, c{2}); }}",
                        GetFactoryFieldName(handler.HandlerTypeName),
                        handler.HandlerTypeName,
                        applicablePipelines.Count),
                };

                var chain = PipelineEmitter.EmitChain(applicablePipelines, shape);
                sb.AppendLine(string.Format("            return {0};", chain));
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitPublishMethods(
            StringBuilder sb,
            List<NotificationHandlerInfo> handlers)
        {
            // Separate base handlers (for interfaces/abstract) from concrete handlers
            var baseHandlers = handlers.Where(h => h.IsBaseHandler).ToList();
            var concreteHandlers = handlers.Where(h => !h.IsBaseHandler).ToList();

            // Group concrete handlers by notification type
            var grouped = concreteHandlers.GroupBy(h => h.NotificationTypeName).ToList();

            foreach (var group in grouped)
            {
                var notificationType = group.Key;
                var isParallel = group.Any(h => h.IsParallel);
                var handlerList = group.ToList();

                // Find matching base handlers by checking the concrete type's base interfaces
                var baseTypeNames = handlerList[0].BaseNotificationTypeNames;
                var baseTypeSet = string.IsNullOrEmpty(baseTypeNames)
                    ? new HashSet<string>()
                    : new HashSet<string>(baseTypeNames.Split(';'));

                var matchingBaseHandlers = baseHandlers
                    .Where(bh => baseTypeSet.Contains(bh.NotificationTypeName))
                    .ToList();

                // Combine concrete + matching base handlers
                var allHandlers = new List<NotificationHandlerInfo>(handlerList);
                allHandlers.AddRange(matchingBaseHandlers);

                if (isParallel)
                {
                    sb.AppendLine(string.Format(
                        "        public static async ValueTask Publish({0} notification, CancellationToken ct = default)",
                        notificationType));
                    sb.AppendLine("        {");

                    // Use Task.WhenAll for parallel execution
                    var taskExprs = new List<string>();
                    foreach (var handler in allHandlers)
                    {
                        var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
                        taskExprs.Add(string.Format(
                            "({0}?.Invoke() ?? new {1}()).Handle(notification, ct).AsTask()",
                            fieldName, handler.HandlerTypeName));
                    }

                    sb.AppendLine("            await Task.WhenAll(");
                    for (int i = 0; i < taskExprs.Count; i++)
                    {
                        var comma = i < taskExprs.Count - 1 ? "," : "";
                        sb.AppendLine(string.Format("                {0}{1}", taskExprs[i], comma));
                    }
                    sb.AppendLine("            );");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine(string.Format(
                        "        public static async ValueTask Publish({0} notification, CancellationToken ct = default)",
                        notificationType));
                    sb.AppendLine("        {");

                    foreach (var handler in allHandlers)
                    {
                        var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
                        sb.AppendLine(string.Format(
                            "            await ({0}?.Invoke() ?? new {1}()).Handle(notification, ct);",
                            fieldName, handler.HandlerTypeName));
                    }

                    sb.AppendLine("        }");
                }

                sb.AppendLine();
            }
        }

        private static void EmitCreateStreamMethod(StringBuilder sb, StreamHandlerInfo handler)
        {
            var fieldName = GetFactoryFieldName(handler.HandlerTypeName);
            sb.AppendLine(string.Format(
                "        public static System.Collections.Generic.IAsyncEnumerable<{0}> CreateStream({1} request, CancellationToken ct = default)",
                handler.ResponseTypeName, handler.RequestTypeName));
            sb.AppendLine("        {");
            sb.AppendLine(string.Format(
                "            var handler = {0}?.Invoke() ?? new {1}();",
                fieldName, handler.HandlerTypeName));
            sb.AppendLine("            return handler.Handle(request, ct);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitConfigureMethod(
            StringBuilder sb,
            List<RequestHandlerInfo> requestHandlers,
            List<NotificationHandlerInfo> notificationHandlers,
            List<StreamHandlerInfo> streamHandlers)
        {
            sb.AppendLine("        public static void Configure(Action<MediatorConfig> configure)");
            sb.AppendLine("        {");
            sb.AppendLine("            var config = new MediatorConfig();");
            sb.AppendLine("            configure(config);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitMediatorConfigClass(
            StringBuilder sb,
            List<RequestHandlerInfo> requestHandlers,
            List<NotificationHandlerInfo> notificationHandlers,
            List<StreamHandlerInfo> streamHandlers)
        {
            sb.AppendLine();
            sb.AppendLine("    public sealed class MediatorConfig");
            sb.AppendLine("    {");
            sb.AppendLine("        public void SetFactory<THandler>(Func<THandler> factory) where THandler : class");
            sb.AppendLine("        {");

            var allHandlers = new List<KeyValuePair<string, string>>();

            foreach (var h in requestHandlers)
            {
                allHandlers.Add(new KeyValuePair<string, string>(h.HandlerTypeName, GetFactoryFieldName(h.HandlerTypeName)));
            }

            // Deduplicate notification handlers
            var seenNotificationHandlers = new HashSet<string>();
            foreach (var h in notificationHandlers)
            {
                if (seenNotificationHandlers.Add(h.HandlerTypeName))
                {
                    allHandlers.Add(new KeyValuePair<string, string>(h.HandlerTypeName, GetFactoryFieldName(h.HandlerTypeName)));
                }
            }

            foreach (var h in streamHandlers)
            {
                allHandlers.Add(new KeyValuePair<string, string>(h.HandlerTypeName, GetFactoryFieldName(h.HandlerTypeName)));
            }

            bool first = true;
            foreach (var pair in allHandlers)
            {
                var keyword = first ? "if" : "else if";
                first = false;
                sb.AppendLine(string.Format(
                    "            {0} (factory is Func<{1}> {2}Factory)",
                    keyword, pair.Key, SanitizeFieldName(pair.Key)));
                sb.AppendLine(string.Format(
                    "                Mediator.{0} = {1}Factory;",
                    pair.Value, SanitizeFieldName(pair.Key)));
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }

        private static void EmitIMediatorInterface(
            StringBuilder sb,
            List<RequestHandlerInfo> requestHandlers,
            List<NotificationHandlerInfo> notificationHandlers,
            List<StreamHandlerInfo> streamHandlers)
        {
            sb.AppendLine("    public partial interface IMediator");
            sb.AppendLine("    {");

            foreach (var handler in requestHandlers)
            {
                sb.AppendLine(string.Format(
                    "        ValueTask<{0}> Send({1} request, CancellationToken ct = default);",
                    handler.ResponseTypeName, handler.RequestTypeName));
            }

            // Only emit Publish for concrete notification types (not base handlers)
            var concreteNotifications = notificationHandlers
                .Where(h => !h.IsBaseHandler)
                .GroupBy(h => h.NotificationTypeName)
                .ToList();

            foreach (var group in concreteNotifications)
            {
                sb.AppendLine(string.Format(
                    "        ValueTask Publish({0} notification, CancellationToken ct = default);",
                    group.Key));
            }

            foreach (var handler in streamHandlers)
            {
                sb.AppendLine(string.Format(
                    "        System.Collections.Generic.IAsyncEnumerable<{0}> CreateStream({1} request, CancellationToken ct = default);",
                    handler.ResponseTypeName, handler.RequestTypeName));
            }

            sb.AppendLine("    }");
        }

        private static void EmitMediatorService(
            StringBuilder sb,
            List<RequestHandlerInfo> requestHandlers,
            List<NotificationHandlerInfo> notificationHandlers,
            List<StreamHandlerInfo> streamHandlers)
        {
            sb.AppendLine("    public partial class MediatorService : IMediator");
            sb.AppendLine("    {");

            foreach (var handler in requestHandlers)
            {
                sb.AppendLine(string.Format(
                    "        public ValueTask<{0}> Send({1} request, CancellationToken ct)",
                    handler.ResponseTypeName, handler.RequestTypeName));
                sb.AppendLine(string.Format(
                    "            => Mediator.Send(request, ct);"));
            }

            var concreteNotifications = notificationHandlers
                .Where(h => !h.IsBaseHandler)
                .GroupBy(h => h.NotificationTypeName)
                .ToList();

            foreach (var group in concreteNotifications)
            {
                sb.AppendLine(string.Format(
                    "        public ValueTask Publish({0} notification, CancellationToken ct)",
                    group.Key));
                sb.AppendLine(string.Format(
                    "            => Mediator.Publish(notification, ct);"));
            }

            foreach (var handler in streamHandlers)
            {
                sb.AppendLine(string.Format(
                    "        public System.Collections.Generic.IAsyncEnumerable<{0}> CreateStream({1} request, CancellationToken ct)",
                    handler.ResponseTypeName, handler.RequestTypeName));
                sb.AppendLine(string.Format(
                    "            => Mediator.CreateStream(request, ct);"));
            }

            sb.AppendLine("    }");
        }

        private static string GetSimpleTypeName(string fullyQualifiedName)
        {
            var name = fullyQualifiedName;
            if (name.StartsWith("global::"))
            {
                name = name.Substring("global::".Length);
            }

            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                name = name.Substring(lastDot + 1);
            }

            return name;
        }

        private static string GetFactoryFieldName(string handlerTypeName)
        {
            // Convert "global::TestApp.PingHandler" to "_pingHandlerFactory"
            var simpleName = GetSimpleTypeName(handlerTypeName);
            return "_" + char.ToLowerInvariant(simpleName[0]) + simpleName.Substring(1) + "Factory";
        }

        private static string SanitizeFieldName(string handlerTypeName)
        {
            var simpleName = GetSimpleTypeName(handlerTypeName);
            return char.ToLowerInvariant(simpleName[0]) + simpleName.Substring(1);
        }
    }
}
