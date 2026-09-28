#nullable enable
namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// Names of the pipeline steps, so tests can check that an unrelated edit leaves them cached.
    /// </summary>
    internal static class TrackingNames
    {
        public const string RequestHandlers = nameof(RequestHandlers);
        public const string NotificationHandlers = nameof(NotificationHandlers);
        public const string StreamHandlers = nameof(StreamHandlers);
        public const string SourceBehaviors = nameof(SourceBehaviors);
        public const string RequestTypes = nameof(RequestTypes);
        public const string NotificationTypes = nameof(NotificationTypes);
        public const string EmitInputs = nameof(EmitInputs);
        public const string DiagnosticInputs = nameof(DiagnosticInputs);
    }
}
