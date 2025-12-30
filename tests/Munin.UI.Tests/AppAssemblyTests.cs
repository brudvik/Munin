using FluentAssertions;
using Munin.UI;
using Xunit;

namespace Munin.UI.Tests;

public class AppAssemblyTests
{
    [Fact]
    public void AppAssembly_HasExpectedName()
    {
        var name = typeof(App).Assembly.GetName().Name;
        name.Should().Be("Munin");
    }
}
