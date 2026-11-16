using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

/// <summary>
/// The release check of a package with no public API surface: its version is decided
/// by the Conventional Commits since the last stable tag, and the tag's message carries
/// the commits that decided it.
/// </summary>
public sealed class ReleaseVersionTests : IDisposable
{
    private readonly ScratchRepository _repository = new("_common.sh", "_release-version.sh");

    public ReleaseVersionTests()
    {
        _repository.Commit("chore: initial");
    }

    public void Dispose() => _repository.Dispose();

    private void Baseline(string tag)
    {
        _repository.Git("tag", "--annotate", tag, "--message", tag);
    }

    private string Commit(string message) => _repository.Commit(message);

    /// <summary>Tags HEAD with a message in the shape the runbook produces: the tag, then the evidence.</summary>
    private void Tag(string tag, params string[] subjects)
    {
        var message = tag + "\n\n" + string.Concat(subjects.Select(subject => $"- {subject}\n"));
        _repository.Git("tag", "--annotate", tag, "--message", message);
    }

    private ScriptResult Check(string tag) => _repository.Script("_release-version.sh", tag);

    [Fact]
    public void A_fix_since_the_last_stable_tag_allows_the_next_patch()
    {
        Baseline("v0.1.0");
        Commit("fix: read the lock file");
        Tag("v0.1.1", "fix: read the lock file");

        var result = Check("v0.1.1");

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void A_fix_does_not_allow_a_minor()
    {
        Baseline("v0.1.0");
        Commit("fix: read the lock file");
        Tag("v0.2.0", "fix: read the lock file");

        var result = Check("v0.2.0");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("v0.2.0").And.Contain("0.1.1").And.Contain("fix: read the lock file");
    }

    [Fact]
    public void A_feature_before_one_allows_a_minor_and_refuses_a_patch()
    {
        Baseline("v0.1.0");
        Commit("feat: add the verb");
        Tag("v0.1.1", "feat: add the verb");
        Check("v0.1.1").Succeeded.Should().BeFalse();
        _repository.Git("tag", "--delete", "v0.1.1");
        Tag("v0.2.0", "feat: add the verb");

        Check("v0.2.0").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_breaking_change_before_one_is_a_minor()
    {
        Baseline("v0.1.0");
        Commit("feat!: rename the verb");
        Tag("v0.2.0", "feat!: rename the verb");

        Check("v0.2.0").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_breaking_change_footer_is_breaking()
    {
        Baseline("v1.2.3");
        Commit("fix: change a default\n\nBREAKING CHANGE: the default is now off");
        Tag("v2.0.0", "fix: change a default");

        Check("v2.0.0").Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("feat!: remove a verb", "v2.0.0", true)]
    [InlineData("feat!: remove a verb", "v1.3.0", false)]
    [InlineData("feat: add a verb", "v1.3.0", true)]
    [InlineData("feat: add a verb", "v2.0.0", false)]
    [InlineData("perf: faster restore", "v1.2.4", true)]
    [InlineData("perf: faster restore", "v1.3.0", false)]
    public void From_one_onwards_each_effect_has_its_own_number(string subject, string tag, bool accepted)
    {
        Baseline("v1.2.3");
        Commit(subject);
        Tag(tag, subject);

        Check(tag).Succeeded.Should().Be(accepted);
    }

    [Fact]
    public void The_strongest_effect_decides()
    {
        Baseline("v0.1.0");
        Commit("fix: a");
        Commit("feat: b");
        Commit("docs: c");
        Tag("v0.2.0", "feat: b");

        Check("v0.2.0").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_range_with_no_effect_calls_for_no_release()
    {
        Baseline("v0.1.0");
        Commit("docs: explain");
        Commit("chore: tidy");
        Commit("ci: speed up");
        Tag("v0.1.1", "docs: explain");

        var result = Check("v0.1.1");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("no release");
    }

    [Fact]
    public void A_tag_whose_message_omits_the_deciding_commit_is_refused()
    {
        Baseline("v0.1.0");
        Commit("fix: read the lock file");
        Tag("v0.1.1", "docs: something else");

        var result = Check("v0.1.1");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("evidence").And.Contain("fix: read the lock file");
    }

    [Fact]
    public void A_prerelease_is_judged_on_its_version_core()
    {
        Baseline("v0.1.0");
        Commit("feat: add the verb");
        Tag("v0.2.0-rc.1", "feat: add the verb");

        Check("v0.2.0-rc.1").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_later_prerelease_does_not_replace_the_last_stable_tag_as_baseline()
    {
        Baseline("v0.1.0");
        Commit("feat: add the verb");
        Tag("v0.2.0-rc.1", "feat: add the verb");
        Commit("fix: tidy the verb");
        Tag("v0.2.0", "feat: add the verb");

        Check("v0.2.0").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_repository_with_no_stable_tag_is_at_its_first_release()
    {
        Commit("feat: the first verb");
        Tag("v0.1.0", "feat: the first verb");

        var result = Check("v0.1.0");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("first release");
    }

    [Fact]
    public void Merge_commits_carry_no_effect()
    {
        Baseline("v0.1.0");
        Commit("fix: a");
        _repository.Git("switch", "--quiet", "-c", "side");
        Commit("docs: b");
        _repository.Git("switch", "--quiet", "-");
        _repository.Git("merge", "--no-ff", "--quiet", "-m", "Merge pull request #1 from side", "side");
        Tag("v0.1.1", "fix: a");

        Check("v0.1.1").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_commit_that_is_not_conventional_has_no_effect_and_is_named()
    {
        Baseline("v0.1.0");
        Commit("Update stuff");
        Commit("fix: a");
        Tag("v0.1.1", "fix: a");

        var result = Check("v0.1.1");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Error.Should().Contain("Update stuff");
    }

    [Fact]
    public void One_point_zero_after_a_zero_version_needs_the_stability_decision()
    {
        Baseline("v0.9.0");
        Commit("fix: a");
        Tag("v1.0.0", "fix: a");

        var result = Check("v1.0.0");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("1.0.0").And.Contain("ADR");
    }

    [Fact]
    public void One_point_zero_is_accepted_whatever_the_commits_when_the_decision_exists()
    {
        Baseline("v0.9.0");
        _repository.Write("docs/adr/0007-stable-surface.md", "# The public surface is stable\n");
        _repository.Git("add", "--all");
        Commit("docs: record the stability decision");
        _repository.Git("tag", "--annotate", "v1.0.0", "--message", "v1.0.0\n\n- docs: record the stability decision\ndocs/adr/0007-stable-surface.md\n");

        Check("v1.0.0").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_named_decision_that_does_not_exist_is_refused()
    {
        Baseline("v0.9.0");
        Commit("fix: a");
        _repository.Git("tag", "--annotate", "v1.0.0", "--message", "v1.0.0\n\ndocs/adr/9999-missing.md\n");

        Check("v1.0.0").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_named_file_that_does_not_declare_the_surface_stable_is_refused()
    {
        Baseline("v0.9.0");
        _repository.Write("docs/adr/0001-other.md", "# Something else\n");
        _repository.Git("add", "--all");
        Commit("fix: a");
        _repository.Git("tag", "--annotate", "v1.0.0", "--message", "v1.0.0\n\ndocs/adr/0001-other.md\n");

        Check("v1.0.0").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_repository_that_declares_a_public_api_leaves_the_version_to_the_surface_diff()
    {
        Baseline("v0.1.0");
        _repository.Write("src/Lib/PublicAPI.Shipped.txt", "#nullable enable\n");
        _repository.Git("add", "--all");
        Commit("feat: a surface");
        Tag("v0.1.1", "feat: a surface");

        var result = Check("v0.1.1");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("surface diff");
    }

    [Fact]
    public void A_tag_that_does_not_exist_is_refused()
    {
        Check("v9.9.9").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void The_release_workflow_runs_the_check_before_it_asks_for_a_credential()
    {
        var workflow = File.ReadAllText(
            Path.Combine(ScratchRepository.RepositoryRoot, ".github", "workflows", "release.yml"));

        var check = workflow.IndexOf("_release-version.sh", StringComparison.Ordinal);
        var login = workflow.IndexOf("NuGet/login", StringComparison.Ordinal);

        check.Should().BePositive("the workflow must run the version check");
        check.Should().BeLessThan(login);
        workflow.IndexOf("_release-tag.sh", StringComparison.Ordinal).Should().BeLessThan(check);
    }
}
