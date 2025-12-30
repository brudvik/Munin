using System.Text.Json;
using FluentAssertions;
using Munin.Core.Services;
using Xunit;

namespace Munin.Core.Tests;

public class ConfigurationTests
{
    [Fact]
    public void ClientConfiguration_Serializes_And_Deserializes()
    {
        var config = new ClientConfiguration
        {
            Settings = new GeneralSettings
            {
                ReconnectOnDisconnect = false
            }
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<ClientConfiguration>(json);

        restored.Should().NotBeNull();
        restored!.Settings.ReconnectOnDisconnect.Should().BeFalse();
    }
}
