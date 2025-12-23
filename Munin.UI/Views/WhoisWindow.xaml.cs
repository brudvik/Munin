using System.Text;
using System.Windows;

namespace Munin.UI.Views;

/// <summary>
/// Simple window that displays WHOIS information for an IRC user.
/// Shows raw WHOIS response lines from the server.
/// </summary>
public partial class WhoisWindow : Window
{
    private readonly StringBuilder _infoBuilder = new();

    public WhoisWindow(string nickname)
    {
        InitializeComponent();
        NicknameText.Text = nickname;
        InfoText.Text = "Loading...";
    }

    /// <summary>
    /// Adds a line of WHOIS information to the display.
    /// </summary>
    /// <param name="info">The WHOIS info line to add.</param>
    public void AddInfo(string info)
    {
        _infoBuilder.AppendLine(info);
        InfoText.Text = _infoBuilder.ToString();
    }

    /// <summary>
    /// Marks the WHOIS query as complete. Displays a message if no information was received.
    /// </summary>
    public void SetComplete()
    {
        if (_infoBuilder.Length == 0)
        {
            InfoText.Text = "No information available.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
