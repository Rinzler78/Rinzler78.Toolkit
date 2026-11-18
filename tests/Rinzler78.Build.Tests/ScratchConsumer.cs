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

    public ScratchConsumer(string properties, string items = "")
    {
        Directory.CreateDirectory(_directory);

        // The checkout's SDK, not whatever the machine resolves outside it: the verdict
        // must be the one this repository's own build would reach.
        File.Copy(Path.Combine(RepositoryRoot, "global.json"), Path.Combine(_directory, "global.json"));
        // The package's location reaches the project through the environment, never
        // through its text: a checkout path is not XML-safe, and MSBuild would evaluate
        // any `$(`, `@(` or `%` in it. An environment variable arrives as a literal.
        File.WriteAllText(Path.Combine(_directory, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="$({SdkVariable})/Sdk.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
                {properties}
              </PropertyGroup>
              {items}
              <Import Project="$({SdkVariable})/Sdk.targets" />
            </Project>
            """);
    }

    private const string SdkVariable = "RinzlerBuildSdkUnderTest";

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

    /// <summary>Writes a file next to the consumer's project, creating its folder.</summary>
    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public Task<BuildResult> BuildAsync() => RunAsync("build");

    /// <summary>
    /// The value MSBuild evaluates for <paramref name="name"/> in this project, empty when
    /// the property is not set: what the contract *does*, not what its text says.
    /// </summary>
    public async Task<string> PropertyAsync(string name, string? framework = null, string? configuration = null)
    {
        // For one slice of a multi-targeted project: the inner build is evaluated with
        // its own TargetFramework, as the outer build would dispatch it.
        List<string> arguments = [$"-getProperty:{name}"];
        if (framework is not null)
        {
            arguments.Add($"-p:TargetFramework={framework}");
        }

        if (configuration is not null)
        {
            arguments.Add($"-p:Configuration={configuration}");
        }

        var result = await RunAsync("msbuild", [.. arguments]).ConfigureAwait(false);
        return result.Output.Trim();
    }

    private async Task<BuildResult> RunAsync(string verb, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet", [verb, "-nologo", "-nodeReuse:false", "--disable-build-servers", .. arguments])
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

        start.Environment[SdkVariable] = Path.Combine(RepositoryRoot, "src", "Rinzler78.Build", "Sdk");

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
