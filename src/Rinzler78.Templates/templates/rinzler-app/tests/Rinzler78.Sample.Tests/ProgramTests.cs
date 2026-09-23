using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Sample.Tests;

public class ProgramTests
{
    [Fact]
    public void Describe_reports_an_empty_command_line()
    {
        Program.Describe([]).Should().Be("nothing to do");
    }

    [Theory]
    [InlineData(new[] { "build" }, "asked for: build")]
    [InlineData(new[] { "deploy", "device" }, "asked for: deploy device")]
    public void Describe_echoes_what_it_was_given(string[] args, string expected)
    {
        Program.Describe(args).Should().Be(expected);
    }
}
