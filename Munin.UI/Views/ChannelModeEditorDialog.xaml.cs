using Munin.Core.Models;
using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Dialog for editing channel modes.
/// </summary>
public partial class ChannelModeEditorDialog : Window
{
    private readonly ChannelModeState _originalModes;
    private readonly string _channelName;

    /// <summary>
    /// Gets the mode changes to apply (e.g., "+m-n" or "+k secret").
    /// </summary>
    public string? ModeChanges { get; private set; }

    /// <summary>
    /// Creates a new channel mode editor dialog.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="currentModes">The current mode state.</param>
    public ChannelModeEditorDialog(string channelName, ChannelModeState? currentModes)
    {
        InitializeComponent();
        
        _channelName = channelName;
        _originalModes = currentModes ?? new ChannelModeState();
        
        ChannelNameText.Text = channelName;
        CurrentModesText.Text = _originalModes.GetModeString();
        if (string.IsNullOrEmpty(CurrentModesText.Text))
            CurrentModesText.Text = "(ingen modi satt)";
        
        // Set initial checkbox states
        ModeN.IsChecked = _originalModes.NoExternalMessages;
        ModeT.IsChecked = _originalModes.TopicProtected;
        ModeS.IsChecked = _originalModes.IsSecret;
        ModeM.IsChecked = _originalModes.IsModerated;
        ModeI.IsChecked = _originalModes.IsInviteOnly;
        ModeP.IsChecked = _originalModes.IsPrivate;
        
        // Parameter modes
        var hasKey = _originalModes.Key != null;
        ModeK.IsChecked = hasKey;
        KeyInput.Text = _originalModes.Key ?? "";
        
        var hasLimit = _originalModes.Limit.HasValue;
        ModeL.IsChecked = hasLimit;
        LimitInput.Text = _originalModes.Limit?.ToString() ?? "";
    }

    #region Window Chrome
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #endregion

    private void ModeK_Changed(object sender, RoutedEventArgs e)
    {
        if (ModeK.IsChecked != true)
            KeyInput.Text = "";
    }

    private void ModeL_Changed(object sender, RoutedEventArgs e)
    {
        if (ModeL.IsChecked != true)
            LimitInput.Text = "";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        // Build mode change string
        var addModes = new List<char>();
        var removeModes = new List<char>();
        var addParams = new List<string>();
        var removeParams = new List<string>();

        // Simple modes
        CheckSimpleMode('n', ModeN.IsChecked == true, _originalModes.NoExternalMessages, addModes, removeModes);
        CheckSimpleMode('t', ModeT.IsChecked == true, _originalModes.TopicProtected, addModes, removeModes);
        CheckSimpleMode('s', ModeS.IsChecked == true, _originalModes.IsSecret, addModes, removeModes);
        CheckSimpleMode('m', ModeM.IsChecked == true, _originalModes.IsModerated, addModes, removeModes);
        CheckSimpleMode('i', ModeI.IsChecked == true, _originalModes.IsInviteOnly, addModes, removeModes);
        CheckSimpleMode('p', ModeP.IsChecked == true, _originalModes.IsPrivate, addModes, removeModes);

        // Key mode
        var wantKey = ModeK.IsChecked == true && !string.IsNullOrWhiteSpace(KeyInput.Text);
        var hasKey = _originalModes.Key != null;
        if (wantKey && (!hasKey || _originalModes.Key != KeyInput.Text.Trim()))
        {
            addModes.Add('k');
            addParams.Add(KeyInput.Text.Trim());
        }
        else if (!wantKey && hasKey)
        {
            removeModes.Add('k');
            removeParams.Add(_originalModes.Key!);
        }

        // Limit mode
        int limitValue = 0;
        var wantLimit = ModeL.IsChecked == true && int.TryParse(LimitInput.Text.Trim(), out limitValue) && limitValue > 0;
        var hasLimit = _originalModes.Limit.HasValue;
        if (wantLimit && (!hasLimit || _originalModes.Limit != limitValue))
        {
            addModes.Add('l');
            addParams.Add(limitValue.ToString());
        }
        else if (!wantLimit && hasLimit)
        {
            removeModes.Add('l');
        }

        // Build the mode string
        var modeStr = "";
        if (addModes.Count > 0)
        {
            modeStr += "+" + new string(addModes.ToArray());
        }
        if (removeModes.Count > 0)
        {
            modeStr += "-" + new string(removeModes.ToArray());
        }

        // Add parameters
        var allParams = addParams.Concat(removeParams).ToList();
        if (allParams.Count > 0)
        {
            modeStr += " " + string.Join(" ", allParams);
        }

        ModeChanges = string.IsNullOrEmpty(modeStr) ? null : modeStr;
        
        DialogResult = true;
        Close();
    }

    private static void CheckSimpleMode(char mode, bool wantEnabled, bool isEnabled, 
        List<char> addModes, List<char> removeModes)
    {
        if (wantEnabled && !isEnabled)
            addModes.Add(mode);
        else if (!wantEnabled && isEnabled)
            removeModes.Add(mode);
    }
}
