using System.Windows;
using System.Windows.Input;

namespace Munin.UI.Views;

/// <summary>
/// Base class for chromeless (borderless) windows with custom title bar.
/// Provides window dragging, minimize, maximize, and close functionality.
/// </summary>
public class ChromelessWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the ChromelessWindow class.
    /// </summary>
    public ChromelessWindow()
    {
        // Set chromeless window properties
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = System.Windows.Media.Brushes.Transparent;
        
        // Update maximize icon when window state changes
        StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Handles title bar mouse down for window dragging.
    /// </summary>
    protected void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize
            ToggleMaximize();
        }
        else
        {
            // Start drag
            if (WindowState == WindowState.Maximized)
            {
                // Restore before dragging when maximized
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                Left = point.X - Width / 2;
                Top = point.Y - 20;
            }
            DragMove();
        }
    }

    /// <summary>
    /// Minimizes the window.
    /// </summary>
    protected void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Toggles between maximized and normal window state.
    /// </summary>
    protected void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    protected void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Toggles the window between maximized and normal state.
    /// </summary>
    protected void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }

    /// <summary>
    /// Updates UI when window state changes.
    /// </summary>
    protected virtual void OnStateChanged(object? sender, EventArgs e)
    {
        // Can be overridden in derived classes to update maximize icon
    }
}
