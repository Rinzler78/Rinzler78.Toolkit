using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

/// <summary>
/// What the build reads from the compiled assembly rather than from the source: a layer that
/// owns its surface may not expose a bare double, and no layer may reference an assembly its
/// policy forbids. Both are declared by the consumer; a project declaring neither builds.
/// </summary>
public class CompiledAssemblyInspectionTests
{
    private const string OwnsSurface = "<RinzlerForbidBareDouble>true</RinzlerForbidBareDouble>";

    private static async Task<BuildResult> BuildAsync(string source, string properties = "", string items = "")
    {
        // The fixtures are written to show a signature, not to satisfy the analysers: only the
        // inspection under test may fail them.
        using var consumer = new ScratchConsumer("<TreatWarningsAsErrors>false</TreatWarningsAsErrors>" + properties, items);
        consumer.Write("Source.cs", source);
        return await consumer.BuildAsync().ConfigureAwait(false);
    }

    private static string Api(string member) => $$"""
        namespace Geo;

        /// <summary>A public type.</summary>
        public class Place
        {
            {{member}}
        }
        """;

    [Theory]
    [InlineData("/// <summary>m</summary>\n    public void Move(double latitude) { }", "Place.Move")]
    [InlineData("/// <summary>m</summary>\n    public double Latitude() => 0;", "Place.Latitude")]
    [InlineData("/// <summary>m</summary>\n    public double? Maybe() => null;", "Place.Maybe")]
    [InlineData("/// <summary>m</summary>\n    public double Latitude { get; set; }", "Place.Latitude")]
    [InlineData("/// <summary>m</summary>\n    public double Field;", "Place.Field")]
    [InlineData("/// <summary>m</summary>\n    protected void Move(double latitude) { }", "Place.Move")]
    [InlineData("/// <summary>m</summary>\n    public void Move(ref double latitude) { }", "Place.Move")]
    [InlineData("/// <summary>m</summary>\n    public Place(double latitude) { }", "Place..ctor")]
    public async Task A_layer_that_owns_its_surface_may_not_expose_a_bare_double(string member, string named)
    {
        var result = await BuildAsync(Api(member), OwnsSurface);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("bare double").And.Contain(named);
    }

    [Theory]
    [InlineData("/// <summary>m</summary>\n    public (double, double) Position() => (0, 0);", "Place.Position")]
    [InlineData("/// <summary>m</summary>\n    public double[] Track() => [];", "Place.Track")]
    [InlineData("/// <summary>m</summary>\n    public System.Collections.Generic.List<double> Track() => [];", "Place.Track")]
    [InlineData("/// <summary>m</summary>\n    public System.Threading.Tasks.Task<double> LatitudeAsync() => null!;", "Place.LatitudeAsync")]
    [InlineData("/// <summary>m</summary>\n    public void Move(params double[] path) { }", "Place.Move")]
    [InlineData("/// <summary>m</summary>\n    public double? Latitude { get; set; }", "Place.Latitude")]
    [InlineData("/// <summary>m</summary>\n    protected internal void Move(double latitude) { }", "Place.Move")]
    [InlineData("/// <summary>m</summary>\n    public const double Pi = 3.14;", "Place.Pi")]
    [InlineData("/// <summary>m</summary>\n    public event System.Action<double> Moved { add { } remove { } }", "Place.Moved")]
    [InlineData("/// <summary>m</summary>\n    public delegate void Step(double latitude);", "Place.Step.Invoke")]
    public async Task A_double_inside_a_tuple_array_generic_delegate_or_event_is_a_bare_double(string member, string named)
    {
        var result = await BuildAsync(Api(member), OwnsSurface);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("bare double").And.Contain(named);
    }

    [Fact]
    public async Task A_double_in_a_function_pointer_signature_is_found()
    {
        var result = await BuildAsync(
            Api("/// <summary>m</summary>\n    public unsafe void Call(delegate*<double, void> callback) { }"),
            OwnsSurface + "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>");

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Place.Call");
    }

    [Fact]
    public async Task A_namespace_that_happens_to_be_called_double_is_not_a_double()
    {
        var result = await BuildAsync("""
            namespace Acme.@double
            {
                /// <summary>A thing.</summary>
                public class Thing { }
            }

            namespace Geo
            {
                /// <summary>A holder.</summary>
                public class Holder
                {
                    /// <summary>m</summary>
                    public Acme.@double.Thing Make() => new();
                }
            }
            """, OwnsSurface);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_double_in_the_public_member_of_a_nested_public_type_or_an_interface_is_found()
    {
        var nested = await BuildAsync("""
            namespace Geo;

            /// <summary>Outer.</summary>
            public class Outer
            {
                /// <summary>Inner.</summary>
                public class Inner
                {
                    /// <summary>m</summary>
                    public void Move(double latitude) { }
                }
            }
            """, OwnsSurface);
        var contract = await BuildAsync("""
            namespace Geo;

            /// <summary>A contract.</summary>
            public interface IPlace
            {
                /// <summary>m</summary>
                void Move(double latitude);
            }
            """, OwnsSurface);

        nested.Succeeded.Should().BeFalse();
        nested.Output.Should().Contain("Geo.Outer.Inner.Move");
        contract.Succeeded.Should().BeFalse();
        contract.Output.Should().Contain("Geo.IPlace.Move");
    }

    [Fact]
    public async Task A_record_exposing_a_double_is_found_and_an_allowance_by_its_name_covers_it()
    {
        const string record = """
            namespace Geo;

            /// <summary>A record.</summary>
            /// <param name="Latitude">Latitude.</param>
            public record Coordinate(double Latitude);
            """;

        var refused = await BuildAsync(record, OwnsSurface);
        var allowed = await BuildAsync(
            record,
            OwnsSurface,
            """<ItemGroup><RinzlerAllowBareDouble Include="Geo.Coordinate" /></ItemGroup>""");

        refused.Succeeded.Should().BeFalse();
        allowed.Succeeded.Should().BeTrue(allowed.Output);
    }

    [Fact]
    public async Task A_generic_type_is_allowed_by_its_name_with_its_arity()
    {
        const string box = """
            namespace Geo;

            /// <summary>A box.</summary>
            /// <typeparam name="T">Content.</typeparam>
            public class Box<T>
            {
                /// <summary>m</summary>
                public void Put(double weight) { }
            }
            """;

        var allowed = await BuildAsync(
            box,
            OwnsSurface,
            """<ItemGroup><RinzlerAllowBareDouble Include="Geo.Box`1" /></ItemGroup>""");

        allowed.Succeeded.Should().BeTrue(allowed.Output);
    }

    [Theory]
    [InlineData("system.net.http")]
    [InlineData("SYSTEM.NET.HTTP")]
    public async Task A_forbidden_reference_is_matched_without_regard_to_case(string spelled)
    {
        var result = await BuildAsync(
            UsesHttp,
            "",
            $"""<ItemGroup><RinzlerForbiddenDependency Include="{spelled}" /></ItemGroup>""");

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("references System.Net.Http");
    }

    [Theory]
    [InlineData("internal void Move(double latitude) { }")]
    [InlineData("private double _latitude;")]
    [InlineData("/// <summary>m</summary>\n    public void Move(float latitude) { }")]
    [InlineData("/// <summary>m</summary>\n    public void Move(int latitude) { }")]
    public async Task What_is_not_public_or_not_a_double_is_accepted(string member)
    {
        var result = await BuildAsync(Api(member), OwnsSurface);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_public_member_of_an_internal_type_is_not_public_api()
    {
        var result = await BuildAsync("""
            namespace Geo;

            internal static class Hidden
            {
                public static void Move(double latitude) { }
            }
            """, OwnsSurface);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_layer_that_does_not_declare_the_rule_may_expose_a_double()
    {
        var result = await BuildAsync(Api("/// <summary>m</summary>\n    public void Move(double latitude) { }"));

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Theory]
    [InlineData("RinzlerAllowBareDouble", "Geo.Place")]
    [InlineData("RinzlerAllowBareDoubleNamespace", "Geo")]
    public async Task A_type_or_namespace_the_consumer_allows_may_take_a_double(string item, string allowed)
    {
        var result = await BuildAsync(
            Api("/// <summary>m</summary>\n    public Place(double latitude) { }"),
            OwnsSurface,
            $"""<ItemGroup><{item} Include="{allowed}" /></ItemGroup>""");

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task An_allowance_for_another_type_does_not_cover_this_one()
    {
        var result = await BuildAsync(
            Api("/// <summary>m</summary>\n    public Place(double latitude) { }"),
            OwnsSurface,
            """
            <ItemGroup>
              <RinzlerAllowBareDouble Include="Geo.Other;Geo.PlaceMat" />
              <RinzlerAllowBareDoubleNamespace Include="Elsewhere;Ge" />
            </ItemGroup>
            """);

        result.Succeeded.Should().BeFalse();
    }

    private const string UsesHttp = """
        namespace Geo;

        /// <summary>Reaches outward.</summary>
        public static class Gateway
        {
            /// <summary>Builds a client.</summary>
            public static object Create() => new System.Net.Http.HttpClient();
        }
        """;

    [Fact]
    public async Task A_reference_to_a_forbidden_assembly_fails_naming_it()
    {
        var result = await BuildAsync(
            UsesHttp,
            "",
            """<ItemGroup><RinzlerForbiddenDependency Include="System.Net.Http" /></ItemGroup>""");

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("references System.Net.Http");
    }

    [Fact]
    public async Task An_assembly_that_does_not_reference_a_forbidden_one_builds()
    {
        var result = await BuildAsync(
            UsesHttp,
            "",
            """<ItemGroup><RinzlerForbiddenDependency Include="Microsoft.Extensions.Http" /></ItemGroup>""");

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_project_declaring_no_policy_is_not_inspected()
    {
        var result = await BuildAsync(UsesHttp);

        result.Succeeded.Should().BeTrue(result.Output);
    }
}
