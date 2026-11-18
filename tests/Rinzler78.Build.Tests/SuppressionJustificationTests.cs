using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

/// <summary>
/// A suppression is a decision, and a decision carries its reason: a warning disabled
/// without a justification on the same line, or a suppressed message without one, fails the
/// build, naming the file and the rule.
/// </summary>
public class SuppressionJustificationTests
{
    // The rule reads the source of this repository too, so a directive spelled in full at the
    // start of a line, even inside a test string, would be one.
    private const string Hash = "#";
    private const string Attribute = "Suppress" + "Message";

    private static async Task<BuildResult> BuildAsync(string source, string file = "Source.cs")
    {
        using var consumer = new ScratchConsumer("<NoWarn>$(NoWarn);CS0168</NoWarn>");
        consumer.Write(file, source);
        return await consumer.BuildAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_pragma_without_a_justification_fails_naming_the_file_and_the_rule()
    {
        var result = await BuildAsync($$"""
            {{Hash}}pragma warning disable CS0168
            internal static class Holder { }
            """);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Source.cs").And.Contain("CS0168").And.Contain("justification");
    }

    [Theory]
    [InlineData("pragma warning disable CS0168 //")]
    [InlineData("pragma warning disable CS0168   ")]
    public async Task A_pragma_with_an_empty_comment_or_none_is_unjustified(string line)
    {
        var result = await BuildAsync($"{Hash}{line}\ninternal static class Holder {{ }}\n");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_pragma_with_a_justification_on_the_same_line_is_accepted()
    {
        var result = await BuildAsync($$"""
            {{Hash}}pragma warning disable CS0168 // the generated code declares it; tracked in ADR-0007
            internal static class Holder { }
            {{Hash}}pragma warning restore CS0168
            """);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_suppress_message_without_a_justification_fails()
    {
        var result = await BuildAsync($$"""
            using System.Diagnostics.CodeAnalysis;
            [{{Attribute}}("Design", "CA1051")]
            internal static class Holder { }
            """);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Source.cs").And.Contain("SuppressMessage").And.Contain("justification");
    }

    [Fact]
    public async Task A_suppress_message_with_an_empty_justification_fails()
    {
        var result = await BuildAsync($$"""
            using System.Diagnostics.CodeAnalysis;
            [{{Attribute}}("Design", "CA1051", Justification = "{{string.Empty}}")]
            internal static class Holder { }
            """);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_suppress_message_with_a_justification_is_accepted()
    {
        var result = await BuildAsync($$"""
            using System.Diagnostics.CodeAnalysis;
            [{{Attribute}}("Design", "CA1051", Justification = "serialised by the vendor tool; ADR-0007")]
            internal static class Holder { }
            """);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task Generated_code_is_not_scanned()
    {
        using var consumer = new ScratchConsumer("");
        consumer.Write("obj/Generated.cs", $"{Hash}pragma warning disable CS0168\ninternal static class Generated {{ }}\n");

        (await consumer.BuildAsync()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_project_with_no_suppression_builds()
    {
        var result = await BuildAsync("internal static class Holder { }\n");

        result.Succeeded.Should().BeTrue(result.Output);
    }
}
