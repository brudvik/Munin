using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for adding or editing an agent connection.
/// </summary>
public partial class AddAgentDialog : Window
{
    private readonly AddAgentDialogViewModel _viewModel;

    /// <summary>
    /// Gets the resulting agent configuration if the dialog was accepted.
    /// </summary>
    public AgentDialogResult? Result { get; private set; }

    /// <summary>
    /// Creates a new Add Agent dialog.
    /// </summary>
    public AddAgentDialog()
    {
        InitializeComponent();
        _viewModel = new AddAgentDialogViewModel();
        DataContext = _viewModel;

        Loaded += (s, e) => AgentNameBox.Focus();
    }

    /// <summary>
    /// Creates an Edit Agent dialog with existing values.
    /// </summary>
    /// <param name="name">Existing agent name.</param>
    /// <param name="host">Existing host.</param>
    /// <param name="port">Existing port.</param>
    /// <param name="useTls">Existing TLS setting.</param>
    /// <param name="authToken">Existing auth token.</param>
    public AddAgentDialog(string name, string host, int port, bool useTls, string authToken)
    {
        InitializeComponent();
        _viewModel = new AddAgentDialogViewModel
        {
            AgentName = name,
            Host = host,
            Port = port,
            UseTls = useTls,
            AuthToken = authToken,
            SaveCredentials = true
        };
        DataContext = _viewModel;

        // Set password box value
        Loaded += (s, e) =>
        {
            AuthTokenBox.Password = authToken;
            AgentNameBox.Focus();
        };

        // Change title for edit mode
        Title = "Edit Agent";
    }

    private void AuthTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.AuthToken = AuthTokenBox.Password;
    }

    private void PasteToken_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            AuthTokenBox.Password = Clipboard.GetText().Trim();
            _viewModel.AuthToken = AuthTokenBox.Password;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        TestResultText.Text = "Testing connection...";
        TestResultText.Foreground = (Brush)FindResource("SecondaryForegroundBrush");

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await client.ConnectAsync(_viewModel.Host, _viewModel.Port, cts.Token);

            TestResultText.Text = "✓ Connection successful!";
            TestResultText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
        }
        catch (OperationCanceledException)
        {
            TestResultText.Text = "✗ Connection timed out";
            TestResultText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"✗ {ex.Message}";
            TestResultText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(_viewModel.AgentName))
        {
            MessageBox.Show(this, 
                "Please enter a name for the agent.", 
                "Validation Error", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
            AgentNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.Host))
        {
            MessageBox.Show(this, 
                "Please enter a host address.", 
                "Validation Error", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
            return;
        }

        if (_viewModel.Port < 1 || _viewModel.Port > 65535)
        {
            MessageBox.Show(this, 
                "Please enter a valid port number (1-65535).", 
                "Validation Error", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
            return;
        }

        Result = new AgentDialogResult
        {
            Name = _viewModel.AgentName.Trim(),
            Host = _viewModel.Host.Trim(),
            Port = _viewModel.Port,
            UseTls = _viewModel.UseTls,
            AuthToken = _viewModel.AuthToken,
            SaveCredentials = _viewModel.SaveCredentials
        };

        DialogResult = true;
        Close();
    }
}

/// <summary>
/// ViewModel for the Add Agent dialog.
/// </summary>
public class AddAgentDialogViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _agentName = "";
    private string _host = "localhost";
    private int _port = 5550;
    private bool _useTls = true;
    private string _authToken = "";
    private bool _saveCredentials = true;

    public string AgentName
    {
        get => _agentName;
        set => SetProperty(ref _agentName, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public bool UseTls
    {
        get => _useTls;
        set => SetProperty(ref _useTls, value);
    }

    public string AuthToken
    {
        get => _authToken;
        set => SetProperty(ref _authToken, value);
    }

    public bool SaveCredentials
    {
        get => _saveCredentials;
        set => SetProperty(ref _saveCredentials, value);
    }
}

/// <summary>
/// Result from the Add Agent dialog.
/// </summary>
public class AgentDialogResult
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public bool UseTls { get; set; }
    public string AuthToken { get; set; } = "";
    public bool SaveCredentials { get; set; }
}
