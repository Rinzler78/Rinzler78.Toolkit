using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

/// <summary>
/// The Release and Debug optimisation properties of the toolchain specification: Debug
/// stays diagnosable, Release enables what the project can enable, and the costly
/// optimisations can be switched off for a feature-branch build. The tests evaluate the
/// properties this package sets; no platform SDK is imported, so they show that nothing here
/// enables an optimisation in Debug (an unset property passes), not what a platform SDK
/// later does with the values. The first real application build is task 1.11f.
/// </summary>
public class OptimisationContractTests
{
    private const string Android = "net10.0-android";
    private const string Ios = "net10.0-ios";
    private const string XamarinAndroid = "monoandroid13.0";
    private const string XamarinIos = "xamarinios10";

    private static async Task<string> EvaluateAsync(string name, string configuration, string properties = "")
    {
        using var consumer = new ScratchConsumer(properties);
        return await consumer.PropertyAsync(name, configuration: configuration).ConfigureAwait(false);
    }

    private static string Framework(string moniker, string extra = "") =>
        $"<TargetFramework>{moniker}</TargetFramework>{extra}";

    private const string Off = "<RinzlerCostlyOptimisations>false</RinzlerCostlyOptimisations>";

    [Fact]
    public async Task Debug_is_unoptimised_with_symbols()
    {
        (await EvaluateAsync("Optimize", "Debug")).Should().Be("false");
        (await EvaluateAsync("DebugSymbols", "Debug")).Should().Be("true");
    }

    [Fact]
    public async Task Release_is_optimised()
    {
        (await EvaluateAsync("Optimize", "Release")).Should().Be("true");
    }

    [Fact]
    public async Task Costly_optimisations_are_on_by_default_and_a_project_can_switch_them_off()
    {
        (await EvaluateAsync("RinzlerCostlyOptimisations", "Release")).Should().Be("true");
        (await EvaluateAsync("RinzlerCostlyOptimisations", "Release", Off)).Should().Be("false");
    }

    [Theory]
    [InlineData("RunAOTCompilation", "true", "false")]
    [InlineData("AndroidEnableProfiledAot", "true", "false")]
    public async Task Release_on_android_compiles_ahead_of_time_unless_the_costly_ones_are_off(string property, string full, string fast)
    {
        (await EvaluateAsync(property, "Release", Framework(Android))).Should().Be(full);
        (await EvaluateAsync(property, "Release", Framework(Android, Off))).Should().Be(fast);
    }

    [Fact]
    public async Task Release_on_android_shrinks_with_r8_even_when_the_costly_ones_are_off()
    {
        (await EvaluateAsync("AndroidLinkTool", "Release", Framework(Android))).Should().Be("r8");
        (await EvaluateAsync("AndroidLinkTool", "Release", Framework(Android, Off))).Should().Be("r8");
    }

    [Fact]
    public async Task Debug_on_android_is_left_to_the_sdk_defaults()
    {
        (await EvaluateAsync("RunAOTCompilation", "Debug", Framework(Android))).Should().NotBe("true");
        (await EvaluateAsync("AndroidLinkTool", "Debug", Framework(Android))).Should().NotBe("r8");
    }

    [Fact]
    public async Task Release_on_xamarin_android_follows_the_same_rule_with_its_own_property_names()
    {
        (await EvaluateAsync("AotAssemblies", "Release", Framework(XamarinAndroid))).Should().Be("true");
        (await EvaluateAsync("EnableLLVM", "Release", Framework(XamarinAndroid))).Should().Be("true");
        (await EvaluateAsync("AndroidEnableProfiledAot", "Release", Framework(XamarinAndroid))).Should().Be("true");
        (await EvaluateAsync("AndroidLinkTool", "Release", Framework(XamarinAndroid))).Should().Be("r8");
        (await EvaluateAsync("AotAssemblies", "Release", Framework(XamarinAndroid, Off))).Should().Be("false");
        (await EvaluateAsync("EnableLLVM", "Release", Framework(XamarinAndroid, Off))).Should().Be("false");
    }

    [Fact]
    public async Task Debug_on_xamarin_android_deploys_assemblies_only_and_compiles_nothing_ahead_of_time()
    {
        (await EvaluateAsync("AndroidFastDeploymentType", "Debug", Framework(XamarinAndroid))).Should().Be("Assemblies");
        (await EvaluateAsync("AotAssemblies", "Debug", Framework(XamarinAndroid))).Should().NotBe("true");
    }

    [Fact]
    public async Task The_fast_deployment_type_is_not_set_where_it_was_removed_or_does_not_exist()
    {
        (await EvaluateAsync("AndroidFastDeploymentType", "Debug", Framework(Android))).Should().BeEmpty();
        (await EvaluateAsync("AndroidFastDeploymentType", "Release", Framework(XamarinAndroid))).Should().NotBe("Assemblies");
    }

    [Theory]
    [InlineData(Ios)]
    [InlineData(XamarinIos)]
    public async Task Release_on_ios_enables_llvm_unless_the_costly_ones_are_off(string framework)
    {
        (await EvaluateAsync("MtouchUseLlvm", "Release", Framework(framework))).Should().Be("true");
        (await EvaluateAsync("MtouchUseLlvm", "Release", Framework(framework, Off))).Should().Be("false");
        (await EvaluateAsync("MtouchUseLlvm", "Debug", Framework(framework))).Should().NotBe("true");
    }

    [Fact]
    public async Task Release_on_blazor_webassembly_compiles_ahead_of_time_and_compresses()
    {
        const string blazor = "<UsingMicrosoftNETSdkBlazorWebAssembly>true</UsingMicrosoftNETSdkBlazorWebAssembly>";

        (await EvaluateAsync("RunAOTCompilation", "Release", blazor)).Should().Be("true");
        (await EvaluateAsync("RunAOTCompilation", "Release", blazor + Off)).Should().Be("false");
        (await EvaluateAsync("BlazorEnableCompression", "Release", blazor)).Should().Be("true");
        (await EvaluateAsync("RunAOTCompilation", "Debug", blazor)).Should().NotBe("true");
    }

    [Fact]
    public async Task Release_on_a_server_application_with_a_runtime_precompiles_it()
    {
        const string server = "<UsingMicrosoftNETSdkWeb>true</UsingMicrosoftNETSdkWeb>";

        (await EvaluateAsync("PublishReadyToRun", "Release", server + "<RuntimeIdentifier>linux-x64</RuntimeIdentifier>")).Should().Be("true");
        (await EvaluateAsync("PublishReadyToRun", "Release", server)).Should().NotBe("true");
        (await EvaluateAsync("PublishReadyToRun", "Debug", server + "<RuntimeIdentifier>linux-x64</RuntimeIdentifier>")).Should().NotBe("true");
    }

    [Fact]
    public async Task A_project_that_is_none_of_these_gains_no_platform_property()
    {
        (await EvaluateAsync("RunAOTCompilation", "Release")).Should().NotBe("true");
        (await EvaluateAsync("MtouchUseLlvm", "Release")).Should().NotBe("true");
        (await EvaluateAsync("AndroidLinkTool", "Release")).Should().NotBe("r8");
    }

    [Fact]
    public async Task A_setting_of_the_project_is_kept()
    {
        (await EvaluateAsync("Optimize", "Release", "<Optimize>false</Optimize>")).Should().Be("false");
        (await EvaluateAsync("AndroidLinkTool", "Release", Framework(Android, "<AndroidLinkTool>proguard</AndroidLinkTool>"))).Should().Be("proguard");
    }
}
