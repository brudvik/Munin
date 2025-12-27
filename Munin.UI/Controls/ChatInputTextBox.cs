using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Munin.UI.Controls;

/// <summary>
/// Custom TextBox with Tab completion for nicknames/commands, command history,
/// and popup-based command autocomplete.
/// </summary>
public class ChatInputTextBox : TextBox
{
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _currentInput = string.Empty;
    
    // Tab completion state
    private string _tabCompletionPrefix = string.Empty;
    private int _tabCompletionIndex = -1;
    private List<string> _tabCompletionMatches = new();
    
    // Command popup
    private Popup? _commandPopup;
    private ListBox? _commandListBox;
    private bool _isPopupOpen;

    public static readonly DependencyProperty NicknamesProperty =
        DependencyProperty.Register(
            nameof(Nicknames),
            typeof(IEnumerable<string>),
            typeof(ChatInputTextBox),
            new PropertyMetadata(null));

    /// <summary>
    /// List of nicknames available for tab completion.
    /// </summary>
    public IEnumerable<string>? Nicknames
    {
        get => (IEnumerable<string>?)GetValue(NicknamesProperty);
        set => SetValue(NicknamesProperty, value);
    }

    /// <summary>
    /// List of IRC commands with descriptions for autocomplete.
    /// </summary>
    private static readonly List<CommandInfo> CommandInfos = new()
    {
        // Channel commands
        new("/join", "#channel [key]", "Join a channel"),
        new("/part", "[message]", "Leave current channel"),
        new("/list", "[pattern]", "List available channels"),
        new("/topic", "[new topic]", "View or set channel topic"),
        new("/names", "[channel]", "List users in channel"),
        new("/invite", "nick channel", "Invite user to channel"),
        
        // Messaging commands
        new("/msg", "nick message", "Send private message"),
        new("/query", "nick [message]", "Open private chat"),
        new("/me", "action", "Send action message"),
        new("/notice", "target message", "Send notice"),
        new("/ctcp", "nick command", "Send CTCP request"),
        
        // User commands
        new("/nick", "newnick", "Change your nickname"),
        new("/whois", "nick", "View user information"),
        new("/who", "mask", "List users matching mask"),
        new("/ping", "nick", "Ping a user"),
        new("/ignore", "nick", "Ignore a user"),
        new("/unignore", "nick", "Stop ignoring a user"),
        
        // Moderation commands
        new("/kick", "nick [reason]", "Kick user from channel"),
        new("/ban", "nick", "Ban user from channel"),
        new("/mode", "target modes", "Set channel/user modes"),
        
        // Connection commands
        new("/quit", "[message]", "Disconnect from server"),
        new("/away", "[message]", "Set away status"),
        new("/back", "", "Return from away"),
        new("/raw", "command", "Send raw IRC command"),
        
        // Scripting commands
        new("/script", "list|load|unload|reload|new|console", "Manage scripts"),
        new("/scripts", "", "Open Script Manager window"),
        new("/reload", "[script]", "Reload script(s)"),
        
        // FiSH encryption commands
        new("/setkey", "[target] key", "Set FiSH encryption key"),
        new("/delkey", "[target]", "Remove FiSH encryption key"),
        new("/keyx", "nick", "Initiate DH1080 key exchange"),
        new("/showkey", "[target]", "Show FiSH key (masked)"),
        
        // Window commands
        new("/clear", "", "Clear message window"),
        new("/close", "", "Close current PM window"),
        new("/stats", "", "Show channel statistics"),
        
        // Alias command
        new("/alias", "name command", "Create command alias"),
    };

    private static readonly List<string> Commands = CommandInfos.Select(c => c.Command).ToList();

    public ChatInputTextBox()
    {
        Loaded += OnLoaded;
        TextChanged += OnTextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateCommandPopup();
    }

    /// <summary>
    /// Creates the command autocomplete popup.
    /// </summary>
    private void CreateCommandPopup()
    {
        _commandListBox = new ListBox
        {
            MaxHeight = 250,
            MinWidth = 300,
            Background = FindResource("BackgroundElevatedBrush") as Brush ?? Brushes.Black,
            BorderBrush = FindResource("BorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            ItemTemplate = CreateCommandItemTemplate()
        };
        
        _commandListBox.PreviewKeyDown += CommandListBox_PreviewKeyDown;
        _commandListBox.MouseDoubleClick += CommandListBox_MouseDoubleClick;
        
        _commandPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = FindResource("BackgroundElevatedBrush") as Brush ?? Brushes.Black,
                BorderBrush = FindResource("AccentDimBrush") as Brush ?? Brushes.Blue,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                Child = _commandListBox,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Opacity = 0.3
                }
            }
        };
        
        _commandPopup.Closed += (_, _) => 
        { 
            _isPopupOpen = false;
            Focus();
        };
    }

    /// <summary>
    /// Creates the item template for command list items.
    /// </summary>
    private DataTemplate CreateCommandItemTemplate()
    {
        var template = new DataTemplate(typeof(CommandInfo));
        
        var stackPanel = new FrameworkElementFactory(typeof(StackPanel));
        stackPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        stackPanel.SetValue(StackPanel.MarginProperty, new Thickness(4, 2, 4, 2));
        
        // Command name
        var commandText = new FrameworkElementFactory(typeof(TextBlock));
        commandText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Command"));
        commandText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        commandText.SetValue(TextBlock.ForegroundProperty, FindResource("AccentBrush") ?? Brushes.CornflowerBlue);
        commandText.SetValue(TextBlock.MinWidthProperty, 80.0);
        stackPanel.AppendChild(commandText);
        
        // Usage
        var usageText = new FrameworkElementFactory(typeof(TextBlock));
        usageText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Usage"));
        usageText.SetValue(TextBlock.ForegroundProperty, FindResource("TextMutedBrush") ?? Brushes.Gray);
        usageText.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0));
        usageText.SetValue(TextBlock.MinWidthProperty, 120.0);
        stackPanel.AppendChild(usageText);
        
        // Description
        var descText = new FrameworkElementFactory(typeof(TextBlock));
        descText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Description"));
        descText.SetValue(TextBlock.ForegroundProperty, FindResource("TextSecondaryBrush") ?? Brushes.LightGray);
        stackPanel.AppendChild(descText);
        
        template.VisualTree = stackPanel;
        return template;
    }

    /// <summary>
    /// Handles text changes to show/hide command popup.
    /// </summary>
    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_commandPopup == null || _commandListBox == null)
            return;
            
        var text = Text ?? "";
        
        // Show popup when typing a command
        if (text.StartsWith("/") && !text.Contains(' '))
        {
            var prefix = text.ToLowerInvariant();
            var matches = CommandInfos
                .Where(c => c.Command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            if (matches.Count > 0)
            {
                _commandListBox.ItemsSource = matches;
                _commandListBox.SelectedIndex = 0;
                _commandPopup.IsOpen = true;
                _isPopupOpen = true;
            }
            else
            {
                _commandPopup.IsOpen = false;
                _isPopupOpen = false;
            }
        }
        else
        {
            _commandPopup.IsOpen = false;
            _isPopupOpen = false;
        }
    }

    /// <summary>
    /// Handles keyboard navigation in the command popup.
    /// </summary>
    private void CommandListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            InsertSelectedCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _commandPopup!.IsOpen = false;
            _isPopupOpen = false;
            Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles double-click on command list item.
    /// </summary>
    private void CommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InsertSelectedCommand();
    }

    /// <summary>
    /// Inserts the selected command from the popup.
    /// </summary>
    private void InsertSelectedCommand()
    {
        if (_commandListBox?.SelectedItem is CommandInfo cmd)
        {
            Text = cmd.Command + " ";
            CaretIndex = Text.Length;
            _commandPopup!.IsOpen = false;
            _isPopupOpen = false;
            Focus();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Handle popup navigation when popup is open
        if (_isPopupOpen && _commandListBox != null)
        {
            switch (e.Key)
            {
                case Key.Up:
                    if (_commandListBox.SelectedIndex > 0)
                        _commandListBox.SelectedIndex--;
                    e.Handled = true;
                    return;
                    
                case Key.Down:
                    if (_commandListBox.SelectedIndex < _commandListBox.Items.Count - 1)
                        _commandListBox.SelectedIndex++;
                    e.Handled = true;
                    return;
                    
                case Key.Tab:
                case Key.Return:
                    if (_commandListBox.SelectedItem != null)
                    {
                        InsertSelectedCommand();
                        e.Handled = true;
                        return;
                    }
                    break;
                    
                case Key.Escape:
                    _commandPopup!.IsOpen = false;
                    _isPopupOpen = false;
                    e.Handled = true;
                    return;
            }
        }
        
        switch (e.Key)
        {
            case Key.Tab:
                if (!_isPopupOpen)
                {
                    HandleTabCompletion();
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (Keyboard.Modifiers == ModifierKeys.None && !_isPopupOpen)
                {
                    NavigateHistory(-1);
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (Keyboard.Modifiers == ModifierKeys.None && !_isPopupOpen)
                {
                    NavigateHistory(1);
                    e.Handled = true;
                }
                break;

            case Key.Return:
                if (_isPopupOpen)
                {
                    // Close popup, don't send
                    _commandPopup!.IsOpen = false;
                    _isPopupOpen = false;
                    e.Handled = true;
                }
                else if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    // Insert newline
                    var caretIndex = CaretIndex;
                    Text = Text.Insert(caretIndex, Environment.NewLine);
                    CaretIndex = caretIndex + Environment.NewLine.Length;
                    e.Handled = true;
                }
                else
                {
                    // Save to history
                    AddToHistory(Text);
                    // Let the base handle sending (via binding)
                }
                break;
                
            case Key.Escape:
                if (_isPopupOpen)
                {
                    _commandPopup!.IsOpen = false;
                    _isPopupOpen = false;
                    e.Handled = true;
                }
                break;

            default:
                // Reset tab completion when typing other keys
                ResetTabCompletion();
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Handles tab completion for nicknames and commands.
    /// Cycles through possible completions on repeated tab presses.
    /// </summary>
    private void HandleTabCompletion()
    {
        if (string.IsNullOrEmpty(Text))
            return;

        // First tab press - find matches
        if (_tabCompletionIndex < 0)
        {
            var caretPos = CaretIndex;
            var textBefore = Text[..caretPos];
            
            // Find the word being completed
            var lastSpace = textBefore.LastIndexOf(' ');
            _tabCompletionPrefix = lastSpace >= 0 ? textBefore[(lastSpace + 1)..] : textBefore;

            if (string.IsNullOrEmpty(_tabCompletionPrefix))
                return;

            // Build list of matches
            _tabCompletionMatches.Clear();

            // Command completion
            if (_tabCompletionPrefix.StartsWith("/"))
            {
                _tabCompletionMatches.AddRange(
                    Commands.Where(c => c.StartsWith(_tabCompletionPrefix, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                // Nickname completion
                if (Nicknames != null)
                {
                    _tabCompletionMatches.AddRange(
                        Nicknames.Where(n => n.StartsWith(_tabCompletionPrefix, StringComparison.OrdinalIgnoreCase)));
                }
            }

            if (_tabCompletionMatches.Count == 0)
                return;

            _tabCompletionIndex = 0;
        }
        else
        {
            // Cycle through matches
            _tabCompletionIndex = (_tabCompletionIndex + 1) % _tabCompletionMatches.Count;
        }

        // Apply completion
        var match = _tabCompletionMatches[_tabCompletionIndex];
        var caretPosition = CaretIndex;
        var textBeforeCaret = Text[..caretPosition];
        var lastSpaceIndex = textBeforeCaret.LastIndexOf(' ');
        var beforePrefix = lastSpaceIndex >= 0 ? textBeforeCaret[..(lastSpaceIndex + 1)] : "";
        var afterCaret = Text[caretPosition..];

        // Add colon suffix for nicknames at start of line
        var suffix = "";
        if (!match.StartsWith("/") && string.IsNullOrEmpty(beforePrefix.Trim()))
        {
            suffix = ": ";
        }
        else if (!match.StartsWith("/") && !string.IsNullOrEmpty(afterCaret) && !afterCaret.StartsWith(" "))
        {
            suffix = " ";
        }

        Text = beforePrefix + match + suffix + afterCaret;
        CaretIndex = beforePrefix.Length + match.Length + suffix.Length;
    }

    /// <summary>
    /// Resets the tab completion state when the text changes.
    /// </summary>
    private void ResetTabCompletion()
    {
        _tabCompletionPrefix = string.Empty;
        _tabCompletionIndex = -1;
        _tabCompletionMatches.Clear();
    }

    /// <summary>
    /// Navigates through the command history.
    /// </summary>
    /// <param name="direction">The direction to navigate: -1 for previous (older), 1 for next (newer).</param>
    private void NavigateHistory(int direction)
    {
        if (_commandHistory.Count == 0)
            return;

        // Save current input when starting navigation
        if (_historyIndex < 0)
        {
            _currentInput = Text;
        }

        var newIndex = _historyIndex + direction;

        if (newIndex < -1)
            newIndex = -1;
        else if (newIndex >= _commandHistory.Count)
            newIndex = _commandHistory.Count - 1;

        _historyIndex = newIndex;

        if (_historyIndex < 0)
        {
            // Restore current input
            Text = _currentInput;
        }
        else
        {
            Text = _commandHistory[_commandHistory.Count - 1 - _historyIndex];
        }

        CaretIndex = Text.Length;
    }

    /// <summary>
    /// Adds a command or message to the input history.
    /// Prevents duplicate consecutive entries and limits history size.
    /// </summary>
    /// <param name="text">The text to add to the history.</param>
    public void AddToHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Don't add duplicates
        if (_commandHistory.Count > 0 && _commandHistory[^1] == text)
            return;

        _commandHistory.Add(text);
        
        // Limit history size
        while (_commandHistory.Count > 100)
        {
            _commandHistory.RemoveAt(0);
        }

        // Reset history navigation
        _historyIndex = -1;
        _currentInput = string.Empty;
    }
}

/// <summary>
/// Represents an IRC command with usage information for autocomplete.
/// </summary>
/// <param name="Command">The command name (e.g., "/join").</param>
/// <param name="Usage">Usage syntax for the command.</param>
/// <param name="Description">Brief description of what the command does.</param>
public record CommandInfo(string Command, string Usage, string Description);
