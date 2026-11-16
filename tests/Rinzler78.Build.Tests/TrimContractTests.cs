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
