using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class MutationTests : IDisposable
{
    private readonly ScratchRepository _repository = new("_common.sh", "_affected.sh", "mutation.sh");

    public void Dispose() => _repository.Dispose();

    // `dotnet` is the only tool the verb drives. The stand-in answers an msbuild
    // property query from the project's name (an "Exempt" project is exempt), and a
    // Stryker run with the score recorded beside the project, failing under --break-at
    // the way Stryker does.
    private void Dotnet() => _repository.Shim("dotnet", """
        case "$1" in
          msbuild)
            [[ "$2" == *Exempt* ]] && echo Exempt || echo Enforced ;;
          stryker)
            project=
            floor=0
            while (($#)); do
              case "$1" in
                --project) project=$2; shift ;;
                --break-at) floor=$2; shift ;;
              esac
              shift
            done
            name=$(basename "$project" .csproj)
            score=$(cat "$PWD/.scores/$name" 2>/dev/null || true)
            [[ -z "$score" ]] && { echo "Stryker was unable to calculate a mutation score"; exit 0; }
            echo "The final mutation score is $score %"
            awk -v s="$score" -v b="$floor" 'BEGIN { exit (s + 0 < b + 0) }' ;;
        esac
        """);

    private void Project(string name, params string[] testedBy)
    {
        _repository.Write($"src/{name}/{name}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        foreach (var test in testedBy)
        {
            _repository.Write(
                $"tests/{test}/{test}.csproj",
                $"<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include=\"../../src/{name}/{name}.csproj\" /></ItemGroup></Project>");
        }
    }

    private void Score(string project, string score) => _repository.Write($".scores/{project}", score);

    [Fact]
    public void An_exempt_project_is_not_mutated()
    {
        Dotnet();
        Project("Library", "Library.Tests");
        Project("Exempt.Binding", "Exempt.Binding.Tests");
        Score("Library", "90");

        var result = _repository.Script("mutation.sh");

        result.Succeeded.Should().BeTrue(result.Error);
        var stryker = _repository.CallsOf("dotnet").Split('\n').Where(line => line.StartsWith("stryker", StringComparison.Ordinal));
        stryker.Should().ContainSingle().Which.Should().Contain("Library.csproj");
    }

    [Fact]
    public void A_repository_with_nothing_to_mutate_runs_no_mutation()
    {
        Dotnet();
        Project("Exempt.Binding", "Exempt.Binding.Tests");

        var result = _repository.Script("mutation.sh");

        result.Succeeded.Should().BeTrue(result.Error);
        result.Output.Should().Contain("no project to mutate");
        _repository.CallsOf("dotnet").Should().NotContain("stryker");
    }

    [Fact]
    public void A_full_run_records_each_score_and_a_badge_of_the_lowest()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Project("Beta", "Beta.Tests");
        Score("Alpha", "91.5");
        Score("Beta", "78");

        var result = _repository.Script("mutation.sh");

        result.Succeeded.Should().BeTrue(result.Error);
        File.ReadAllText(Path.Combine(_repository.Root, "artifacts/mutation/scores.txt"))
            .Should().Be("Alpha=91.5\nBeta=78\n");
        File.ReadAllText(Path.Combine(_repository.Root, "artifacts/mutation/badge.json"))
            .Should().Contain("\"message\":\"78%\"").And.Contain("\"color\":\"yellow\"");
    }

    [Theory]
    [InlineData("95", "brightgreen")]
    [InlineData("85", "brightgreen")]
    [InlineData("84.9", "yellow")]
    [InlineData("70", "yellow")]
    public void The_badge_is_green_from_the_target_and_yellow_from_the_break_threshold(string score, string color)
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", score);

        _repository.Script("mutation.sh").Succeeded.Should().BeTrue();

        File.ReadAllText(Path.Combine(_repository.Root, "artifacts/mutation/badge.json"))
            .Should().Contain($"\"color\":\"{color}\"");
    }

    [Fact]
    public void A_run_under_the_break_threshold_fails_and_names_the_project()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "69.9");

        var result = _repository.Script("mutation.sh");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha");
        File.Exists(Path.Combine(_repository.Root, "artifacts/mutation/badge.json")).Should().BeFalse();
    }

    [Fact]
    public void The_thresholds_and_the_runner_are_the_specified_ones()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "90");

        _repository.Script("mutation.sh").Succeeded.Should().BeTrue();

        _repository.CallsOf("dotnet").Should()
            .Contain("--test-runner mtp").And.Contain("--break-at 70")
            .And.Contain("--threshold-high 85").And.Contain("--threshold-low 70");
    }

    [Fact]
    public void No_test_project_is_named_because_the_solution_already_finds_them()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "90");

        _repository.Script("mutation.sh").Succeeded.Should().BeTrue();

        _repository.CallsOf("dotnet").Should().Contain("--project src/Alpha/Alpha.csproj").And.NotContain("--test-project");
    }

    [Fact]
    public void A_diff_run_is_scoped_to_the_revision_and_publishes_nothing()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "90");

        var result = _repository.Script("mutation.sh", "--since", "origin/develop");

        result.Succeeded.Should().BeTrue(result.Error);
        _repository.CallsOf("dotnet").Should().Contain("--since:origin/develop");
        File.Exists(Path.Combine(_repository.Root, "artifacts/mutation/scores.txt")).Should().BeFalse();
    }

    [Fact]
    public void A_diff_without_a_mutant_passes()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");

        var result = _repository.Script("mutation.sh", "--since", "origin/develop");

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void A_diff_score_under_the_full_run_score_of_its_project_fails()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "88");
        _repository.Write("baseline.txt", "Alpha=92\n");

        var result = _repository.Script("mutation.sh", "--since", "origin/develop", "--baseline", "baseline.txt");

        result.Succeeded.Should().BeFalse();
        _repository.CallsOf("dotnet").Should().Contain("--break-at 92");
    }

    [Fact]
    public void A_baseline_under_the_break_threshold_does_not_lower_it()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "75");
        _repository.Write("baseline.txt", "Alpha=40\n");

        var result = _repository.Script("mutation.sh", "--since", "origin/develop", "--baseline", "baseline.txt");

        result.Succeeded.Should().BeTrue(result.Error);
        _repository.CallsOf("dotnet").Should().Contain("--break-at 70");
    }

    [Fact]
    public void The_baseline_of_another_project_does_not_apply()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "75");
        _repository.Write("baseline.txt", "Beta=99\n");

        _repository.Script("mutation.sh", "--since", "origin/develop", "--baseline", "baseline.txt")
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_missing_baseline_file_is_the_first_run_and_not_an_error()
    {
        Dotnet();
        Project("Alpha", "Alpha.Tests");
        Score("Alpha", "75");

        _repository.Script("mutation.sh", "--since", "origin/develop", "--baseline", "absent.txt")
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void An_unknown_argument_is_refused()
    {
        Dotnet();

        var result = _repository.Script("mutation.sh", "--bogus");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("usage");
    }
}
