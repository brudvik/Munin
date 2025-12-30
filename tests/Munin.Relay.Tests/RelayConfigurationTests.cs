using FluentAssertions;
using Munin.Relay;
using Xunit;

namespace Munin.Relay.Tests;

public class RelayConfigurationTests
{
    [Fact]
    public void Default_ListenPort_Is_6900()
    {
        var config = new RelayConfiguration();
        config.ListenPort.Should().Be(6900);
    }
}
