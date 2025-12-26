using Munin.Core.Scripting;
using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Window for viewing script output and executing Lua code interactively.
/// </summary>
public partial class ScriptConsoleWindow : Window
{
    private readonly ScriptManager _scriptManager;

    public ScriptConsoleWindow(ScriptManager scriptManager)
    {
        InitializeComponent();
        _scriptManager = scriptManager;
        
        UpdateScriptCount();
        
        AddOutput("System", "Script console ready. Type Lua code and press Enter to execute.");
        AddOutput("System", $"Scripts directory: {_scriptManager.Context.ScriptsDirectory}");
    }

    #region Window Chrome
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    
    #endregion

    /// <summary>
    /// Adds output from a script.
    /// </summary>
    public void AddOutput(string scriptName, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        AppendLine($"[{timestamp}] [{scriptName}] {message}");
    }

    /// <summary>
    /// Adds an error from a script.
    /// </summary>
    public void AddError(string scriptName, string error)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        AppendLine($"[{timestamp}] [ERROR: {scriptName}] {error}");
    }

    private void AppendLine(string line)
    {
        if (OutputTextBox.Text.Length > 0)
        {
            OutputTextBox.AppendText(Environment.NewLine);
        }
        OutputTextBox.AppendText(line);
        OutputTextBox.ScrollToEnd();
    }

    private void UpdateScriptCount()
    {
        var count = _scriptManager.GetLoadedScripts().Count();
        ScriptCountText.Text = count.ToString();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteInputAsync();
    }

    private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            await ExecuteInputAsync();
            e.Handled = true;
        }
    }

    private async Task ExecuteInputAsync()
    {
        var code = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(code)) return;
        
        InputTextBox.Text = string.Empty;
        
        AddOutput("Console", $"> {code}");
        
        try
        {
            var result = await _scriptManager.ExecuteAsync(code);
            if (!result.Success)
            {
                AddError("Console", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            AddError("Console", ex.Message);
        }
    }

    private async void ReloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        AddOutput("System", "Reloading all scripts...");
        
        var scripts = _scriptManager.GetLoadedScripts().ToList();
        foreach (var script in scripts)
        {
            var result = await _scriptManager.ReloadScriptAsync(script.Name);
            if (result.Success)
            {
                AddOutput("System", $"Reloaded: {script.Name}");
            }
            else
            {
                AddError("System", $"Failed to reload {script.Name}: {result.Error}");
            }
        }
        
        // Also load any new scripts
        await _scriptManager.LoadAllScriptsAsync();
        
        UpdateScriptCount();
        AddOutput("System", $"Reload complete. {_scriptManager.GetLoadedScripts().Count()} scripts loaded.");
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        OutputTextBox.Clear();
    }
}
