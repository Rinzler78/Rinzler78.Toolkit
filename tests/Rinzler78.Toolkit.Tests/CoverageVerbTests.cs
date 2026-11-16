using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class CoverageVerbTests : IDisposable
{
    private readonly ScratchRepository _repository = new("_common.sh", "_affected.sh", "_coverage-gate.sh", "coverage.sh", "test.sh");

    public void Dispose() => _repository.Dispose();

    // `dotnet` stands in for the whole chain: `tool restore` does nothing, `dotnet-coverage
    // collect` writes the report it was asked for (an assembly covered at $PERCENT), and
    // `msbuild` answers the property queries of the gate.
    private void Dotnet(string percent)
    {
        _repository.Write("src/Alpha/Alpha.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        _repository.Write("tests/Alpha.Tests/Alpha.Tests.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        _repository.Shim("dotnet", $$"""
            case "$1" in
              tool) ;;
              dotnet-coverage)
                out=
                while (($#)); do [[ "$1" == --output ]] && out=$2; shift; done
                mkdir -p "$(dirname "$out")"
                echo '<coverage><packages><package line-rate="{{percent}}" branch-rate="{{percent}}" name="Alpha"></package></packages></coverage>' >"$out"
                ;;
              msbuild)
                [[ "$3" == -getProperty:AssemblyName ]] && echo Alpha || echo Enforced ;;
            esac
            """);
        // test.sh would run the real suite: the collect stand-in never calls it.
    }

    [Fact]
    public void Without_the_gate_the_report_is_collected_and_nothing_is_judged()
    {
        Dotnet("0.10");

        var result = _repository.Script("coverage.sh");

        result.Succeeded.Should().BeTrue(result.Error);
        File.Exists(Path.Combine(_repository.Root, "artifacts/coverage/coverage.cobertura.xml")).Should().BeTrue();
    }

    [Fact]
    public void With_the_gate_a_report_under_the_floor_fails_the_verb()
    {
        Dotnet("0.10");

        var result = _repository.Script("coverage.sh", "--gate");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha");
    }

    [Fact]
    public void With_the_gate_a_report_over_the_floor_passes()
    {
        Dotnet("0.95");

        var result = _repository.Script("coverage.sh", "--gate");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("the coverage gate holds");
    }

    [Fact]
    public void The_diff_revision_reaches_the_gate()
    {
        Dotnet("0.95");
        _repository.Commit("base");

        var result = _repository.Script("coverage.sh", "--gate", "--since", "HEAD");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("no C# line changed since HEAD");
    }
}
