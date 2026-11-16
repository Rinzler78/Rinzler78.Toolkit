using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

/// <summary>
/// The trimming contract as the quality specification words it: Committed, Analyzed and
/// OutOfScope, applied only where trimming exists.
/// </summary>
public class TrimContractTests
{
    private static async Task<(string Trimmable, string AotCompatible, string TrimAnalyzer)> EvaluateAsync(string properties)
    {
        using var consumer = new ScratchConsumer(properties);
        return (
            await consumer.PropertyAsync("IsTrimmable").ConfigureAwait(false),
            await consumer.PropertyAsync("IsAotCompatible").ConfigureAwait(false),
            await consumer.PropertyAsync("EnableTrimAnalyzer").ConfigureAwait(false));
    }

    [Fact]
    public async Task A_committed_project_is_trimmable_and_aot_compatible()
    {
        var (trimmable, aot, _) = await EvaluateAsync("<RinzlerTrimContract>Committed</RinzlerTrimContract>");

        trimmable.Should().Be("true");
        aot.Should().Be("true");
    }

    [Fact]
    public async Task An_analyzed_project_runs_the_analyser_and_is_not_marked_trimmable()
    {
        var (trimmable, aot, analyzer) = await EvaluateAsync("<RinzlerTrimContract>Analyzed</RinzlerTrimContract>");

        analyzer.Should().Be("true");
        trimmable.Should().NotBe("true");
        aot.Should().NotBe("true");
    }

    [Theory]
    [InlineData("<RinzlerTrimContract>OutOfScope</RinzlerTrimContract>")]
    [InlineData("")]
    public async Task An_out_of_scope_project_and_one_declaring_nothing_are_not_touched(string properties)
    {
        var (trimmable, aot, analyzer) = await EvaluateAsync(properties);

        trimmable.Should().NotBe("true");
        aot.Should().NotBe("true");
        analyzer.Should().NotBe("true");
    }

    // net6.0 has trimming but not the AOT analysers: IsAotCompatible there switches the
    // trim checks off, so a Committed library would look enforced and not be.
    [Fact]
    public async Task A_committed_project_on_net6_is_trimmable_but_not_marked_aot_compatible()
    {
        var (trimmable, aot, _) = await EvaluateAsync(
            "<TargetFramework>net6.0</TargetFramework><RinzlerTrimContract>Committed</RinzlerTrimContract>");

        trimmable.Should().Be("true");
        aot.Should().NotBe("true");
    }

    [Theory]
    [InlineData("net7.0")]
    [InlineData("net8.0")]
    public async Task A_committed_project_is_aot_compatible_from_net7(string framework)
    {
        var (trimmable, aot, _) = await EvaluateAsync(
            $"<TargetFramework>{framework}</TargetFramework><RinzlerTrimContract>Committed</RinzlerTrimContract>");

        trimmable.Should().Be("true");
        aot.Should().Be("true");
    }

    [Theory]
    [InlineData("netstandard2.0", "")]
    [InlineData("net6.0", "true")]
    [InlineData("net8.0", "true")]
    public async Task Each_slice_of_a_multi_targeted_project_gets_what_its_framework_supports(string slice, string trimmable)
    {
        using var consumer = new ScratchConsumer(
            "<TargetFramework />\n<TargetFrameworks>netstandard2.0;net6.0;net8.0</TargetFrameworks><RinzlerTrimContract>Committed</RinzlerTrimContract>");

        var value = await consumer.PropertyAsync("IsTrimmable", slice);

        value.Should().Be(trimmable);
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("Analyzed")]
    public async Task A_framework_that_predates_trimming_is_out_of_scope_whatever_the_value(string contract)
    {
        var (trimmable, aot, analyzer) = await EvaluateAsync(
            $"<TargetFramework>netstandard2.0</TargetFramework><RinzlerTrimContract>{contract}</RinzlerTrimContract>");

        trimmable.Should().NotBe("true");
        aot.Should().NotBe("true");
        analyzer.Should().NotBe("true");
    }

    [Theory]
    [InlineData("Trimmable")]
    [InlineData("AotCompatible")]
    [InlineData("Bogus")]
    public async Task A_value_outside_the_three_fails_the_build_and_names_them(string contract)
    {
        using var consumer = new ScratchConsumer($"<RinzlerTrimContract>{contract}</RinzlerTrimContract>");

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("RinzlerTrimContract must be Committed, Analyzed or OutOfScope");
    }

    [Theory]
    [InlineData("Committed")]
    [InlineData("Analyzed")]
    [InlineData("OutOfScope")]
    public async Task Each_of_the_three_builds(string contract)
    {
        using var consumer = new ScratchConsumer($"<RinzlerTrimContract>{contract}</RinzlerTrimContract>");

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeTrue(result.Output);
    }
}
