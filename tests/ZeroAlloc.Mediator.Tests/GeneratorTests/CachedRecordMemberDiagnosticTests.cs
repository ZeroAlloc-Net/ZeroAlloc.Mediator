using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

/// <summary>
/// ZAM011: a [CacheResponse] record's compiler-written ToString prints each member through the
/// member's own ToString, so a member whose type does not override it prints as its type name, and
/// requests that differ only in that member share one cache entry.
/// </summary>
public class CachedRecordMemberDiagnosticTests
{
    private const string Usings = """
        using ZeroAlloc.Mediator;
        using ZeroAlloc.Mediator.Cache;
        using System;
        using System.Collections.Generic;
        using System.Text;
        namespace TestApp;

        """;

    [Fact]
    public void ArrayMember_IsReportedAsAnError_AtTheMember()
    {
        var (source, spans) = DiagnosticLocationTests.Unmark(Usings + """
            [CacheResponse(TtlMs = 1000)]
            public readonly record struct GetUsers(int[] [|Ids|]) : IRequest<int>;
            """);

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        var zam011 = DiagnosticLocationTests.One(diagnostics, "ZAM011");
        Assert.Equal(DiagnosticSeverity.Error, zam011.Severity);
        var message = zam011.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Member 'Ids'", message, StringComparison.Ordinal);
        Assert.Contains("TestApp.GetUsers'", message, StringComparison.Ordinal);
        Assert.Contains("type 'int[]'", message, StringComparison.Ordinal);
        DiagnosticLocationTests.AssertAt(zam011.Location, source, Assert.Single(spans));
        // ZAM010 is about non-record types; a record is not reported twice.
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAM010", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public sealed record R(List<int> Items) : IRequest<int>;", "Items", "System.Collections.Generic.List<int>")]
    [InlineData("public sealed record R(IReadOnlyList<int> Items) : IRequest<int>;", "Items", "System.Collections.Generic.IReadOnlyList<int>")]
    [InlineData("public sealed record R(Dictionary<string, int> Map) : IRequest<int>;", "Map", "System.Collections.Generic.Dictionary<string, int>")]
    // A class or struct that keeps object's ToString.
    [InlineData("public sealed class Box { public int V; }\n[CacheResponse(TtlMs = 1000)]\npublic sealed record R(Box B) : IRequest<int>;", "B", "TestApp.Box")]
    [InlineData("public struct Point { public int X; }\n[CacheResponse(TtlMs = 1000)]\npublic readonly record struct R(Point P) : IRequest<int>;", "P", "TestApp.Point")]
    // Nullable<T> prints its value, so T decides.
    [InlineData("public struct Point { public int X; }\n[CacheResponse(TtlMs = 1000)]\npublic readonly record struct R(Point? P) : IRequest<int>;", "P", "TestApp.Point")]
    // PrintMembers appends a member with ToString, not IFormattable, so IFormattable alone is not enough.
    [InlineData("""
        public readonly struct Code : IFormattable
        {
            public readonly int V;
            public string ToString(string? format, IFormatProvider? provider) => V.ToString(format, provider);
        }
        [CacheResponse(TtlMs = 1000)]
        public readonly record struct R(Code C) : IRequest<int>;
        """, "C", "TestApp.Code")]
    // A record member: its own members are printed, with the same rule.
    [InlineData("public sealed record Filter(string Name, int[] Tags);\n[CacheResponse(TtlMs = 1000)]\npublic sealed record R(Filter F) : IRequest<int>;", "F.Tags", "int[]")]
    // A record that refers to itself: the cycle is visited once.
    [InlineData("public sealed record Node(int[] Values, Node? Next);\n[CacheResponse(TtlMs = 1000)]\npublic sealed record R(Node Head) : IRequest<int>;", "Head.Values", "int[]")]
    // A tuple prints each element by its own ToString.
    [InlineData("public readonly record struct R((int[] Ids, int Page) Query) : IRequest<int>;", "Query.Ids", "int[]")]
    [InlineData("public readonly record struct R(KeyValuePair<string, int[]> Pair) : IRequest<int>;", "Pair.Value", "int[]")]
    // Memory<T> prints only its length unless T is char.
    [InlineData("public readonly record struct R(ReadOnlyMemory<int> Ids) : IRequest<int>;", "Ids", "System.ReadOnlyMemory<int>")]
    [InlineData("public readonly record struct R(Func<int> Factory) : IRequest<int>;", "Factory", "System.Func<int>")]
    // Members declared in the body, not only positional ones.
    [InlineData("public sealed record R : IRequest<int> { public int[] Ids { get; init; } = []; }", "Ids", "int[]")]
    [InlineData("public sealed record R : IRequest<int> { public int[] Ids = []; }", "Ids", "int[]")]
    // A generic record's member whose type is known to be a sequence.
    [InlineData("public sealed record R<T>(List<T> Items) : IRequest<int>;", "Items", "System.Collections.Generic.List<T>")]
    public void MemberWithoutValueText_IsReported(string declaration, string path, string typeName)
    {
        var source = Usings + Attributed(declaration);

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        var message = DiagnosticLocationTests.One(diagnostics, "ZAM011").GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains($"Member '{path}'", message, StringComparison.Ordinal);
        Assert.Contains($"type '{typeName}'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void EachMemberWithoutValueText_IsReportedSeparately()
    {
        var source = Usings + Attributed("public sealed record R(int[] Ids, int Page, List<string> Names) : IRequest<int>;");

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        var messages = diagnostics.Where(d => string.Equals(d.Id, "ZAM011", StringComparison.Ordinal))
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Contains("Member 'Ids'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Member 'Names'", StringComparison.Ordinal));
    }

    [Fact]
    public void InheritedMember_IsReportedAtItsDeclarationInTheBaseRecord()
    {
        var (source, spans) = DiagnosticLocationTests.Unmark(Usings + """
            public abstract record Paged { public int[] [|Ids|] { get; init; } = []; }
            [CacheResponse(TtlMs = 1000)]
            public sealed record R(int Page) : Paged, IRequest<int>;
            """);

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        var zam011 = DiagnosticLocationTests.One(diagnostics, "ZAM011");
        Assert.Contains("TestApp.R'", zam011.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        DiagnosticLocationTests.AssertAt(zam011.Location, source, Assert.Single(spans));
    }

    [Fact]
    public void OverriddenMember_IsReportedOnce()
    {
        // PrintMembers prints the base declaration and skips the override.
        var source = Usings + """
            public abstract record Paged { public virtual int[] Ids { get; init; } = []; }
            [CacheResponse(TtlMs = 1000)]
            public sealed record R : Paged, IRequest<int> { public override int[] Ids { get; init; } = []; }
            """;

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        DiagnosticLocationTests.One(diagnostics, "ZAM011");
    }

    [Fact]
    public void InheritedMemberFromAnotherAssembly_IsReportedAtTheRequestType()
    {
        var library = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText("namespace Lib; public abstract record Paged { public int[] Ids { get; init; } = []; }")],
            GeneratorTestHelper.CompilableReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var (source, spans) = DiagnosticLocationTests.Unmark(Usings + """
            [CacheResponse(TtlMs = 1000)]
            public sealed record [|R|](int Page) : Lib.Paged, IRequest<int>;
            """);

        var diagnostics = CacheKeyDiagnosticTests.Run(source, library.ToMetadataReference());

        var zam011 = DiagnosticLocationTests.One(diagnostics, "ZAM011");
        Assert.Contains("Member 'Ids'", zam011.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        DiagnosticLocationTests.AssertAt(zam011.Location, source, Assert.Single(spans));
    }

    [Theory]
    // Members with a string form: primitives, string, enums, BCL value types, nullable of them.
    [InlineData("public readonly record struct R(int Id, string Name, DayOfWeek Day, Guid G, DateTime At, decimal M, int? N, bool B, char C) : IRequest<int>;")]
    [InlineData("public readonly record struct R(ReadOnlyMemory<char> Text, (int A, string B) Pair) : IRequest<int>;")]
    // The request writes its own text.
    [InlineData("public sealed record R(int[] Ids) : IRequest<int> { public override string ToString() => string.Join(\",\", Ids); }")]
    [InlineData("public sealed record R(int[] Ids) : IRequest<int> { private bool PrintMembers(StringBuilder b) { b.AppendJoin(',', Ids); return true; } }")]
    [InlineData("public readonly record struct R(int[] Ids) : IRequest<int> { private bool PrintMembers(StringBuilder b) { b.AppendJoin(',', Ids); return true; } }")]
    // The key interpolation formats an IFormattable request through that interface.
    [InlineData("public sealed record R(int[] Ids) : IRequest<int>, IFormattable { public string ToString(string? f, IFormatProvider? p) => string.Join(\",\", Ids); }")]
    // A base record prints its own members its own way.
    [InlineData("""
        public abstract record Paged
        {
            public int[] Ids { get; init; } = [];
            protected virtual bool PrintMembers(StringBuilder b) { b.AppendJoin(',', Ids); return true; }
        }
        [CacheResponse(TtlMs = 1000)]
        public sealed record R(int Page) : Paged, IRequest<int>;
        """)]
    // A base record seals ToString, so the request inherits it.
    [InlineData("""
        public abstract record Paged(int[] Ids) { public sealed override string ToString() => string.Join(",", Ids); }
        [CacheResponse(TtlMs = 1000)]
        public sealed record R(int[] More) : Paged(More), IRequest<int>;
        """)]
    // A nested record that writes its own text.
    [InlineData("""
        public sealed record Filter(int[] Tags) { public override string ToString() => string.Join(",", Tags); }
        [CacheResponse(TtlMs = 1000)]
        public sealed record R(Filter F) : IRequest<int>;
        """)]
    // A member type that overrides ToString, itself or in a base class.
    [InlineData("""
        public class Named { public string Name = ""; public override string ToString() => Name; }
        public sealed class Box : Named { }
        [CacheResponse(TtlMs = 1000)]
        public sealed record R(Named N, Box B) : IRequest<int>;
        """)]
    // A collection that prints its items.
    [InlineData("""
        public sealed class IdList : List<int> { public override string ToString() => string.Join(",", this); }
        [CacheResponse(TtlMs = 1000)]
        public sealed record R(IdList Ids) : IRequest<int>;
        """)]
    // The runtime type decides: a type parameter, object, a non-sequence interface.
    [InlineData("public sealed record R<T>(T Value) : IRequest<int>;")]
    [InlineData("public sealed record R<T>(T Value) : IRequest<int> where T : class, IEnumerable<int>;")]
    [InlineData("public sealed record R(object Value, IComparable Key) : IRequest<int>;")]
    // Not printed: non-public, static, write-only, and indexer members.
    [InlineData("""
        public sealed record R(int Id) : IRequest<int>
        {
            private int[] hidden = [];
            internal int[] Internal => hidden;
            public static int[] Shared = [];
            public int[] WriteOnly { set => hidden = value; }
            public int this[int i] => hidden[i];
        }
        """)]
    public void MemberWithValueText_IsNotReported(string declaration)
    {
        var source = Usings + Attributed(declaration);

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAM011", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public sealed record R(int[] Ids) : IRequest<int>;")]
    [InlineData("[CacheResponse(TtlMs = 1000)]\npublic sealed record R(int[] Ids);")]
    public void RecordThatIsNotACachedRequest_IsNotReported(string declaration)
    {
        var diagnostics = CacheKeyDiagnosticTests.Run(Usings + declaration);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAM011", StringComparison.Ordinal));
    }

    [Fact]
    public void Pragma_AtTheMember_SuppressesIt()
    {
        var source = Usings + """
            [CacheResponse(TtlMs = 1000)]
            public sealed record R(
            #pragma warning disable ZAM011
                int[] Ids
            #pragma warning restore ZAM011
            ) : IRequest<int>;
            """;

        var diagnostics = CacheKeyDiagnosticTests.Run(source);

        Assert.True(DiagnosticLocationTests.One(diagnostics, "ZAM011").IsSuppressed);
    }

    // Puts [CacheResponse] on a single-line declaration; a multi-line one carries its own.
    private static string Attributed(string declaration) =>
        declaration.Contains("[CacheResponse", StringComparison.Ordinal)
            ? declaration
            : "[CacheResponse(TtlMs = 1000)]\n" + declaration;
}
