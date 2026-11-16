using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class SkillsTests : IDisposable
{
    private const string Ref = "0123456789abcdef0123456789abcdef01234567";

    private readonly ScratchRepository _repository =
        new("_common.sh", "sync-skills.sh", "hooks/skills-are-vendored.sh");

    private readonly string _upstream =
        Path.Combine(Path.GetTempPath(), "rinzler-toolkit-tests", "upstream-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _repository.Dispose();
        if (Directory.Exists(_upstream))
        {
            Directory.Delete(_upstream, recursive: true);
        }
    }

    /// <summary>Publishes an upstream archive the way codeload serves it: one top directory.</summary>
    private void Upstream(string repository, params (string Path, string Content)[] files)
    {
        var staging = Path.Combine(_upstream, "staging", repository.Replace('/', '-'), "top");
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(staging, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        var archive = Path.Combine(_upstream, repository, "tar.gz", Ref);
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        var tar = Process.Start(new ProcessStartInfo("tar", ["czf", archive, "-C", Path.GetDirectoryName(staging)!, "top"]))!;
        tar.WaitForExit();
        tar.ExitCode.Should().Be(0);
    }

    private void Manifest(params string[] lines) =>
        _repository.Write("scripts/skills.manifest", string.Join('\n', lines) + "\n");

    private ScriptResult Sync(params string[] arguments) =>
        _repository.Script("sync-skills.sh", ["--base", "file://" + _upstream, .. arguments]);

    private ScriptResult Check() => _repository.Script("hooks/skills-are-vendored.sh");

    private void TwoSkills()
    {
        Upstream("acme/skills",
            ("skills/a/tdd/SKILL.md", "---\nname: tdd\n---\nred green\n"),
            ("skills/a/tdd/extra.md", "reference\n"),
            ("skills/b/triage/SKILL.md", "---\nname: triage\n---\n"),
            ("LICENSE", "MIT License\nCopyright acme\n"));
        Manifest(
            "# name repository ref path",
            $"tdd acme/skills {Ref} skills/a/tdd",
            $"triage acme/skills {Ref} skills/b/triage");
    }

    [Fact]
    public void Each_skill_lands_in_both_directories_with_its_whole_folder()
    {
        TwoSkills();

        var result = Sync("--no-commit");

        result.Succeeded.Should().BeTrue(result.Error);
        foreach (var directory in new[] { ".agents/skills", ".claude/skills" })
        {
            File.ReadAllText(Path.Combine(_repository.Root, directory, "tdd", "extra.md")).Should().Be("reference\n");
            File.Exists(Path.Combine(_repository.Root, directory, "triage", "SKILL.md")).Should().BeTrue();
        }
    }

    [Fact]
    public void A_shell_script_inside_a_skill_is_executable()
    {
        Upstream("acme/skills",
            ("skills/a/tdd/SKILL.md", "---\nname: tdd\n---\n"),
            ("skills/a/tdd/scripts/loop.sh", "#!/usr/bin/env bash\n"),
            ("LICENSE", "MIT License\n"));
        Manifest($"tdd acme/skills {Ref} skills/a/tdd");

        Sync("--no-commit").Succeeded.Should().BeTrue();

        // The scripts under test are bash; a Windows host has no executable bit to read.
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The harness scripts run under bash, on Linux or macOS.");
        }

        foreach (var directory in new[] { ".agents/skills", ".claude/skills" })
        {
            File.GetUnixFileMode(Path.Combine(_repository.Root, directory, "tdd/scripts/loop.sh"))
                .Should().HaveFlag(UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void The_notice_names_every_source_pin_and_carries_its_licence()
    {
        TwoSkills();

        Sync("--no-commit").Succeeded.Should().BeTrue();

        var notice = File.ReadAllText(Path.Combine(_repository.Root, ".agents/skills/NOTICE.md"));
        notice.Should().Contain("acme/skills").And.Contain(Ref).And.Contain("Copyright acme");
        notice.Should().Be(File.ReadAllText(Path.Combine(_repository.Root, ".claude/skills/NOTICE.md")));
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();
        _repository.Git("add", "-A");
        _repository.Git("commit", "--quiet", "-m", "first");

        Sync("--no-commit").Succeeded.Should().BeTrue();

        _repository.Git("status", "--porcelain", "--", ".agents", ".claude").Should().BeEmpty();
    }

    [Fact]
    public void A_skill_dropped_from_the_manifest_is_removed()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();
        Manifest($"tdd acme/skills {Ref} skills/a/tdd");

        Sync("--no-commit").Succeeded.Should().BeTrue();

        Directory.Exists(Path.Combine(_repository.Root, ".agents/skills/triage")).Should().BeFalse();
        Directory.Exists(Path.Combine(_repository.Root, ".claude/skills/triage")).Should().BeFalse();
    }

    [Fact]
    public void A_skill_missing_upstream_is_refused_and_nothing_is_written()
    {
        TwoSkills();
        Manifest($"gone acme/skills {Ref} skills/a/gone");

        var result = Sync("--no-commit");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("skills/a/gone");
        Directory.Exists(Path.Combine(_repository.Root, ".agents/skills/gone")).Should().BeFalse();
    }

    [Fact]
    public void A_reference_that_is_not_a_full_commit_is_refused()
    {
        TwoSkills();
        Manifest("tdd acme/skills main skills/a/tdd");

        var result = Sync("--no-commit");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("not a full commit");
    }

    [Fact]
    public void The_result_is_committed_on_a_feature_branch()
    {
        TwoSkills();
        _repository.Commit("seed");
        _repository.Git("switch", "--quiet", "-c", "feature/refresh-skills");

        var result = Sync();

        result.Succeeded.Should().BeTrue(result.Error);
        _repository.Git("log", "-1", "--format=%s").Should().Contain("chore(skills)");
        _repository.Git("status", "--porcelain", "--", ".agents", ".claude", "scripts/skills.manifest").Should().BeEmpty();
    }

    [Fact]
    public void An_integration_branch_is_never_committed_to()
    {
        TwoSkills();
        _repository.Commit("seed");

        var result = Sync();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("feature branch");
    }

    [Fact]
    public void The_check_passes_on_a_freshly_synchronised_tree()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();

        var result = Check();

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public void The_check_names_a_skill_missing_from_one_directory()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();
        Directory.Delete(Path.Combine(_repository.Root, ".claude/skills/triage"), recursive: true);

        var result = Check();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("triage");
    }

    [Fact]
    public void The_check_names_two_directories_that_diverge()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();
        File.AppendAllText(Path.Combine(_repository.Root, ".agents/skills/tdd/SKILL.md"), "a local edit\n");

        var result = Check();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("diverge");
    }

    [Fact]
    public void The_check_refuses_a_skill_the_manifest_does_not_name()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();
        _repository.Write(".agents/skills/stray/SKILL.md", "x");
        _repository.Write(".claude/skills/stray/SKILL.md", "x");

        var result = Check();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("stray");
    }

    [Fact]
    public void The_check_refuses_a_symbolic_link()
    {
        TwoSkills();
        Sync("--no-commit").Succeeded.Should().BeTrue();
        File.CreateSymbolicLink(Path.Combine(_repository.Root, ".agents/skills/link"), "tdd");

        var result = Check();

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("symbolic link");
    }
}
