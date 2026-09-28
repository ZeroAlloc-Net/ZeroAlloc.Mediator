#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// A pipeline behavior declared in this compilation, with the locations its diagnostics point
    /// at. <see cref="PipelineBehaviorInfo"/> comes from ZeroAlloc.Pipeline and has no location.
    /// </summary>
    internal sealed class SourceBehaviorInfo : IEquatable<SourceBehaviorInfo>
    {
        public SourceBehaviorInfo(PipelineBehaviorInfo info, LocationInfo? typeLocation, LocationInfo? attributeLocation)
        {
            Info = info;
            TypeLocation = typeLocation;
            AttributeLocation = attributeLocation;
        }

        public PipelineBehaviorInfo Info { get; }

        /// <summary>The class identifier, for ZAM005.</summary>
        public LocationInfo? TypeLocation { get; }

        /// <summary>The behavior attribute that sets the Order, for ZAM006.</summary>
        public LocationInfo? AttributeLocation { get; }

        public static SourceBehaviorInfo? From(GeneratorAttributeSyntaxContext context)
        {
            var info = PipelineBehaviorDiscoverer.FromAttributeSyntaxContext(context);
            if (info == null) return null;

            var typeLocation = context.TargetNode is ClassDeclarationSyntax classDecl
                ? LocationInfo.From(classDecl.Identifier.GetLocation())
                : null;
            var attributeLocation = context.Attributes.Length > 0
                ? LocationInfo.From(context.Attributes[0].ApplicationSyntaxReference)
                : null;
            return new SourceBehaviorInfo(info, typeLocation, attributeLocation);
        }

        public bool Equals(SourceBehaviorInfo? other) =>
            other is not null
            && Info.Equals(other.Info)
            && Equals(TypeLocation, other.TypeLocation)
            && Equals(AttributeLocation, other.AttributeLocation);

        public override bool Equals(object? obj) => Equals(obj as SourceBehaviorInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Info.GetHashCode();
                hash = (hash * 31) + (TypeLocation?.GetHashCode() ?? 0);
                hash = (hash * 31) + (AttributeLocation?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
