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
