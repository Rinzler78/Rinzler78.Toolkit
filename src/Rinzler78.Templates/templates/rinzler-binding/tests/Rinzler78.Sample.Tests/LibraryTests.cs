using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Sample.Tests;

public class LibraryTests
{
    [Fact]
    public void Name_is_the_simple_name_of_the_assembly_under_test()
    {
        Library.Name.Should().Be("Rinzler78.Sample");
    }
}
