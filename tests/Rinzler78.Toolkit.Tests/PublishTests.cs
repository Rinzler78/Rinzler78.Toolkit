using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class PublishTests : IDisposable
{
    private readonly ScratchRepository _repository = new("_common.sh", "publish.sh");

    public void Dispose() => _repository.Dispose();

    private ScriptResult Verify(string expected) =>
        _repository.Script("publish.sh", "--verify", "--expect", expected);

    [Fact]
    public void Packages_whose_manifests_declare_the_tag_version_pass()
    {
        _repository.Package("Scratch.1.2.3-rc.1.nupkg", "1.2.3-rc.1");

        var result = Verify("1.2.3-rc.1");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("1 package(s), all at version 1.2.3-rc.1");
    }

    [Fact]
    public void A_package_renamed_to_the_tag_version_is_refused_on_its_manifest()
    {
        _repository.Package("Scratch.1.2.3.nupkg", "1.2.4-alpha.0.2");

        var result = Verify("1.2.3");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Scratch.1.2.3.nupkg declares version 1.2.4-alpha.0.2, not 1.2.3");
    }

    [Fact]
    public void A_version_element_formatted_over_several_lines_is_read()
    {
        _repository.PackageWithVersionElement("Scratch.1.2.3.nupkg", "<version>\n      1.2.3\n    </version>");

        var result = Verify("1.2.3");

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void Verification_needs_no_credential()
    {
        _repository.Package("Scratch.1.2.3.nupkg", "1.2.3");

        var result = Verify("1.2.3");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Error.Should().NotContain("NUGET_API_KEY");
    }

    [Fact]
    public void Nothing_to_publish_is_refused()
    {
        var result = Verify("1.2.3");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("no package to publish");
    }
}
