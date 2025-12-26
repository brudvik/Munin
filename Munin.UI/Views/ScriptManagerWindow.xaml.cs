using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Munin.Core.Scripting;
using Munin.Core.Scripting.Triggers;
using Munin.UI.Resources;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace Munin.UI.Views;

/// <summary>
/// Comprehensive script management window with editor, list, trigger builder, and quick actions.
/// </summary>
public partial class ScriptManagerWindow : Window
{
    private readonly ScriptManager _scriptManager;
    private readonly ObservableCollection<ScriptDisplayItem> _scripts = new();
    private readonly ObservableCollection<TriggerDisplayItem> _triggers = new();
    private readonly List<QuickActionTemplate> _quickActions;
    
    private string? _currentFilePath;
    private bool _hasUnsavedChanges;

    public ScriptManagerWindow(ScriptManager scriptManager)
    {
        InitializeComponent();
        _scriptManager = scriptManager;
        
        ScriptListView.ItemsSource = _scripts;
        TriggerListView.ItemsSource = _triggers;
        
        _quickActions = CreateQuickActionTemplates();
        
        // Configure code editor
        SetupCodeEditor();
        
        // Subscribe to script events
        _scriptManager.ScriptLoaded += OnScriptLoaded;
        _scriptManager.ScriptUnloaded += OnScriptUnloaded;
        _scriptManager.ScriptOutput += OnScriptOutput;
        _scriptManager.ScriptError += OnScriptError;
        
        // Update maximize icon when window state changes
        StateChanged += OnStateChanged;
        
        // Defer initial data loading until window is fully loaded
        Loaded += (s, e) =>
        {
            RefreshFileTree();
            RefreshScriptList();
            RefreshTriggerList();
            PopulateQuickActions();
            UpdateLoadedCount();
        };
    }
    
    /// <summary>
    /// Sets up the AvalonEdit code editor with syntax highlighting and theme.
    /// </summary>
    private void SetupCodeEditor()
    {
        // Load Lua syntax highlighting
        LoadSyntaxHighlighting("Lua", "LuaSyntax.xshd");
        LoadSyntaxHighlighting("JSON", "JsonSyntax.xshd");
        
        // Set default to Lua
        CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Lua");
        
        // Configure editor options
        CodeEditor.Options.EnableHyperlinks = false;
        CodeEditor.Options.EnableEmailHyperlinks = false;
        CodeEditor.Options.ConvertTabsToSpaces = true;
        CodeEditor.Options.IndentationSize = 4;
        CodeEditor.Options.ShowSpaces = false;
        CodeEditor.Options.ShowTabs = false;
        CodeEditor.Options.HighlightCurrentLine = true;
        
        // Set colors for dark theme
        CodeEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        CodeEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
        CodeEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(100, 0, 122, 204));
        CodeEditor.TextArea.SelectionForeground = null; // Use default foreground
    }
    
    /// <summary>
    /// Loads a syntax highlighting definition from an embedded resource.
    /// </summary>
    private void LoadSyntaxHighlighting(string name, string resourceFileName)
    {
        try
        {
            var assembly = typeof(ScriptManagerWindow).Assembly;
            var resourceName = $"Munin.UI.Resources.{resourceFileName}";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Try loading from file as fallback
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", resourceFileName);
                if (File.Exists(filePath))
                {
                    using var fileStream = File.OpenRead(filePath);
                    using var reader = new XmlTextReader(fileStream);
                    var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting(name, new[] { $".{name.ToLower()}" }, highlighting);
                }
                return;
            }
            
            using var xmlReader = new XmlTextReader(stream);
            var definition = HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(name, new[] { $".{name.ToLower()}" }, definition);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load syntax highlighting for {name}: {ex.Message}");
        }
    }

    #region Window Chrome
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            if (WindowState == WindowState.Maximized)
            {
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                Left = point.X - Width / 2;
                Top = point.Y - 20;
            }
            DragMove();
        }
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    
    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon != null)
        {
            MaximizeIcon.Data = WindowState == WindowState.Maximized
                ? Geometry.Parse("M0,3 H7 V10 H0 Z M3,0 H10 V7 H7 M3,3 V0")
                : Geometry.Parse("M0,0 H10 V10 H0 Z");
        }
    }
    
    #endregion

    #region File Tree

    private void RefreshFileTree()
    {
        FileTreeView.Items.Clear();
        
        var scriptsDir = _scriptManager.Context.ScriptsDirectory;
        if (!Directory.Exists(scriptsDir))
        {
            Directory.CreateDirectory(scriptsDir);
        }
        
        var rootItem = new TreeViewItem
        {
            Header = "📁 scripts",
            IsExpanded = true,
            Tag = scriptsDir
        };
        
        AddDirectoryItems(rootItem, scriptsDir);
        FileTreeView.Items.Add(rootItem);
    }

    private void AddDirectoryItems(TreeViewItem parent, string path)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirItem = new TreeViewItem
                {
                    Header = $"📁 {Path.GetFileName(dir)}",
                    Tag = dir
                };
                AddDirectoryItems(dirItem, dir);
                parent.Items.Add(dirItem);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                var ext = Path.GetExtension(file).ToLower();
                var icon = ext switch
                {
                    ".lua" => "🌙",
                    ".json" when file.Contains(".triggers") => "⚡",
                    ".json" => "📄",
                    ".cs" => "🔷",
                    _ => "📄"
                };
                
                parent.Items.Add(new TreeViewItem
                {
                    Header = $"{icon} {Path.GetFileName(file)}",
                    Tag = file
                });
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error reading directory: {ex.Message}");
        }
    }

    private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string path && File.Exists(path))
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    Strings.ScriptManager_UnsavedChanges,
                    Strings.ScriptManager_Title,
                    MessageBoxButton.YesNoCancel);
                
                if (result == MessageBoxResult.Yes)
                    SaveCurrentFile();
                else if (result == MessageBoxResult.Cancel)
                    return;
            }
            
            OpenFile(path);
        }
    }

    private void OpenFile(string filePath)
    {
        try
        {
            _currentFilePath = filePath;
            CodeEditor.Text = File.ReadAllText(filePath);
            CurrentFileLabel.Text = Path.GetFileName(filePath);
            _hasUnsavedChanges = false;
            SaveButton.IsEnabled = false;
            RunButton.IsEnabled = filePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
            
            // Set syntax highlighting based on file extension
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            CodeEditor.SyntaxHighlighting = extension switch
            {
                ".lua" => HighlightingManager.Instance.GetDefinition("Lua"),
                ".json" => HighlightingManager.Instance.GetDefinition("JSON"),
                _ => null
            };
        }
        catch (Exception ex)
        {
            AppendOutput($"Error opening file: {ex.Message}");
        }
    }

    #endregion

    #region Editor Actions

    private void CodeEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_currentFilePath != null)
        {
            _hasUnsavedChanges = true;
            SaveButton.IsEnabled = true;
            
            if (!CurrentFileLabel.Text.EndsWith("*"))
                CurrentFileLabel.Text += " *";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentFile();
    }

    private void SaveCurrentFile()
    {
        if (string.IsNullOrEmpty(_currentFilePath)) return;
        
        try
        {
            File.WriteAllText(_currentFilePath, CodeEditor.Text);
            _hasUnsavedChanges = false;
            SaveButton.IsEnabled = false;
            CurrentFileLabel.Text = Path.GetFileName(_currentFilePath);
            AppendOutput($"Saved: {Path.GetFileName(_currentFilePath)}");
            StatusText.Text = Strings.ScriptManager_FileSaved;
            
            // Auto-reload the script if it's loaded
            var scriptName = Path.GetFileNameWithoutExtension(_currentFilePath);
            var loaded = _scriptManager.GetLoadedScripts().FirstOrDefault(s => s.Name == scriptName);
            if (loaded != null)
            {
                _ = _scriptManager.ReloadScriptAsync(scriptName);
                AppendOutput($"Reloaded: {scriptName}");
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error saving: {ex.Message}");
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CodeEditor.Text)) return;
        
        AppendOutput("--- Running script ---");
        
        var result = await _scriptManager.ExecuteAsync(CodeEditor.Text);
        
        if (!result.Success)
        {
            AppendOutput($"Error: {result.Error}");
        }
        else
        {
            AppendOutput("Script executed successfully");
        }
    }

    private void NewScriptButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog(
            Strings.ScriptManager_NewScript,
            Strings.ScriptManager_EnterScriptName,
            "my_script.lua");
        
        if (dialog.ShowDialog() == true)
        {
            var name = dialog.InputText;
            if (!name.EndsWith(".lua"))
                name += ".lua";
            
            var path = Path.Combine(_scriptManager.Context.ScriptsDirectory, name);
            
            var template = @"-- " + Path.GetFileNameWithoutExtension(name) + @"
-- Created: " + DateTime.Now.ToString("yyyy-MM-dd") + @"

-- Event handlers
irc.on(""message"", function(e)
    -- Handle messages here
    -- if e.text:match(""hello"") then
    --     e:reply(""Hello, "" .. e.nick .. ""!"")
    -- end
end)

-- Initialization
print(""Script loaded: " + Path.GetFileNameWithoutExtension(name) + @""")
";
            
            try
            {
                File.WriteAllText(path, template);
                RefreshFileTree();
                OpenFile(path);
                AppendOutput($"Created: {name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating script: {ex.Message}");
            }
        }
    }

    private void NewTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog(
            Strings.ScriptManager_NewTrigger,
            Strings.ScriptManager_EnterTriggerName,
            "my_triggers");
        
        if (dialog.ShowDialog() == true)
        {
            var name = dialog.InputText;
            if (!name.EndsWith(".triggers.json"))
                name += ".triggers.json";
            
            var path = Path.Combine(_scriptManager.Context.ScriptsDirectory, name);
            
            var template = new
            {
                triggers = new[]
                {
                    new
                    {
                        on = "message",
                        match = "hello",
                        action = "reply",
                        message = "Hello there!"
                    }
                }
            };
            
            try
            {
                var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                RefreshFileTree();
                OpenFile(path);
                AppendOutput($"Created: {name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating trigger file: {ex.Message}");
            }
        }
    }

    private void DeleteFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileTreeView.SelectedItem is TreeViewItem item && item.Tag is string path && File.Exists(path))
        {
            var result = MessageBox.Show(
                string.Format(Strings.ScriptManager_ConfirmDelete, Path.GetFileName(path)),
                Strings.ScriptManager_Delete,
                MessageBoxButton.YesNo);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var scriptName = Path.GetFileNameWithoutExtension(path);
                    _scriptManager.UnloadScript(scriptName);
                    File.Delete(path);
                    
                    if (_currentFilePath == path)
                    {
                        _currentFilePath = null;
                        CodeEditor.Text = string.Empty;
                        CurrentFileLabel.Text = Strings.ScriptManager_NoFileSelected;
                        SaveButton.IsEnabled = false;
                        RunButton.IsEnabled = false;
                    }
                    
                    RefreshFileTree();
                    RefreshScriptList();
                    AppendOutput($"Deleted: {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting file: {ex.Message}");
                }
            }
        }
    }

    #endregion

    #region Script List

    private void RefreshScriptList()
    {
        _scripts.Clear();
        
        foreach (var script in _scriptManager.GetLoadedScripts())
        {
            var ext = Path.GetExtension(script.FilePath).ToLower();
            _scripts.Add(new ScriptDisplayItem
            {
                Name = script.Name,
                FilePath = script.FilePath,
                LoadedAt = script.LoadedAt,
                IsEnabled = true,
                TypeIcon = ext switch
                {
                    ".lua" => "🌙",
                    ".json" => "⚡",
                    ".cs" or ".dll" => "🔷",
                    _ => "📄"
                }
            });
        }
    }

    private void ScriptSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = ScriptSearchBox.Text.ToLower();
        
        foreach (var item in ScriptListView.Items.Cast<ScriptDisplayItem>())
        {
            var container = ScriptListView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
            if (container != null)
            {
                container.Visibility = string.IsNullOrEmpty(filter) || 
                                       item.Name.ToLower().Contains(filter) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }
    }

    private void ScriptToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is ScriptDisplayItem item)
        {
            if (item.IsEnabled)
            {
                // Re-enable (reload) script
                _ = _scriptManager.LoadScriptAsync(item.FilePath);
                AppendOutput($"Enabled: {item.Name}");
            }
            else
            {
                // Disable (unload) script
                _scriptManager.UnloadScript(item.Name);
                AppendOutput($"Disabled: {item.Name}");
            }
        }
    }

    private async void ReloadScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string scriptName)
        {
            var result = await _scriptManager.ReloadScriptAsync(scriptName);
            if (result.Success)
            {
                AppendOutput($"Reloaded: {scriptName}");
                StatusText.Text = $"Reloaded {scriptName}";
            }
            else
            {
                AppendOutput($"Error reloading {scriptName}: {result.Error}");
            }
        }
    }

    private void EditScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string filePath)
        {
            MainTabControl.SelectedItem = EditorTab;
            OpenFile(filePath);
        }
    }

    #endregion

    #region Trigger Builder

    private void RefreshTriggerList()
    {
        _triggers.Clear();
        
        var scriptsDir = _scriptManager.Context.ScriptsDirectory;
        if (!Directory.Exists(scriptsDir)) return;
        
        foreach (var file in Directory.GetFiles(scriptsDir, "*.triggers.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var triggerFile = JsonSerializer.Deserialize<TriggerFile>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (triggerFile?.Triggers != null)
                {
                    foreach (var trigger in triggerFile.Triggers)
                    {
                        _triggers.Add(new TriggerDisplayItem
                        {
                            DisplayName = trigger.Match ?? trigger.On,
                            EventType = trigger.On,
                            ActionSummary = GetActionSummary(trigger),
                            IsEnabled = true,
                            SourceFile = file,
                            TriggerData = trigger
                        });
                    }
                }
            }
            catch
            {
                // Skip invalid files
            }
        }
    }

    private string GetActionSummary(TriggerDefinition trigger)
    {
        return trigger.Action?.ToLower() switch
        {
            "reply" => $"Reply: \"{trigger.Message?.Truncate(30)}\"",
            "send" => $"Send to {trigger.Channel}: \"{trigger.Message?.Truncate(20)}\"",
            "command" => $"Run: {trigger.Command}",
            "highlight" => "Highlight message",
            "sound" => $"Play sound: {trigger.Sound}",
            "notify" => "Show notification",
            _ => trigger.Action ?? "Unknown"
        };
    }

    private void TriggerListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Future: show trigger details panel
    }

    private void AddTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TriggerBuilderDialog();
        dialog.Owner = this;
        
        if (dialog.ShowDialog() == true)
        {
            SaveTriggerToFile(dialog.CreatedTrigger);
            RefreshTriggerList();
        }
    }

    private void EditTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is TriggerDisplayItem item)
        {
            var dialog = new TriggerBuilderDialog(item.TriggerData);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                // Update the trigger in file
                UpdateTriggerInFile(item.SourceFile, item.TriggerData, dialog.CreatedTrigger);
                RefreshTriggerList();
            }
        }
    }

    private void DeleteTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is TriggerDisplayItem item)
        {
            var result = MessageBox.Show(
                Strings.ScriptManager_ConfirmDeleteTrigger,
                Strings.ScriptManager_Delete,
                MessageBoxButton.YesNo);
            
            if (result == MessageBoxResult.Yes)
            {
                RemoveTriggerFromFile(item.SourceFile, item.TriggerData);
                RefreshTriggerList();
            }
        }
    }

    private void SaveTriggerToFile(TriggerDefinition trigger)
    {
        var defaultFile = Path.Combine(_scriptManager.Context.ScriptsDirectory, "custom.triggers.json");
        
        TriggerFile? triggerFile;
        if (File.Exists(defaultFile))
        {
            var json = File.ReadAllText(defaultFile);
            triggerFile = JsonSerializer.Deserialize<TriggerFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new TriggerFile { Triggers = new List<TriggerDefinition>() };
        }
        else
        {
            triggerFile = new TriggerFile { Triggers = new List<TriggerDefinition>() };
        }
        
        triggerFile.Triggers.Add(trigger);
        
        var outputJson = JsonSerializer.Serialize(triggerFile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(defaultFile, outputJson);
        
        // Reload triggers
        _ = _scriptManager.LoadScriptAsync(defaultFile);
    }

    private void UpdateTriggerInFile(string filePath, TriggerDefinition oldTrigger, TriggerDefinition newTrigger)
    {
        var json = File.ReadAllText(filePath);
        var triggerFile = JsonSerializer.Deserialize<TriggerFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (triggerFile?.Triggers != null)
        {
            var index = triggerFile.Triggers.FindIndex(t => 
                t.On == oldTrigger.On && t.Match == oldTrigger.Match);
            
            if (index >= 0)
            {
                triggerFile.Triggers[index] = newTrigger;
                var outputJson = JsonSerializer.Serialize(triggerFile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, outputJson);
                _ = _scriptManager.LoadScriptAsync(filePath);
            }
        }
    }

    private void RemoveTriggerFromFile(string filePath, TriggerDefinition trigger)
    {
        var json = File.ReadAllText(filePath);
        var triggerFile = JsonSerializer.Deserialize<TriggerFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (triggerFile?.Triggers != null)
        {
            triggerFile.Triggers.RemoveAll(t => 
                t.On == trigger.On && t.Match == trigger.Match);
            
            var outputJson = JsonSerializer.Serialize(triggerFile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, outputJson);
            _ = _scriptManager.LoadScriptAsync(filePath);
        }
    }

    #endregion

    #region Quick Actions

    private List<QuickActionTemplate> CreateQuickActionTemplates()
    {
        return new List<QuickActionTemplate>
        {
            new()
            {
                Name = "Auto-Away",
                Description = "Automatically set away after inactivity",
                Icon = "🌙",
                Category = "Status",
                CreateAction = CreateAutoAwayScript
            },
            new()
            {
                Name = "Highlight Logger",
                Description = "Log all mentions to a file",
                Icon = "📝",
                Category = "Logging",
                CreateAction = CreateHighlightLoggerScript
            },
            new()
            {
                Name = "URL Logger",
                Description = "Log all URLs shared in channels",
                Icon = "🔗",
                Category = "Logging",
                CreateAction = CreateUrlLoggerScript
            },
            new()
            {
                Name = "Auto-Rejoin",
                Description = "Automatically rejoin on kick",
                Icon = "🔄",
                Category = "Channels",
                CreateAction = CreateAutoRejoinScript
            },
            new()
            {
                Name = "Greet Bot",
                Description = "Greet users when they join",
                Icon = "👋",
                Category = "Social",
                CreateAction = CreateGreetBotScript
            },
            new()
            {
                Name = "Anti-Spam",
                Description = "Ignore rapid message senders",
                Icon = "🛡️",
                Category = "Security",
                CreateAction = CreateAntiSpamScript
            },
            new()
            {
                Name = "Nick Highlighter",
                Description = "Play sound on nick mention",
                Icon = "🔔",
                Category = "Notifications",
                CreateAction = CreateNickHighlighterTrigger
            },
            new()
            {
                Name = "Auto-Op Friends",
                Description = "Auto-op users from a list",
                Icon = "👑",
                Category = "Channels",
                CreateAction = CreateAutoOpScript
            }
        };
    }

    private void PopulateQuickActions()
    {
        QuickActionsPanel.Children.Clear();
        
        var categories = _quickActions.GroupBy(a => a.Category);
        
        foreach (var category in categories)
        {
            // Category header
            var header = new TextBlock
            {
                Text = category.Key,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("SecondaryForegroundBrush"),
                Width = 800,
                Margin = new Thickness(5, 15, 5, 5)
            };
            QuickActionsPanel.Children.Add(header);
            
            foreach (var action in category)
            {
                var card = CreateQuickActionCard(action);
                QuickActionsPanel.Children.Add(card);
            }
        }
    }

    private Border CreateQuickActionCard(QuickActionTemplate template)
    {
        var card = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SidebarBackgroundBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(5),
            Width = 200,
            Cursor = Cursors.Hand
        };
        
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
        var icon = new TextBlock
        {
            Text = template.Icon,
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(icon, 0);
        grid.Children.Add(icon);
        
        var name = new TextBlock
        {
            Text = template.Name,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetRow(name, 1);
        grid.Children.Add(name);
        
        var desc = new TextBlock
        {
            Text = template.Description,
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("SecondaryForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(desc, 2);
        grid.Children.Add(desc);
        
        card.Child = grid;
        card.Tag = template;
        card.MouseLeftButtonUp += QuickActionCard_Click;
        
        // Hover effect
        card.MouseEnter += (s, e) => card.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        card.MouseLeave += (s, e) => card.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        
        return card;
    }

    private void QuickActionCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border card && card.Tag is QuickActionTemplate template)
        {
            var result = MessageBox.Show(
                $"Create '{template.Name}' script?\n\n{template.Description}",
                Strings.ScriptManager_QuickActionsTab,
                MessageBoxButton.YesNo);
            
            if (result == MessageBoxResult.Yes)
            {
                template.CreateAction?.Invoke();
                RefreshFileTree();
                RefreshScriptList();
                StatusText.Text = $"Created: {template.Name}";
            }
        }
    }

    #region Quick Action Script Generators

    private void CreateAutoAwayScript()
    {
        var script = @"-- Auto-Away Script
-- Sets away status after 10 minutes of inactivity

local away_minutes = 10
local away_timer = nil
local is_away = false
local last_server = nil

-- Reset timer on activity
irc.on(""message"", function(e)
    last_server = e.server
    
    if away_timer then
        timer.clear(away_timer)
    end
    
    if is_away then
        irc.raw(e.server, ""AWAY"")
        is_away = false
        print(""Returned from away"")
    end
    
    away_timer = timer.timeout(function()
        if last_server then
            irc.raw(last_server, ""AWAY :Auto-away"")
            is_away = true
            print(""Set to away"")
        end
    end, away_minutes * 60 * 1000)
end)

print(""Auto-away loaded: "" .. away_minutes .. "" min timeout"")
";
        SaveQuickActionScript("auto_away.lua", script);
    }

    private void CreateHighlightLoggerScript()
    {
        var script = @"-- Highlight Logger
-- Logs all messages containing your nick to a file

irc.on(""message"", function(e)
    if e.isHighlight then
        local timestamp = os.date(""%Y-%m-%d %H:%M:%S"")
        local logLine = string.format(""[%s] %s <%s> %s"", timestamp, e.channel, e.nick, e.text)
        storage.append(""highlights.log"", logLine .. ""\n"")
        print(""Logged highlight from "" .. e.nick)
    end
end)

print(""Highlight logger loaded"")
";
        SaveQuickActionScript("highlight_logger.lua", script);
    }

    private void CreateUrlLoggerScript()
    {
        var script = @"-- URL Logger
-- Logs all URLs shared in channels

irc.on(""message"", function(e)
    local urls = {}
    for url in e.text:gmatch(""https?://[%w%.%-_/%%?=&#]+"") do
        table.insert(urls, url)
    end
    
    for _, url in ipairs(urls) do
        local timestamp = os.date(""%Y-%m-%d %H:%M:%S"")
        local logLine = string.format(""[%s] %s <%s> %s"", timestamp, e.channel, e.nick, url)
        storage.append(""urls.log"", logLine .. ""\n"")
        print(""Logged URL: "" .. url)
    end
end)

print(""URL logger loaded"")
";
        SaveQuickActionScript("url_logger.lua", script);
    }

    private void CreateAutoRejoinScript()
    {
        var script = @"-- Auto-Rejoin on Kick
-- Automatically rejoins channels when kicked

irc.on(""kick"", function(e)
    if e.kicked == irc.me(e.server) then
        print(""Kicked from "" .. e.channel .. "" by "" .. e.kicker .. "", rejoining..."")
        timer.timeout(function()
            irc.join(e.server, e.channel)
        end, 3000)
    end
end)

print(""Auto-rejoin loaded"")
";
        SaveQuickActionScript("auto_rejoin.lua", script);
    }

    private void CreateGreetBotScript()
    {
        var script = @"-- Greet Bot
-- Greets users when they join channels

local greetings = {
    ""Welcome, %s!"",
    ""Hey %s, good to see you!"",
    ""Hello %s! 👋"",
    ""%s has arrived!"",
}

irc.on(""join"", function(e)
    -- Don't greet ourselves
    if e.nick == irc.me(e.server) then return end
    
    local greeting = greetings[math.random(#greetings)]
    local message = string.format(greeting, e.nick)
    
    timer.timeout(function()
        irc.say(e.server, e.channel, message)
    end, 1000)
end)

print(""Greet bot loaded"")
";
        SaveQuickActionScript("greet_bot.lua", script);
    }

    private void CreateAntiSpamScript()
    {
        var script = @"-- Anti-Spam
-- Temporarily ignores users who send too many messages

local message_counts = {}
local max_messages = 5
local time_window = 3000 -- 3 seconds

irc.on(""message"", function(e)
    local key = e.nick .. ""@"" .. e.server
    
    if not message_counts[key] then
        message_counts[key] = { count = 0, time = os.time() * 1000 }
    end
    
    local data = message_counts[key]
    local now = os.time() * 1000
    
    if now - data.time > time_window then
        data.count = 1
        data.time = now
    else
        data.count = data.count + 1
    end
    
    if data.count > max_messages then
        print(""Ignoring spammer: "" .. e.nick)
        irc.ignore(e.nick)
        
        -- Remove ignore after 5 minutes
        timer.setTimeout(300000, function()
            irc.unignore(e.nick)
            print(""Unignored: "" .. e.nick)
        end)
        
        message_counts[key] = nil
    end
end)

print(""Anti-spam loaded (max "" .. max_messages .. "" msgs in "" .. (time_window/1000) .. ""s)"")
";
        SaveQuickActionScript("anti_spam.lua", script);
    }

    private void CreateNickHighlighterTrigger()
    {
        var trigger = new TriggerFile
        {
            Triggers = new List<TriggerDefinition>
            {
                new()
                {
                    On = "message",
                    MatchType = "highlight",
                    Action = "notify",
                    Message = "You were mentioned by {nick} in {channel}"
                }
            }
        };
        
        var json = JsonSerializer.Serialize(trigger, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(_scriptManager.Context.ScriptsDirectory, "nick_highlighter.triggers.json");
        File.WriteAllText(path, json);
        _ = _scriptManager.LoadScriptAsync(path);
    }

    private void CreateAutoOpScript()
    {
        var script = @"-- Auto-Op Friends
-- Automatically ops users from a friend list

-- Add your friends' nicks here
local friends = {
    ""friend1"",
    ""friend2"",
    ""friend3"",
}

local function isFriend(nick)
    for _, friend in ipairs(friends) do
        if nick:lower() == friend:lower() then
            return true
        end
    end
    return false
end

irc.on(""join"", function(e)
    if isFriend(e.nick) then
        -- Give op to friend after a short delay
        timer.timeout(function()
            irc.mode(e.server, e.channel, ""+o "" .. e.nick)
            print(""Auto-opped friend: "" .. e.nick)
        end, 500)
    end
end)

print(""Auto-op loaded with "" .. #friends .. "" friends"")
";
        SaveQuickActionScript("auto_op.lua", script);
    }

    private void SaveQuickActionScript(string filename, string content)
    {
        var path = Path.Combine(_scriptManager.Context.ScriptsDirectory, filename);
        File.WriteAllText(path, content);
        _ = _scriptManager.LoadScriptAsync(path);
        AppendOutput($"Created: {filename}");
    }

    #endregion

    #endregion

    #region Global Actions

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _scriptManager.Context.ScriptsDirectory;
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        
        System.Diagnostics.Process.Start("explorer.exe", path);
    }

    private async void ReloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        await _scriptManager.LoadAllScriptsAsync();
        RefreshScriptList();
        RefreshTriggerList();
        UpdateLoadedCount();
        StatusText.Text = Strings.ScriptManager_AllReloaded;
        AppendOutput("All scripts reloaded");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                Strings.ScriptManager_UnsavedChanges,
                Strings.ScriptManager_Title,
                MessageBoxButton.YesNoCancel);
            
            if (result == MessageBoxResult.Yes)
                SaveCurrentFile();
            else if (result == MessageBoxResult.Cancel)
                return;
        }
        
        Close();
    }

    #endregion

    #region Event Handlers

    private void OnScriptLoaded(object? sender, ScriptLoadedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshScriptList();
            UpdateLoadedCount();
            AppendOutput($"Loaded: {e.ScriptName}");
        });
    }

    private void OnScriptUnloaded(object? sender, ScriptUnloadedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshScriptList();
            UpdateLoadedCount();
        });
    }

    private void OnScriptOutput(object? sender, ScriptOutputEventArgs e)
    {
        Dispatcher.Invoke(() => AppendOutput($"[{e.Source}] {e.Message}"));
    }

    private void OnScriptError(object? sender, ScriptErrorEventArgs e)
    {
        Dispatcher.Invoke(() => AppendOutput($"[ERROR: {e.Source}] {e.Message}"));
    }

    private void UpdateLoadedCount()
    {
        var count = _scriptManager.GetLoadedScripts().Count();
        LoadedCountText.Text = $"{count} {(count == 1 ? "script" : "scripts")} loaded";
    }

    private void AppendOutput(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        if (OutputText.Text.Length > 0)
        {
            OutputText.AppendText(Environment.NewLine);
        }
        OutputText.AppendText($"[{timestamp}] {message}");
        OutputText.ScrollToEnd();
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _scriptManager.ScriptLoaded -= OnScriptLoaded;
        _scriptManager.ScriptUnloaded -= OnScriptUnloaded;
        _scriptManager.ScriptOutput -= OnScriptOutput;
        _scriptManager.ScriptError -= OnScriptError;
        base.OnClosed(e);
    }
}

#region Display Models

/// <summary>
/// Display item for scripts in the list view.
/// </summary>
public class ScriptDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; }
    public bool IsEnabled { get; set; }
    public string TypeIcon { get; set; } = "📄";
}

/// <summary>
/// Display item for triggers in the list view.
/// </summary>
public class TriggerDisplayItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ActionSummary { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public TriggerDefinition TriggerData { get; set; } = new();
}

/// <summary>
/// Template for quick action cards.
/// </summary>
public class QuickActionTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📄";
    public string Category { get; set; } = string.Empty;
    public Action? CreateAction { get; set; }
}

// Note: TriggerFile is defined in Munin.Core.Scripting.Triggers

#endregion

#region String Extensions

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

#endregion
