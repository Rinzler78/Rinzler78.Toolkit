using System.Diagnostics;

namespace Rinzler78.Toolkit.Tests;

/// <summary>
/// A throwaway git repository carrying a copy of the scripts under test, so that each
/// script resolves its repository root to the scratch one and never to this checkout.
/// </summary>
internal sealed class ScratchRepository : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "rinzler-toolkit-tests", Guid.NewGuid().ToString("N"));

    private readonly string _bin;

    public ScratchRepository(params string[] scripts)
    {
        var scriptsDirectory = Path.Combine(_root, "scripts");
        Directory.CreateDirectory(scriptsDirectory);
        foreach (var script in scripts)
        {
            File.Copy(Path.Combine(RepositoryRoot, "scripts", script), Path.Combine(scriptsDirectory, script));
        }

        _bin = Path.Combine(_root, ".bin");
        Directory.CreateDirectory(_bin);

        Git("init", "--quiet", "--initial-branch=develop");
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rinzler78.Toolkit.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("The tests run outside the Rinzler78.Toolkit checkout.");
        }
    }

    public string Commit(string message)
    {
        File.WriteAllText(Path.Combine(_root, "history.txt"), message);
        Git("add", "history.txt");
        Git("commit", "--quiet", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    public string Git(params string[] arguments)
    {
        var result = Run("git", arguments);
        return result.ExitCode == 0
            ? result.Output
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Output}");
    }

    /// <summary>
    /// Puts a stand-in for the GitHub CLI first on the PATH, answering the tag
    /// verification query with <paramref name="verified"/> and <paramref name="reason"/>.
    /// </summary>
    public void GitHubVerifies(bool verified, string reason)
    {
        var gh = Path.Combine(_bin, "gh");
        File.WriteAllText(gh, $"#!/usr/bin/env bash\necho '{{\"verified\":{(verified ? "true" : "false")},\"reason\":\"{reason}\"}}'\n");
        MakeExecutable(gh);
    }

    private static void MakeExecutable(string path)
    {
        // The scripts under test are bash, run by the CI's Linux runners and on macOS;
        // a Windows host has no executable bit to set and no bash to run them with.
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The harness scripts run under bash, on Linux or macOS.");
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Puts a recording stand-in for the GitHub CLI first on the PATH. Every call is
    /// appended to <see cref="GitHubCalls"/> with its standard input; a GET of the
    /// rulesets or of the deployment policies answers with the JSON given here.
    /// </summary>
    public void GitHubRecords(string rulesets, string deploymentPolicies)
    {
        File.WriteAllText(Path.Combine(_bin, "rulesets.json"), rulesets);
        File.WriteAllText(Path.Combine(_bin, "policies.json"), deploymentPolicies);
        var gh = Path.Combine(_bin, "gh");
        File.WriteAllText(gh, $$"""
            #!/usr/bin/env bash
            log="{{_bin}}/calls.log"
            input=
            [[ " $* " == *" --input - "* ]] && input=$(cat)
            printf '%s\n%s\n---\n' "$*" "$input" >>"$log"
            case "$*" in
              *"-X"*) echo '{}' ;;
              *"/rulesets"*) cat "{{_bin}}/rulesets.json" ;;
              *"/deployment-branch-policies"*) cat "{{_bin}}/policies.json" ;;
              *) echo '{}' ;;
            esac
            """);
        MakeExecutable(gh);
    }

    public string GitHubCalls =>
        File.Exists(Path.Combine(_bin, "calls.log")) ? File.ReadAllText(Path.Combine(_bin, "calls.log")) : string.Empty;

    public ScriptResult Script(string script, params string[] arguments) =>
        Run("bash", [Path.Combine(_root, "scripts", script), .. arguments]);

    private ScriptResult Run(string file, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // A git hook exports GIT_DIR, GIT_INDEX_FILE and their neighbours to what it
        // runs: under the commit gate, the scratch repository's commands wrote to the
        // index of the commit being made. Nothing of the caller's git survives.
        foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.Ordinal)).ToList())
        {
            start.Environment.Remove(name);
        }

        // Isolated from the machine's git configuration: a signing key, a commit
        // template or a hook path there would change what the scratch history is.
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_AUTHOR_NAME"] = "Scratch";
        start.Environment["GIT_AUTHOR_EMAIL"] = "scratch@example.invalid";
        start.Environment["GIT_COMMITTER_NAME"] = "Scratch";
        start.Environment["GIT_COMMITTER_EMAIL"] = "scratch@example.invalid";
        start.Environment["GITHUB_REPOSITORY"] = "owner/scratch";
        start.Environment.Remove("GITHUB_OUTPUT");
        start.Environment["PATH"] = $"{_bin}:{start.Environment["PATH"]}";

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{file} could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output, error);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

internal sealed record ScriptResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}
