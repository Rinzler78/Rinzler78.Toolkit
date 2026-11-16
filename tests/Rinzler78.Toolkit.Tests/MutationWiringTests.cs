using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

/// <summary>
/// What a generated repository carries of mutation testing: the tool, and a workflow only
/// where an assembly can be mutated.
/// </summary>
public sealed class MutationWiringTests
{
    private static string Templates => Path.Combine(ScratchRepository.RepositoryRoot, "src", "Rinzler78.Templates", "templates");

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([ScratchRepository.RepositoryRoot, .. path]));

    [Fact]
    public void The_library_template_runs_the_diff_on_a_pull_request_and_the_full_run_nightly()
    {
        var workflow = File.ReadAllText(Path.Combine(Templates, "rinzler-lib", ".github", "workflows", "mutation.yml"));

        workflow.Should().Contain("pull_request:").And.Contain("schedule:");
        workflow.Should().Contain("./scripts/mutation.sh --since \"origin/$BASE_REF\" --baseline");
        workflow.Should().Contain("./scripts/_mutation-store.sh fetch").And.Contain("./scripts/_mutation-store.sh publish");
    }

    [Fact]
    public void The_baseline_is_fetched_before_the_diff_run_reads_it()
    {
        var workflow = File.ReadAllText(Path.Combine(Templates, "rinzler-lib", ".github", "workflows", "mutation.yml"));

        workflow.IndexOf("_mutation-store.sh fetch", StringComparison.Ordinal)
            .Should().BeLessThan(workflow.IndexOf("mutation.sh --since", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_nightly_may_write_to_the_repository()
    {
        var workflow = File.ReadAllText(Path.Combine(Templates, "rinzler-lib", ".github", "workflows", "mutation.yml"));

        workflow.Split("contents: write").Length.Should().Be(2, "exactly one job asks for write access");
        workflow.IndexOf("contents: write", StringComparison.Ordinal)
            .Should().BeGreaterThan(workflow.IndexOf("nightly:", StringComparison.Ordinal));
        workflow.Should().Contain("permissions:\n  contents: read");
    }

    [Fact]
    public void A_manual_run_from_another_branch_cannot_publish_its_score()
    {
        var workflow = File.ReadAllText(Path.Combine(Templates, "rinzler-lib", ".github", "workflows", "mutation.yml"));

        workflow.Should().Contain("github.ref == format('refs/heads/{0}', github.event.repository.default_branch)");
    }

    [Theory]
    [InlineData("rinzler-binding")]
    [InlineData("rinzler-app")]
    public void A_template_without_a_portable_library_carries_no_mutation_job(string template)
    {
        File.Exists(Path.Combine(Templates, template, ".github", "workflows", "mutation.yml")).Should().BeFalse();
        File.Exists(Path.Combine(Templates, "shared", ".github", "workflows", "mutation.yml")).Should().BeFalse();
    }

    [Fact]
    public void Stryker_is_pinned_exactly_in_the_tool_manifest_every_repository_shares()
    {
        var manifest = Read(".config", "dotnet-tools.json");

        manifest.Should().MatchRegex("\"dotnet-stryker\": \\{\\s*\"version\": \"\\d+\\.\\d+\\.\\d+\"");
        Read("src", "Rinzler78.Templates", "templates", "shared", ".config", "dotnet-tools.json").Should().Be(manifest);
    }
}
