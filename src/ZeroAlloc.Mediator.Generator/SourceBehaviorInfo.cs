#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// A type in this compilation that carries a behavior attribute, with the locations its
    /// diagnostics point at. <see cref="PipelineBehaviorInfo"/> and
    /// <see cref="PipelineBehaviorCandidateInfo"/> come from ZeroAlloc.Pipeline and have no location.
    /// </summary>
    internal sealed class SourceBehaviorInfo : IEquatable<SourceBehaviorInfo>
    {
        public SourceBehaviorInfo(
            PipelineBehaviorCandidateInfo candidate,
            PipelineBehaviorInfo? info,
            LocationInfo? typeLocation,
            LocationInfo? attributeLocation)
        {
            Candidate = candidate;
            Info = info;
            TypeLocation = typeLocation;
            AttributeLocation = attributeLocation;
        }

        /// <summary>The attributed type, valid or not, for ZAM009.</summary>
        public PipelineBehaviorCandidateInfo Candidate { get; }

        /// <summary>
        /// The behavior, or null when the type does not implement IPipelineBehavior. Only a type
        /// with a behavior joins the pipeline.
        /// </summary>
        public PipelineBehaviorInfo? Info { get; }

        /// <summary>The class identifier, for ZAM005 and ZAM009.</summary>
        public LocationInfo? TypeLocation { get; }

        /// <summary>The behavior attribute that sets the Order, for ZAM006.</summary>
        public LocationInfo? AttributeLocation { get; }

        public static SourceBehaviorInfo? From(GeneratorAttributeSyntaxContext context)
        {
            var candidate = PipelineBehaviorDiscoverer.CandidateFromAttributeSyntaxContext(context);
            if (candidate == null) return null;

            // Still null for a type without IPipelineBehavior, which stays out of the pipeline.
            var info = PipelineBehaviorDiscoverer.FromAttributeSyntaxContext(context);

            var typeLocation = context.TargetNode is ClassDeclarationSyntax classDecl
                ? LocationInfo.From(classDecl.Identifier.GetLocation())
                : null;
            var attributeLocation = context.Attributes.Length > 0
                ? LocationInfo.From(context.Attributes[0].ApplicationSyntaxReference)
                : null;
            return new SourceBehaviorInfo(candidate, info, typeLocation, attributeLocation);
        }

        public bool Equals(SourceBehaviorInfo? other) =>
            other is not null
            && Candidate.Equals(other.Candidate)
            && Equals(Info, other.Info)
            && Equals(TypeLocation, other.TypeLocation)
            && Equals(AttributeLocation, other.AttributeLocation);

        public override bool Equals(object? obj) => Equals(obj as SourceBehaviorInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Candidate.GetHashCode();
                hash = (hash * 31) + (Info?.GetHashCode() ?? 0);
                hash = (hash * 31) + (TypeLocation?.GetHashCode() ?? 0);
                hash = (hash * 31) + (AttributeLocation?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
