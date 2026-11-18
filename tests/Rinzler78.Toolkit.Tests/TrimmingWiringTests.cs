using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

/// <summary>
/// What a generated repository carries of the trimming gate: a workflow on the promotion
/// pull request into master, and only where a library can be Committed.
/// </summary>
public sealed class TrimmingWiringTests
{
    private static string Templates => Path.Combine(ScratchRepository.RepositoryRoot, "src", "Rinzler78.Templates", "templates");

    private static string Workflow => File.ReadAllText(Path.Combine(Templates, "rinzler-lib", ".github", "workflows", "trimming.yml"));

    [Fact]
    public void The_gate_runs_on_a_pull_request_into_master_and_nowhere_else()
    {
        Workflow.Should().Contain("pull_request:").And.Contain("branches: [master]");
        Workflow.Should().NotContain("push:").And.NotContain("schedule:").And.NotContain("workflow_dispatch:");
    }

    [Fact]
    public void The_gate_checks_the_readme_and_compiles_ahead_of_time()
    {
        Workflow.Should().Contain("./scripts/trimming.sh --check --aot");
    }

    [Fact]
    public void The_restore_is_locked_and_the_native_toolchain_present_before_the_gate_runs()
    {
        var gate = Workflow.IndexOf("./scripts/trimming.sh --check --aot", StringComparison.Ordinal);

        Workflow.IndexOf("dotnet restore --locked-mode", StringComparison.Ordinal).Should().BeInRange(0, gate - 1);
        Workflow.IndexOf("zlib1g-dev", StringComparison.Ordinal).Should().BeInRange(0, gate - 1);
    }

    [Fact]
    public void The_sizes_are_posted_on_the_pull_request_whatever_the_verdict_and_replace_the_previous_post()
    {
        Workflow.Should().Contain("if: always()").And.Contain("artifacts/trimming/summary.md");
        Workflow.Should().Contain("trimming-summary", "the post is found again by its marker, so that a push edits it rather than adding another");
        Workflow.IndexOf("if: always()", StringComparison.Ordinal)
            .Should().BeGreaterThan(Workflow.IndexOf("./scripts/trimming.sh --check --aot", StringComparison.Ordinal));
    }

    [Fact]
    public void The_token_may_comment_and_may_not_write_to_the_repository()
    {
        Workflow.Should().Contain("contents: read").And.Contain("pull-requests: write");
        Workflow.Should().NotContain("contents: write");
    }

    [Theory]
    [InlineData("rinzler-binding")]
    [InlineData("rinzler-app")]
    public void A_template_whose_libraries_cannot_be_committed_carries_no_trimming_workflow(string template)
    {
        File.Exists(Path.Combine(Templates, template, ".github", "workflows", "trimming.yml")).Should().BeFalse();
        File.Exists(Path.Combine(Templates, "shared", ".github", "workflows", "trimming.yml")).Should().BeFalse();
    }
}
