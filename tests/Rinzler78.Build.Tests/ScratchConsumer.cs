using System.Diagnostics;

namespace Rinzler78.Build.Tests;

/// <summary>
/// A throwaway project importing the package's MSBuild files from this checkout,
/// built out of process so that its verdict is exactly the one a consumer gets.
/// </summary>
internal sealed class ScratchConsumer : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "rinzler-build-tests", Guid.NewGuid().ToString("N"));

    public ScratchConsumer(string properties)
    {
        var sdk = Path.Combine(RepositoryRoot, "src", "Rinzler78.Build", "Sdk");
        Directory.CreateDirectory(_directory);

        // The checkout's SDK, not whatever the machine resolves outside it: the verdict
        // must be the one this repository's own build would reach.
        File.Copy(Path.Combine(RepositoryRoot, "global.json"), Path.Combine(_directory, "global.json"));
        File.WriteAllText(Path.Combine(_directory, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{sdk}/Sdk.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
                {properties}
              </PropertyGroup>
              <Import Project="{sdk}/Sdk.targets" />
            </Project>
            """);
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

    public async Task<BuildResult> BuildAsync()
    {
        var start = new ProcessStartInfo("dotnet", ["build", "-nologo", "-nodeReuse:false", "--disable-build-servers"])
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // `dotnet run` exports settings of its own build into the runner —
        // MSBuildLoadMicrosoftTargetsReadOnly, MSBUILDFAILONDRIVEENUMERATINGWILDCARD — and
        // a scratch consumer must build as a consumer would, not as the host's child.
        foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(name);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("dotnet could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return new BuildResult(process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

internal sealed record BuildResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}
