using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class ReleaseTagTests : IDisposable
{
    private readonly ScratchRepository _repository = new("_common.sh", "_release-tag.sh");

    public ReleaseTagTests()
    {
        _repository.Commit("initial");
        _repository.Git("update-ref", "refs/remotes/origin/master", "HEAD");
        _repository.GitHubVerifies(verified: true, reason: "valid");
    }

    public void Dispose() => _repository.Dispose();

    [Fact]
    public void A_signed_annotated_release_tag_on_master_publishes_its_version()
    {
        _repository.Git("tag", "--annotate", "v1.2.3", "--message", "v1.2.3");

        var result = _repository.Script("_release-tag.sh", "v1.2.3");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("version=1.2.3").And.Contain("prerelease=false");
    }

    [Theory]
    [InlineData("v1.2.3-alpha.1")]
    [InlineData("v1.2.3-beta.0")]
    [InlineData("v1.2.3-rc.10")]
    public void A_prerelease_tag_publishes_a_prerelease(string tag)
    {
        _repository.Git("tag", "--annotate", tag, "--message", tag);

        var result = _repository.Script("_release-tag.sh", tag);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain($"version={tag[1..]}").And.Contain("prerelease=true");
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3-rc2")]
    [InlineData("v1.2.3-rc.01")]
    [InlineData("v1.2.3-preview.1")]
    [InlineData("v1.2.3-rc.1+build")]
    public void A_tag_outside_the_version_forms_is_refused(string tag)
    {
        _repository.Git("tag", "--annotate", tag, "--message", tag);

        var result = _repository.Script("_release-tag.sh", tag);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain($"'{tag}' is not a release tag");
    }

    [Fact]
    public void A_lightweight_tag_is_refused()
    {
        _repository.Git("tag", "v1.2.3");

        var result = _repository.Script("_release-tag.sh", "v1.2.3");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("'v1.2.3' is a lightweight tag");
    }

    [Fact]
    public void A_tag_on_a_commit_master_does_not_contain_is_refused()
    {
        _repository.Commit("work not yet promoted");
        _repository.Git("tag", "--annotate", "v1.2.3", "--message", "v1.2.3");

        var result = _repository.Script("_release-tag.sh", "v1.2.3");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("'v1.2.3' points at a commit origin/master does not contain");
    }

    [Fact]
    public void A_tag_GitHub_does_not_verify_is_refused_with_its_reason()
    {
        _repository.GitHubVerifies(verified: false, reason: "unsigned");
        _repository.Git("tag", "--annotate", "v1.2.3", "--message", "v1.2.3");

        var result = _repository.Script("_release-tag.sh", "v1.2.3");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("'v1.2.3' carries no verified signature: unsigned");
    }
}
