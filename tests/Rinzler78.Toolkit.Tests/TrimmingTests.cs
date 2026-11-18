using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

/// <summary>
/// The trimming measurement: what a Committed library's trimming test application weighs,
/// published with the library marked and unmarked, written to the README and checked
/// against it. <c>dotnet</c> is a stand-in: msbuild answers the property queries, and
/// publish writes the files a trimmed publication would, with sizes the tests choose.
/// </summary>
public sealed class TrimmingTests : IDisposable
{
    private const string Start = "<!-- trimming:start -->";
    private const string End = "<!-- trimming:end -->";

    private readonly ScratchRepository _repository = new("_common.sh", "_affected.sh", "_coverage-gate.sh", "trimming.sh");

    public TrimmingTests()
    {
        _repository.Shim("dotnet", """
            case "$1" in
              msbuild)
                project=$(basename "$2" .csproj)
                name=${3#-getProperty:}
                file="$PWD/.props/$project.$name"
                if [[ -f "$file" ]]; then cat "$file"
                else
                  case "$name" in
                    RinzlerTrimContract) echo OutOfScope ;;
                    AssemblyName) echo "$project" ;;
                  esac
                fi
                ;;
              publish)
                app=$(basename "$2" .csproj)
                library=${app%.Trimming}
                output=
                unmarked=false
                aot=false
                shift
                while (($#)); do
                  case "$1" in
                    --output) output=$2; shift ;;
                    -p:RinzlerTrimContract=OutOfScope) unmarked=true ;;
                    -p:RinzlerAot=true) aot=true ;;
                  esac
                  shift
                done
                if [[ $aot == true && -f "$PWD/.aot-fails" ]]; then
                  echo "error IL3050: not compatible with ahead-of-time compilation" >&2
                  exit 1
                fi
                kind=marked
                [[ $unmarked == true ]] && kind=unmarked
                size=$(cat "$PWD/.sizes/$library.$kind" 2>/dev/null || { [[ $kind == marked ]] && echo 1000 || echo 5000; })
                mkdir -p "$output"
                if [[ ! -f "$PWD/.no-dll" ]]; then head -c "$size" /dev/zero >"$output/$library.dll"; fi
                head -c 100000 /dev/zero >"$output/System.Runtime.dll"
                if [[ $aot == true && ! -f "$PWD/.no-binary" ]]; then head -c 300000 /dev/zero >"$output/$app"; fi
                if [[ -f "$PWD/.rewrite-locks" ]]; then
                  echo rewritten >"$PWD/src/$library/packages.lock.json"
                  echo created >"$PWD/tests/$library.Trimming/packages.lock.json"
                fi
                ;;
            esac
            """);
    }

    public void Dispose() => _repository.Dispose();

    private void Library(string name, string contract = "Committed", int? marked = null, int? unmarked = null, bool withApp = true)
    {
        _repository.Write($"src/{name}/{name}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        _repository.Write($".props/{name}.RinzlerTrimContract", contract);
        if (withApp)
        {
            _repository.Write($"tests/{name}.Trimming/{name}.Trimming.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        }

        if (marked is not null)
        {
            _repository.Write($".sizes/{name}.marked", marked.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (unmarked is not null)
        {
            _repository.Write($".sizes/{name}.unmarked", unmarked.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private void Readme(string content) => _repository.Write("README.md", content);

    private string ReadmeText => File.ReadAllText(Path.Combine(_repository.Root, "README.md"));

    private static string Block(string rows) =>
        $"{Start}\n| Project | Library, marked | Library, unmarked | Published, marked | Published, unmarked |\n|---|---:|---:|---:|---:|\n{rows}\n{End}\n";

    [Fact]
    public void A_repository_with_no_committed_library_has_nothing_to_measure()
    {
        Library("Alpha", "Analyzed");
        Library("Beta", "OutOfScope");
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeTrue(result.Error + result.Output);
        result.Output.Should().Contain("no Committed");
        _repository.CallsOf("dotnet").Should().NotContain("publish");
    }

    [Fact]
    public void A_committed_library_without_its_trimming_application_fails_naming_where_it_belongs()
    {
        Library("Alpha", withApp: false);
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("tests/Alpha.Trimming/Alpha.Trimming.csproj");
    }

    [Fact]
    public void A_library_is_published_marked_and_unmarked_without_a_runtime_identifier_on_the_command_line()
    {
        Library("Alpha");
        Readme("# Readme\n");

        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();

        var calls = _repository.CallsOf("dotnet").Split('\n').Where(call => call.StartsWith("publish", StringComparison.Ordinal)).ToArray();
        calls.Should().HaveCount(2);
        calls.Should().OnlyContain(call => call.Contains("tests/Alpha.Trimming/Alpha.Trimming.csproj", StringComparison.Ordinal) && !call.Contains(" -r ", StringComparison.Ordinal));
        calls.Count(call => call.Contains("-p:RinzlerTrimContract=OutOfScope", StringComparison.Ordinal)).Should().Be(1, "only the unmarked publication overrides the contract");
    }

    [Fact]
    public void Write_records_both_sizes_of_the_library_and_of_the_publication_in_the_readme()
    {
        Library("Alpha", marked: 1000, unmarked: 5000);
        Readme("# Readme\n\nText.\n");

        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();

        ReadmeText.Should().Contain(Start).And.Contain(End)
            .And.Contain("| Alpha | 1000 | 5000 | 101000 | 105000 |");
        ReadmeText.Should().StartWith("# Readme\n\nText.\n", "what the author wrote stays");
    }

    [Fact]
    public void Write_replaces_its_own_block_and_nothing_else()
    {
        Library("Alpha", marked: 2000, unmarked: 5000);
        Readme($"# Readme\n\nBefore.\n\n{Block("| Alpha | 1 | 2 | 3 | 4 |")}\nAfter.\n");

        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();

        ReadmeText.Should().Contain("| Alpha | 2000 | 5000 | 102000 | 105000 |").And.NotContain("| Alpha | 1 | 2 | 3 | 4 |");
        ReadmeText.Should().Contain("Before.").And.Contain("After.");
        ReadmeText.Split(Start, StringSplitOptions.None).Length.Should().Be(2, "the block is replaced, never duplicated");
    }

    [Fact]
    public void Write_twice_changes_nothing_the_second_time()
    {
        Library("Alpha");
        Readme("# Readme\n");
        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();
        var first = ReadmeText;

        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();

        ReadmeText.Should().Be(first);
    }

    [Fact]
    public void Check_passes_when_the_readme_records_what_is_measured()
    {
        Library("Alpha", marked: 1000, unmarked: 5000);
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeTrue(result.Error + result.Output);
    }

    [Fact]
    public void Check_does_not_touch_the_readme()
    {
        Library("Alpha", marked: 3000, unmarked: 5000);
        var recorded = Block("| Alpha | 1000 | 5000 | 101000 | 105000 |");
        Readme(recorded);

        _repository.Script("trimming.sh", "--check");

        ReadmeText.Should().Be(recorded);
    }

    [Fact]
    public void Check_fails_when_the_marked_library_grew_by_more_than_five_percent()
    {
        Library("Alpha", marked: 1060, unmarked: 5000);
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("1060").And.Contain("1000").And.Contain("5 %");
    }

    [Fact]
    public void Check_accepts_growth_within_five_percent_and_any_shrinking()
    {
        Library("Alpha", marked: 1050, unmarked: 5000);
        Library("Beta", marked: 500, unmarked: 5000);
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |\n| Beta | 1000 | 5000 | 101000 | 105000 |"));

        _repository.Script("trimming.sh", "--check").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void The_published_payload_is_information_and_never_the_verdict()
    {
        // 5 % of a framework of megabytes is a hundred times a library of kilobytes: a
        // payload gate would pass any regression of the library, and trip on a runtime
        // patch. Only the library column gates.
        Library("Alpha", marked: 1000, unmarked: 5000);
        Readme(Block("| Alpha | 1000 | 5000 | 50000 | 105000 |"));

        _repository.Script("trimming.sh", "--check").Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("n/a")]
    [InlineData("")]
    [InlineData("1,000")]
    public void Check_fails_when_the_readme_records_no_number_for_the_library(string cell)
    {
        Library("Alpha");
        Readme(Block($"| Alpha | {cell} | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeFalse("a figure that cannot be read is not a figure that holds");
        result.Error.Should().Contain("Alpha").And.Contain("records no size");
    }

    [Fact]
    public void A_publication_without_the_library_assembly_names_that_and_not_a_trimming_problem()
    {
        Library("Alpha");
        _repository.Write(".no-dll", "yes");
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--write");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("Alpha.dll").And.Contain("missing");
        result.Error.Should().NotContain("trims nothing");
        ReadmeText.Should().NotContain(Start, "nothing is recorded from a measurement that failed");
    }

    [Fact]
    public void A_native_publication_without_its_binary_is_a_failure_and_not_an_empty_size()
    {
        Library("Alpha");
        _repository.Write(".no-binary", "yes");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check", "--aot");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("native");
        File.ReadAllText(Path.Combine(_repository.Root, "artifacts/trimming/summary.md")).Should().NotContain("|  |");
    }

    [Fact]
    public void A_readme_with_windows_line_endings_keeps_one_block_however_often_it_is_written()
    {
        Library("Alpha");
        // As git writes it on a machine with autocrlf: the markers carry the carriage return too.
        Readme("# Readme\r\n\r\n" + Block("| Alpha | 1 | 2 | 3 | 4 |").Replace("\n", "\r\n", StringComparison.Ordinal));

        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();
        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();

        ReadmeText.Split(Start, StringSplitOptions.None).Length.Should().Be(2);
        _repository.Script("trimming.sh", "--check").Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_readme_carrying_two_blocks_is_left_with_one()
    {
        Library("Alpha");
        Readme($"{Block("| Alpha | 1 | 2 | 3 | 4 |")}\nMiddle.\n\n{Block("| Alpha | 5 | 6 | 7 | 8 |")}");

        _repository.Script("trimming.sh", "--write").Succeeded.Should().BeTrue();

        ReadmeText.Split(Start, StringSplitOptions.None).Length.Should().Be(2);
        ReadmeText.Should().Contain("Middle.").And.Contain("| Alpha | 1000 |");
    }

    [Fact]
    public void Check_fails_when_marking_the_library_trims_nothing()
    {
        Library("Alpha", marked: 5000, unmarked: 5000);
        Readme(Block("| Alpha | 5000 | 5000 | 105000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("trims nothing");
    }

    [Fact]
    public void Check_fails_when_the_readme_records_no_measurement()
    {
        Library("Alpha");
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("trimming.sh --write");
    }

    [Fact]
    public void Check_fails_for_a_committed_library_the_readme_does_not_know()
    {
        Library("Alpha");
        Library("Beta");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Beta");
    }

    [Fact]
    public void A_test_project_and_a_project_shipping_no_assembly_are_not_measured()
    {
        Library("Alpha", "OutOfScope");
        _repository.Write("tests/Alpha.Tests/Alpha.Tests.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        _repository.Write(".props/Alpha.Tests.RinzlerTrimContract", "Committed");
        Library("Gamma");
        _repository.Write(".props/Gamma.IncludeBuildOutput", "false");
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--check");

        result.Succeeded.Should().BeTrue(result.Error + result.Output);
        _repository.CallsOf("dotnet").Should().NotContain("publish");
    }

    [Fact]
    public void The_summary_for_the_pull_request_is_written_whatever_the_verdict()
    {
        Library("Alpha", marked: 2000, unmarked: 5000);
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        _repository.Script("trimming.sh", "--check").Succeeded.Should().BeFalse();

        var summary = File.ReadAllText(Path.Combine(_repository.Root, "artifacts/trimming/summary.md"));
        summary.Should().Contain("Alpha").And.Contain("2000").And.Contain("5000").And.Contain("1000");
    }

    [Fact]
    public void The_summary_carries_the_marker_the_workflow_finds_its_previous_post_by()
    {
        Library("Alpha");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        _repository.Script("trimming.sh", "--check").Succeeded.Should().BeTrue();

        File.ReadAllText(Path.Combine(_repository.Root, "artifacts/trimming/summary.md")).Should().StartWith("<!-- trimming-summary -->");
    }

    [Fact]
    public void Aot_publishes_each_application_natively_and_reports_the_size()
    {
        Library("Alpha");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check", "--aot");

        result.Succeeded.Should().BeTrue(result.Error + result.Output);
        _repository.CallsOf("dotnet").Should().Contain("-p:RinzlerAot=true").And.NotContain("-p:PublishAot", "a global PublishAot would flow to the library's netstandard slice");
        var summary = File.ReadAllText(Path.Combine(_repository.Root, "artifacts/trimming/summary.md"));
        summary.Should().Contain("300000");
    }

    [Fact]
    public void Aot_fails_when_the_library_cannot_be_compiled_ahead_of_time()
    {
        Library("Alpha");
        _repository.Write(".aot-fails", "yes");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        var result = _repository.Script("trimming.sh", "--check", "--aot");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Alpha").And.Contain("ahead-of-time");
    }

    [Fact]
    public void Without_aot_nothing_is_compiled_natively()
    {
        Library("Alpha");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        _repository.Script("trimming.sh", "--check").Succeeded.Should().BeTrue();

        _repository.CallsOf("dotnet").Should().NotContain("RinzlerAot");
    }

    [Fact]
    public void The_lock_files_are_left_as_they_were_found_whatever_the_publications_did_to_them()
    {
        Library("Alpha");
        _repository.Write("src/Alpha/packages.lock.json", "committed");
        _repository.Write(".rewrite-locks", "yes");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        _repository.Script("trimming.sh", "--check", "--aot").Succeeded.Should().BeTrue();

        File.ReadAllText(Path.Combine(_repository.Root, "src/Alpha/packages.lock.json")).Should().Be("committed");
        File.Exists(Path.Combine(_repository.Root, "tests/Alpha.Trimming/packages.lock.json")).Should().BeFalse("a lock that did not exist is not left behind");
    }

    [Fact]
    public void The_lock_files_are_put_back_when_a_publication_fails()
    {
        Library("Alpha");
        _repository.Write("src/Alpha/packages.lock.json", "committed");
        _repository.Write(".rewrite-locks", "yes");
        _repository.Write(".aot-fails", "yes");
        Readme(Block("| Alpha | 1000 | 5000 | 101000 | 105000 |"));

        _repository.Script("trimming.sh", "--check", "--aot").Succeeded.Should().BeFalse();

        File.ReadAllText(Path.Combine(_repository.Root, "src/Alpha/packages.lock.json")).Should().Be("committed");
    }

    [Fact]
    public void Write_and_check_together_are_refused()
    {
        Library("Alpha");
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--write", "--check");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("usage");
    }

    [Fact]
    public void An_unknown_argument_is_refused()
    {
        Readme("# Readme\n");

        var result = _repository.Script("trimming.sh", "--frobnicate");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("usage");
    }
}
