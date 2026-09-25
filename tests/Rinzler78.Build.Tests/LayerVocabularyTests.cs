using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Build.Tests;

public class LayerVocabularyTests
{
    [Fact]
    public async Task A_layer_named_in_a_delimited_vocabulary_is_accepted()
    {
        using var consumer = new ScratchConsumer("""
            <RinzlerKnownLayers>Binding;Wrapper</RinzlerKnownLayers>
            <RinzlerLayer>Binding</RinzlerLayer>
            """);

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_vocabulary_written_one_layer_per_line_is_accepted()
    {
        using var consumer = new ScratchConsumer("""
            <RinzlerKnownLayers>
              Binding;
              Wrapper
            </RinzlerKnownLayers>
            <RinzlerLayer>Wrapper</RinzlerLayer>
            """);

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public async Task A_layer_outside_the_vocabulary_is_refused_by_name()
    {
        using var consumer = new ScratchConsumer("""
            <RinzlerKnownLayers>Binding;Wrapper</RinzlerKnownLayers>
            <RinzlerLayer>Bindings</RinzlerLayer>
            """);

        var result = await consumer.BuildAsync();

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("RinzlerLayer 'Bindings' is not one of");
    }
}
