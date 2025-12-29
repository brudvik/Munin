using Munin.Core.Services;
using Munin.UI.ViewModels;
using System.Windows;

namespace Munin.UI.Views;

/// <summary>
/// Window for managing remote Munin agents.
/// </summary>
public partial class AgentManagerWindow : Window
{
    public AgentManagerWindow(ConfigurationService configService, EncryptionService encryptionService)
    {
        InitializeComponent();
        DataContext = new AgentManagerViewModel(configService, encryptionService);
    }
}
