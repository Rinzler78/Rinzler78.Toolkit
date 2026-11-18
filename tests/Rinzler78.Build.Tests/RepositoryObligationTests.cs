using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

/// <summary>
/// The obligations a repository declares and the build enforces: a README fragment, a
/// project reference that stays inside its repository, and an ADR behind a reduced or
/// exempt coverage contract. A project declaring none of them builds.
/// </summary>
public class RepositoryObligationTests
{
    private const string Library = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
        </Project>
        """;

    [Fact]
    public async Task A_project_declaring_no_obligation_builds()
    {
        using var consumer = new ScratchConsumer("");

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_packable_project_whose_readme_lacks_the_required_fragment_is_refused_by_name()
    {
        using var consumer = new ScratchConsumer(
            "<IsPackable>true</IsPackable>",
            """<ItemGroup><RinzlerRequiredReadmeFragment Include="not affiliated with HERE" /></ItemGroup>""");
        consumer.Write("README.md", "# Consumer\n");

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("README.md must contain: not affiliated with HERE");
    }

    [Fact]
    public async Task A_readme_carrying_every_fragment_is_accepted()
    {
        using var consumer = new ScratchConsumer(
            "<IsPackable>true</IsPackable>",
            """<ItemGroup><RinzlerRequiredReadmeFragment Include="not affiliated with HERE;MIT licence" /></ItemGroup>""");
        consumer.Write("README.md", "# Consumer\nThis is not affiliated with HERE. MIT licence.\n");

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_readme_is_refused_when_a_fragment_is_required()
    {
        using var consumer = new ScratchConsumer(
            "<IsPackable>true</IsPackable>",
            """<ItemGroup><RinzlerRequiredReadmeFragment Include="not affiliated with HERE" /></ItemGroup>""");

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("README.md");
    }

    [Fact]
    public async Task A_project_shipping_no_package_owes_no_readme_fragment()
    {
        using var consumer = new ScratchConsumer(
            "",
            """<ItemGroup><RinzlerRequiredReadmeFragment Include="not affiliated with HERE" /></ItemGroup>""");

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_project_reference_into_another_repository_is_refused_naming_the_target()
    {
        using var consumer = new ScratchConsumer(
            "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
            """<ItemGroup><ProjectReference Include="other/Lib/Lib.csproj" /></ItemGroup>""");
        consumer.Write("Directory.Packages.props", "<Project />");
        consumer.Write("other/Directory.Packages.props", "<Project />");
        consumer.Write("other/Lib/Lib.csproj", Library);

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Lib.csproj").And.Contain("another repository");
    }

    [Fact]
    public async Task A_project_reference_inside_the_same_repository_is_accepted()
    {
        using var consumer = new ScratchConsumer(
            "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
            """<ItemGroup><ProjectReference Include="shared/Lib/Lib.csproj" /></ItemGroup>""");
        consumer.Write("Directory.Packages.props", "<Project />");
        consumer.Write("shared/Lib/Lib.csproj", Library);

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_repository_without_a_boundary_marker_is_not_checked()
    {
        using var consumer = new ScratchConsumer(
            "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
            """<ItemGroup><ProjectReference Include="other/Lib/Lib.csproj" /></ItemGroup>""");
        consumer.Write("other/Directory.Packages.props", "<Project />");
        consumer.Write("other/Lib/Lib.csproj", Library);

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Reduced")]
    [InlineData("Exempt")]
    public async Task A_shipped_project_with_a_lowered_coverage_contract_and_no_adr_is_refused(string contract)
    {
        using var consumer = new ScratchConsumer($"""
            <IsPackable>true</IsPackable>
            <RinzlerCoverageContract>{contract}</RinzlerCoverageContract>
            """);

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain($"RinzlerCoverageContract {contract} requires RinzlerCoverageAdr");
    }

    [Fact]
    public async Task A_lowered_contract_naming_an_adr_that_does_not_exist_is_refused()
    {
        using var consumer = new ScratchConsumer("""
            <IsPackable>true</IsPackable>
            <RinzlerCoverageContract>Reduced</RinzlerCoverageContract>
            <RinzlerCoverageAdr>docs/adr/ADR-0001-wrappers.md</RinzlerCoverageAdr>
            """);

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("ADR-0001-wrappers.md");
    }

    [Fact]
    public async Task A_lowered_contract_naming_an_existing_adr_builds()
    {
        using var consumer = new ScratchConsumer("""
            <IsPackable>true</IsPackable>
            <RinzlerCoverageContract>Reduced</RinzlerCoverageContract>
            <RinzlerCoverageAdr>docs/adr/ADR-0001-wrappers.md</RinzlerCoverageAdr>
            """);
        consumer.Write("docs/adr/ADR-0001-wrappers.md", "# ADR");

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("<IsTestProject>true</IsTestProject>")]
    [InlineData("<IsPackable>false</IsPackable>")]
    public async Task A_project_shipping_nothing_may_be_exempt_without_an_adr(string shipsNothing)
    {
        using var consumer = new ScratchConsumer($"""
            {shipsNothing}
            <RinzlerCoverageContract>Exempt</RinzlerCoverageContract>
            """);

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task An_enforced_contract_owes_no_adr()
    {
        using var consumer = new ScratchConsumer("""
            <IsPackable>true</IsPackable>
            <RinzlerCoverageContract>Enforced</RinzlerCoverageContract>
            """);

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }
}
