#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// A diagnostic location the pipeline can cache: the syntax tree and the span within it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree is kept, not just its file path, because the rebuilt diagnostic must be a source
    /// location. <c>Location.Create(filePath, span, lineSpan)</c> gives an external-file location
    /// with no <see cref="Location.SourceTree"/>, and the compiler then ignores
    /// <c>#pragma warning disable</c> for it.
    /// </para>
    /// <para>
    /// The location takes part in the equality of the model that holds it. A model that compared
    /// equal without it kept the first run's location in the cache, in a tree that a later
    /// compilation no longer contains, and reporting that location failed the generator.
    /// </para>
    /// <para>
    /// Keeping the tree does not defeat caching. <see cref="SyntaxTree"/> compares by reference,
    /// and a compilation reuses the tree instance of every file that did not change, so the
    /// location compares equal across runs until its own file is edited. The emitted source is
    /// built from models with the location stripped, so such an edit reruns only the diagnostics.
    /// </para>
    /// </remarks>
    internal sealed class LocationInfo : IEquatable<LocationInfo>
    {
        public LocationInfo(SyntaxTree tree, TextSpan span)
        {
            Tree = tree;
            Span = span;
        }

        public SyntaxTree Tree { get; }

        public TextSpan Span { get; }

        public Location ToLocation() => Location.Create(Tree, Span);

        /// <summary>
        /// Null for <see cref="Location.None"/>, a missing location, or one outside source.
        /// </summary>
        public static LocationInfo? From(Location? location) =>
            location?.SourceTree is { } tree ? new LocationInfo(tree, location.SourceSpan) : null;

        public static LocationInfo? From(SyntaxReference? reference) =>
            reference is null ? null : new LocationInfo(reference.SyntaxTree, reference.Span);

        /// <summary>The source location, or <see cref="Location.None"/> when there is none.</summary>
        public static Location ToLocation(LocationInfo? info) => info?.ToLocation() ?? Location.None;

        /// <summary>
        /// Orders locations by file path, then position, so "the later declaration" is the same
        /// on every run.
        /// </summary>
        public static IComparer<LocationInfo?> Order { get; } = Comparer<LocationInfo?>.Create(Compare);

        /// <inheritdoc cref="Order"/>
        public static int Compare(LocationInfo? x, LocationInfo? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var byPath = string.CompareOrdinal(x.Tree.FilePath, y.Tree.FilePath);
            return byPath != 0 ? byPath : x.Span.Start.CompareTo(y.Span.Start);
        }

        public bool Equals(LocationInfo? other) =>
            other is not null && ReferenceEquals(Tree, other.Tree) && Span.Equals(other.Span);

        public override bool Equals(object? obj) => Equals(obj as LocationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                return (RuntimeHelpers.GetHashCode(Tree) * 31) + Span.GetHashCode();
            }
        }
    }
}
