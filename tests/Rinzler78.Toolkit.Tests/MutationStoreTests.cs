using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class MutationStoreTests : IDisposable
{
    private readonly ScratchRepository _repository = new("_common.sh", "_mutation-store.sh");

    public MutationStoreTests() => _repository.AddOrigin();

    public void Dispose() => _repository.Dispose();

    private void Recorded(string scores, string badge)
    {
        _repository.Write("artifacts/mutation/scores.txt", scores);
        _repository.Write("artifacts/mutation/badge.json", badge);
    }

    private string Remote(string revision, string path) =>
        _repository.Git("--git-dir", Path.Combine(_repository.Root + "-origin"), "show", $"{revision}:{path}");

    private int RemoteCommits() =>
        _repository.Git("--git-dir", Path.Combine(_repository.Root + "-origin"), "rev-list", "--count", "badges").Trim() switch
        {
            var count => int.Parse(count, System.Globalization.CultureInfo.InvariantCulture),
        };

    [Fact]
    public void Publishing_puts_the_scores_and_the_badge_on_an_orphan_branch()
    {
        Recorded("Alpha=91.5\n", "{\"message\":\"91.5%\"}\n");

        var result = _repository.Script("_mutation-store.sh", "publish");

        result.Succeeded.Should().BeTrue(result.Error);
        Remote("badges", "scores.txt").Should().Be("Alpha=91.5\n");
        Remote("badges", "mutation.json").Should().Be("{\"message\":\"91.5%\"}\n");
        _repository.Git("--git-dir", Path.Combine(_repository.Root + "-origin"), "rev-list", "--max-parents=0", "badges")
            .Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void Publishing_does_not_touch_the_working_tree_or_the_current_branch()
    {
        _repository.Commit("work");
        var head = _repository.Git("rev-parse", "HEAD");
        Recorded("Alpha=91.5\n", "{}\n");

        _repository.Script("_mutation-store.sh", "publish").Succeeded.Should().BeTrue();

        _repository.Git("rev-parse", "HEAD").Should().Be(head);
        _repository.Git("branch", "--list", "badges").Should().BeEmpty();
    }

    [Fact]
    public void Publishing_a_new_score_adds_a_commit_on_top_of_the_history()
    {
        Recorded("Alpha=91.5\n", "{}\n");
        _repository.Script("_mutation-store.sh", "publish").Succeeded.Should().BeTrue();
        Recorded("Alpha=93\n", "{}\n");

        var result = _repository.Script("_mutation-store.sh", "publish");

        result.Succeeded.Should().BeTrue(result.Error);
        RemoteCommits().Should().Be(2);
        Remote("badges", "scores.txt").Should().Be("Alpha=93\n");
    }

    [Fact]
    public void Publishing_an_unchanged_score_adds_no_commit()
    {
        Recorded("Alpha=91.5\n", "{}\n");
        _repository.Script("_mutation-store.sh", "publish").Succeeded.Should().BeTrue();

        var result = _repository.Script("_mutation-store.sh", "publish");

        result.Succeeded.Should().BeTrue(result.Error);
        RemoteCommits().Should().Be(1);
    }

    [Fact]
    public void Publishing_without_a_recorded_score_is_refused()
    {
        var result = _repository.Script("_mutation-store.sh", "publish");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("run mutation.sh");
    }

    [Fact]
    public void Fetching_returns_the_published_scores()
    {
        Recorded("Alpha=91.5\nBeta=80\n", "{}\n");
        _repository.Script("_mutation-store.sh", "publish").Succeeded.Should().BeTrue();

        var result = _repository.Script("_mutation-store.sh", "fetch", "baseline.txt");

        result.Succeeded.Should().BeTrue(result.Error);
        File.ReadAllText(Path.Combine(_repository.Root, "baseline.txt")).Should().Be("Alpha=91.5\nBeta=80\n");
    }

    [Fact]
    public void Fetching_before_any_publication_leaves_no_baseline_and_is_not_an_error()
    {
        var result = _repository.Script("_mutation-store.sh", "fetch", "baseline.txt");

        result.Succeeded.Should().BeTrue(result.Error);
        File.Exists(Path.Combine(_repository.Root, "baseline.txt")).Should().BeFalse();
    }

    [Fact]
    public void An_unreachable_origin_is_an_error_not_a_first_run()
    {
        _repository.Git("remote", "set-url", "origin", Path.Combine(_repository.Root, "nowhere"));

        var result = _repository.Script("_mutation-store.sh", "fetch", "baseline.txt");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("origin");
    }

    [Fact]
    public void An_unknown_verb_is_refused()
    {
        var result = _repository.Script("_mutation-store.sh", "bogus");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("usage");
    }
}
