using FluentAssertions;
using Munin.Agent.Configuration;
using Xunit;

namespace Munin.Agent.Tests;

public class AgentConfigurationTests
{
    [Fact]
    public void Default_AllowedIPs_AllowsWildcard()
    {
        var config = new AgentConfiguration();
        config.AllowedIPs.Should().Contain("*");
    }
}
