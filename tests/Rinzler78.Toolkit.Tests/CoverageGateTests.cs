using System.Globalization;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class CoverageGateTests : IDisposable
{
    private readonly ScratchRepository _repository;
    private readonly List<string> _packages = [];

    // Every case runs in a checkout whose path holds a pipe, a space and regex characters:
    // a root is data to the gate, never syntax, and a gate that reads it as syntax fails open.
    public CoverageGateTests()
    {
        _repository = new ScratchRepository("|p q.(r)+", ["_common.sh", "_affected.sh", "_coverage-gate.sh"]);
        // `dotnet msbuild <project> -getProperty:<name>` answers from files beside the
        // shim: .props/<Project>.<name>, with the defaults of a project that says nothing.
        _repository.Shim("dotnet", """
            project=$(basename "$2" .csproj)
            name=${3#-getProperty:}
            file="$PWD/.props/$project.$name"
            if [[ -f "$file" ]]; then cat "$file"
            else
              case "$name" in
                RinzlerCoverageContract) echo Enforced ;;
                AssemblyName) echo "$project" ;;
              esac
            fi
            """);
    }

    public void Dispose() => _repository.Dispose();

    private void Project(string name, string? contract = null, string? floor = null, string? includeBuildOutput = null)
    {
        _repository.Write($"src/{name}/{name}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        if (contract is not null)
        {
            _repository.Write($".props/{name}.RinzlerCoverageContract", contract);
        }

        if (floor is not null)
        {
            _repository.Write($".props/{name}.RinzlerCoverageFloor", floor);
        }

        if (includeBuildOutput is not null)
        {
            _repository.Write($".props/{name}.IncludeBuildOutput", includeBuildOutput);
        }
    }

    private void TestProject(string name) =>
        _repository.Write($"tests/{name}/{name}.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");

    private static string Rate(double percent) => (percent / 100).ToString("0.####", CultureInfo.InvariantCulture);

    // One <package> of a cobertura report: the assembly's own rates, and the lines of
    // each file as (number, hits).
    private void Package(string assembly, double lines, double branches, params (string File, (int Number, int Hits)[] Lines)[] files) =>
        PackageUnder(_repository.Root, assembly, lines, branches, files);

    // The report names files under <root>, the way the machine that collected it saw them.
    private void PackageUnder(string root, string assembly, double lines, double branches, params (string File, (int Number, int Hits)[] Lines)[] files)
    {
        var xml = new StringBuilder();
        xml.Append(CultureInfo.InvariantCulture, $"<package line-rate=\"{Rate(lines)}\" branch-rate=\"{Rate(branches)}\" name=\"{assembly}\"><classes>");
        foreach (var (file, covered) in files)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<class name=\"{assembly}.C\" filename=\"{(root.Length == 0 ? file : root + "/" + file)}\"><lines>");
            foreach (var (number, hits) in covered)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<line number=\"{number}\" hits=\"{hits}\" branch=\"False\" />");
            }

            xml.Append("</lines></class>");
        }

        xml.Append("</classes></package>");
        _packages.Add(xml.ToString());
    }

    private string Report()
    {
        _repository.Write(
            "artifacts/coverage/coverage.cobertura.xml",
            $"<?xml version=\"1.0\"?><coverage><packages>{string.Concat(_packages)}</packages></coverage>");
        return "artifacts/coverage/coverage.cobertura.xml";
    }

    private ScriptResult Evaluate(params string[] arguments) =>
        _repository.Script("_coverage-gate.sh", ["evaluate", Report(), .. arguments]);

    [Theory]
    [InlineData(90, 90)]
    [InlineData(100, 95)]
    public void An_enforced_project_at_or_over_ninety_percent_passes(double lines, double branches)
    {
        Project("Alpha");
        Package("Alpha", lines, branches);

        var result = Evaluate();

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void An_enforced_project_under_ninety_percent_of_lines_fails_and_says_which()
    {
        Project("Alpha");
        Package("Alpha", 89.9, 95);

        var result = Evaluate();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("lines").And.Contain("89.9").And.Contain("90");
        result.Output.Should().NotContain("floor 90 %", "a project that failed is not reported as holding");
    }

    [Fact]
    public void An_enforced_project_under_ninety_percent_of_branches_fails_and_says_which()
    {
        Project("Alpha");
        Package("Alpha", 95, 80);

        var result = Evaluate();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("branches").And.Contain("80");
    }

    [Fact]
    public void A_reduced_project_is_held_to_the_floor_it_declares()
    {
        Project("Alpha", "Reduced", "60");
        Package("Alpha", 61, 60);

        Evaluate().Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_reduced_project_under_its_own_floor_fails()
    {
        Project("Alpha", "Reduced", "60");
        Package("Alpha", 59, 80);

        var result = Evaluate();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("59").And.Contain("60");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("101")]
    [InlineData("-1")]
    public void A_reduced_project_without_a_usable_floor_fails(string floor)
    {
        Project("Alpha", "Reduced", floor);
        Package("Alpha", 99, 99);

        var result = Evaluate();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("RinzlerCoverageFloor");
    }

    [Fact]
    public void An_exempt_project_is_not_gated_whatever_it_covers()
    {
        Project("Alpha", "Exempt");
        Package("Alpha", 0, 0);

        Evaluate().Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_test_project_is_never_gated()
    {
        TestProject("Alpha.Tests");
        Package("Alpha.Tests", 1, 1);

        Evaluate().Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_project_that_ships_no_assembly_is_not_gated()
    {
        Project("Props", includeBuildOutput: "false");

        var result = Evaluate();

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void A_project_with_an_assembly_that_the_report_never_mentions_fails()
    {
        Project("Alpha");
        Package("Elsewhere", 100, 100);

        var result = Evaluate();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("no coverage");
    }

    [Fact]
    public void An_assembly_that_is_not_a_project_of_the_repository_is_ignored()
    {
        Project("Alpha");
        Package("Alpha", 95, 95);
        Package("MinVer", 10, 10);

        Evaluate().Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_assembly_name_is_the_one_the_project_declares_not_the_file_name()
    {
        Project("Alpha");
        _repository.Write(".props/Alpha.AssemblyName", "Company.Alpha");
        Package("Company.Alpha", 50, 50);

        Evaluate().Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_report_that_is_absent_is_an_error()
    {
        var result = _repository.Script("_coverage-gate.sh", "evaluate", "artifacts/coverage/none.xml");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("none.xml");
    }

    // --- the diff -------------------------------------------------------------------

    private void Feature(string path, string baseContent, string changedContent)
    {
        _repository.Write(path, baseContent);
        _repository.Git("add", "--all");
        _repository.Commit("base");
        _repository.Git("branch", "-M", "develop");
        _repository.Git("checkout", "--quiet", "-b", "feature/x");
        _repository.Write(path, changedContent);
        _repository.Git("add", path);
        _repository.Git("commit", "--quiet", "-m", "change");
    }

    private static string Lines(int count) => string.Concat(Enumerable.Range(1, count).Select(n => $"line {n}\n"));

    [Fact]
    public void New_lines_that_are_covered_pass_the_diff_gate()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        Package("Alpha", 95, 95, ("src/Alpha/A.cs", Enumerable.Range(1, 20).Select(n => (n, 1)).ToArray()));

        var result = Evaluate("--since", "develop");

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void New_lines_that_are_not_covered_fail_the_diff_gate_though_the_project_floor_holds()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        // Ten new lines, two covered: 20 % of the diff, while the assembly stays at 95.
        Package("Alpha", 95, 95, ("src/Alpha/A.cs", Enumerable.Range(1, 20).Select(n => (n, n is 11 or 12 || n <= 10 ? 1 : 0)).ToArray()));

        var result = Evaluate("--since", "develop");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("diff").And.Contain("20.00 %").And.Contain("2 of 10").And.Contain("95");
    }

    [Fact]
    public void Lines_the_report_does_not_know_are_not_counted_against_the_diff()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        // Lines 11 to 20 are comments or braces: no sequence point, so nothing to cover.
        Package("Alpha", 95, 95, ("src/Alpha/A.cs", Enumerable.Range(1, 10).Select(n => (n, 1)).ToArray()));

        Evaluate("--since", "develop").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_diff_of_an_exempt_project_is_not_gated()
    {
        Project("Alpha", "Exempt");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        Package("Alpha", 0, 0, ("src/Alpha/A.cs", Enumerable.Range(1, 20).Select(n => (n, 0)).ToArray()));

        Evaluate("--since", "develop").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_diff_gate_applies_to_a_reduced_project_too()
    {
        Project("Alpha", "Reduced", "50");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        Package("Alpha", 60, 60, ("src/Alpha/A.cs", Enumerable.Range(1, 20).Select(n => (n, n <= 10 ? 1 : 0)).ToArray()));

        Evaluate("--since", "develop").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Without_a_revision_there_is_no_diff_gate()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        Package("Alpha", 95, 95, ("src/Alpha/A.cs", Enumerable.Range(1, 20).Select(n => (n, 0)).ToArray()));

        Evaluate().Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_change_to_no_source_file_has_nothing_to_cover()
    {
        Project("Alpha");
        Feature("README.md", "a\n", "a\nb\n");
        Package("Alpha", 95, 95);

        Evaluate("--since", "develop").Succeeded.Should().BeTrue();
    }

    private static (string, (int, int)[])[] Covered(string file, int count, int hits) =>
        [(file, Enumerable.Range(1, count).Select(n => (n, hits)).ToArray())];

    private static string PhysicalPath(string path)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("bash")
        {
            ArgumentList = { "-c", "cd \"$0\" && pwd -P", path },
            RedirectStandardOutput = true,
        })!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }

    [Fact]
    public void A_report_naming_files_by_the_physical_path_of_the_checkout_is_still_read()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        PackageUnder(PhysicalPath(_repository.Root), "Alpha", 95, 95, Covered("src/Alpha/A.cs", 20, 0));

        var result = Evaluate("--since", "develop");

        result.Succeeded.Should().BeFalse("the new lines are uncovered, and the gate must see them");
        result.Error.Should().Contain("diff");
    }

    [Fact]
    public void A_report_naming_files_with_backslashes_is_still_read()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        PackageUnder(_repository.Root, "Alpha", 95, 95, Covered("src\\Alpha\\A.cs", 20, 0));

        Evaluate("--since", "develop").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_report_naming_files_by_a_deterministic_path_is_still_read()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        PackageUnder("/_", "Alpha", 95, 95, Covered("src/Alpha/A.cs", 20, 0));

        Evaluate("--since", "develop").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_report_from_another_checkout_is_refused_rather_than_read_as_nothing_to_cover()
    {
        Project("Alpha");
        Feature("src/Alpha/A.cs", Lines(10), Lines(20));
        PackageUnder("/somewhere/else", "Alpha", 95, 95, Covered("src/Alpha/A.cs", 20, 0));

        var result = Evaluate("--since", "develop");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("not of this checkout");
    }

    [Fact]
    public void A_renamed_and_edited_file_is_still_part_of_the_diff()
    {
        Project("Alpha");
        _repository.Write("src/Alpha/Old.cs", Lines(10));
        _repository.Git("add", "--all");
        _repository.Commit("base");
        _repository.Git("branch", "-M", "develop");
        _repository.Git("checkout", "--quiet", "-b", "feature/x");
        _repository.Git("mv", "src/Alpha/Old.cs", "src/Alpha/New.cs");
        _repository.Write("src/Alpha/New.cs", Lines(10) + Lines(10));
        _repository.Git("add", "--all");
        _repository.Git("commit", "--quiet", "-m", "rename and edit");
        Package("Alpha", 95, 95, ("src/Alpha/New.cs", Enumerable.Range(1, 20).Select(n => (n, n <= 10 ? 1 : 0)).ToArray()));

        Evaluate("--since", "develop").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_file_name_with_a_space_is_read()
    {
        Project("Alpha");
        Feature("src/Alpha/My File.cs", Lines(10), Lines(20));
        Package("Alpha", 95, 95, Covered("src/Alpha/My File.cs", 20, 0));

        Evaluate("--since", "develop").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_deleted_file_has_nothing_to_cover()
    {
        Project("Alpha");
        _repository.Write("src/Alpha/Gone.cs", Lines(10));
        _repository.Git("add", "--all");
        _repository.Commit("base");
        _repository.Git("branch", "-M", "develop");
        _repository.Git("checkout", "--quiet", "-b", "feature/x");
        _repository.Git("rm", "--quiet", "src/Alpha/Gone.cs");
        _repository.Git("commit", "--quiet", "-m", "delete");
        Package("Alpha", 95, 95);

        Evaluate("--since", "develop").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_new_file_with_no_executable_line_is_not_a_failure()
    {
        Project("Alpha");
        _repository.Write("src/Alpha/A.cs", Lines(10));
        _repository.Git("add", "--all");
        _repository.Commit("base");
        _repository.Git("branch", "-M", "develop");
        _repository.Git("checkout", "--quiet", "-b", "feature/x");
        _repository.Write("src/Alpha/IShape.cs", "interface IShape { }\n");
        _repository.Git("add", "--all");
        _repository.Git("commit", "--quiet", "-m", "interface");
        Package("Alpha", 95, 95, Covered("src/Alpha/A.cs", 10, 1));

        Evaluate("--since", "develop").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void An_assembly_reported_several_times_is_held_to_its_worst_entry()
    {
        Project("Alpha");
        Package("Alpha", 99, 99);
        Package("Alpha", 70, 99);

        var result = Evaluate();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("70");
    }

    [Fact]
    public void An_unknown_verb_is_refused()
    {
        var result = _repository.Script("_coverage-gate.sh", "bogus");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("usage");
    }
}
