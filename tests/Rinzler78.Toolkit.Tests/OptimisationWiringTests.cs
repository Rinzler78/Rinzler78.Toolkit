using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

/// <summary>
/// What a generated repository carries of the optimisation policy: the pull-request build
/// switches the costly optimisations off unless it targets master, and the release leaves them on.
/// </summary>
public sealed class OptimisationWiringTests
{
    private static string Shared => Path.Combine(ScratchRepository.RepositoryRoot, "src", "Rinzler78.Templates", "templates", "shared", ".github", "workflows");

    private static string Section(string workflow, string step)
    {
        var start = workflow.IndexOf($"- name: {step}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the workflow has a step named {step}");
        var next = workflow.IndexOf("- name:", start + 1, StringComparison.Ordinal);
        return next < 0 ? workflow[start..] : workflow[start..next];
    }

    [Fact]
    public void The_continuous_integration_build_turns_the_costly_optimisations_off_unless_it_targets_master()
    {
        var ci = File.ReadAllText(Path.Combine(Shared, "ci.yml"));

        Section(ci, "Build").Should().Contain("RinzlerCostlyOptimisations:")
            .And.Contain("github.base_ref == 'master'")
            .And.Contain("'true' || 'false'");
    }

    [Fact]
    public void The_release_build_never_turns_them_off()
    {
        File.ReadAllText(Path.Combine(Shared, "release.yml")).Should().NotContain("RinzlerCostlyOptimisations");
    }
}
