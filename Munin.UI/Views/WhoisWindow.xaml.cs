using System.Text;
using System.Windows;
using System.Windows.Input;

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

    /// <summary>
    /// Handles mouse drag on the custom title bar to move the window.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            return; // No maximize for dialogs
        
        DragMove();
    }

    /// <summary>
    /// Handles the close button click.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
