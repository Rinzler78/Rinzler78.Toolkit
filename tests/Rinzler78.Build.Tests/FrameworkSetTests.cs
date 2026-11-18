using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

/// <summary>
/// The named target framework sets the package topology requires, so that a solution cannot
/// disagree with itself about what "portable", "Xamarin" or "Android" means.
/// </summary>
public class FrameworkSetTests
{
    public static TheoryData<string, string> Sets => new()
    {
        { "RinzlerPortableTargetFrameworks", "netstandard2.0;net8.0;net9.0;net10.0" },
        { "RinzlerModernTargetFrameworks", "net8.0;net9.0;net10.0" },
        { "RinzlerXamarinTargetFrameworks", "monoandroid12.0;xamarinios10" },
        { "RinzlerAndroidTargetFrameworks", "net8.0-android;net9.0-android;net10.0-android" },
        { "RinzlerIosTargetFrameworks", "net8.0-ios;net9.0-ios;net10.0-ios" },
        { "RinzlerWindowsTargetFrameworks", "net10.0-windows10.0.19041.0" },
        { "RinzlerMacCatalystTargetFrameworks", "net10.0-maccatalyst" },
        { "RinzlerPlatformTargetFrameworks", "monoandroid12.0;xamarinios10;net8.0-android;net9.0-android;net10.0-android;net8.0-ios;net9.0-ios;net10.0-ios" },
    };

    [Theory]
    [MemberData(nameof(Sets))]
    public async Task A_named_set_holds_the_frameworks_the_topology_requires(string name, string expected)
    {
        using var consumer = new ScratchConsumer("");

        (await consumer.PropertyAsync(name)).Should().Be(expected);
    }

    [Fact]
    public async Task A_project_can_override_a_set()
    {
        using var consumer = new ScratchConsumer("<RinzlerAndroidTargetFrameworks>net10.0-android</RinzlerAndroidTargetFrameworks>");

        (await consumer.PropertyAsync("RinzlerAndroidTargetFrameworks")).Should().Be("net10.0-android");
    }
}
