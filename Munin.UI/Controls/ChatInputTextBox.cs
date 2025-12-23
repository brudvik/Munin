using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Munin.UI.Controls;

/// <summary>
/// Custom TextBox with Tab completion for nicknames/commands and command history.
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

    private static readonly List<string> Commands = new()
    {
        "/join", "/part", "/quit", "/msg", "/me", "/nick", "/topic", "/kick", "/ban",
        "/mode", "/whois", "/who", "/names", "/list", "/ignore", "/unignore", "/clear",
        "/query", "/notice", "/away", "/back", "/invite", "/ctcp", "/ping"
    };

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
                HandleTabCompletion();
                e.Handled = true;
                break;

            case Key.Up:
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    NavigateHistory(-1);
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    NavigateHistory(1);
                    e.Handled = true;
                }
                break;

            case Key.Return:
                if (Keyboard.Modifiers == ModifierKeys.Shift)
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
