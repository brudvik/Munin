namespace Munin.Core.Services;

/// <summary>
/// Manages portable mode detection and data path resolution.
/// </summary>
/// <remarks>
/// <para>Portable mode is activated when a file named "portable.txt" exists 
/// in the same directory as the executable.</para>
/// <para>In portable mode, all data is stored relative to the executable,
/// making the application suitable for running from USB drives.</para>
/// </remarks>
public static class PortableMode
{
    private static bool? _isPortable;
    private static string? _basePath;
    private static string? _exeDirectory;
    
    /// <summary>
    /// Gets whether the application is running in portable mode.
    /// </summary>
    public static bool IsPortable
    {
        get
        {
            _isPortable ??= DetectPortableMode();
            return _isPortable.Value;
        }
    }
    
    /// <summary>
    /// Gets the base path for application data.
    /// In portable mode: [exe directory]\data
    /// In normal mode: %APPDATA%\IrcClient
    /// </summary>
    public static string BasePath
    {
        get
        {
            _basePath ??= DetermineBasePath();
            return _basePath;
        }
    }
    
    /// <summary>
    /// Gets the log directory path.
    /// </summary>
    public static string LogPath => Path.Combine(BasePath, "logs");
    
    /// <summary>
    /// Gets the directory where the executable is located.
    /// </summary>
    public static string ExeDirectory
    {
        get
        {
            if (_exeDirectory == null)
            {
                var exePath = Environment.ProcessPath ?? 
                              System.Reflection.Assembly.GetExecutingAssembly().Location;
                _exeDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
            }
            return _exeDirectory;
        }
    }
    
    /// <summary>
    /// Gets the path to the portable mode marker file.
    /// </summary>
    public static string PortableMarkerPath => Path.Combine(ExeDirectory, "portable.txt");
    
    /// <summary>
    /// Enables portable mode by creating the marker file.
    /// </summary>
    /// <returns>True if portable mode was enabled successfully.</returns>
    public static bool EnablePortableMode()
    {
        try
        {
            var portablePath = Path.Combine(ExeDirectory, "data");
            Directory.CreateDirectory(portablePath);
            
            File.WriteAllText(PortableMarkerPath, 
                $"# Munin Portable Mode\r\n" +
                $"# Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"# \r\n" +
                $"# Delete this file to switch back to normal mode.\r\n" +
                $"# Data will be stored in: {portablePath}\r\n");
            
            // Reset cached values
            _isPortable = true;
            _basePath = portablePath;
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Disables portable mode by removing the marker file.
    /// </summary>
    /// <returns>True if portable mode was disabled successfully.</returns>
    public static bool DisablePortableMode()
    {
        try
        {
            if (File.Exists(PortableMarkerPath))
            {
                File.Delete(PortableMarkerPath);
            }
            
            // Reset cached values
            _isPortable = false;
            _basePath = null;
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Detects if portable mode is enabled.
    /// </summary>
    private static bool DetectPortableMode()
    {
        return File.Exists(PortableMarkerPath);
    }
    
    /// <summary>
    /// Determines the base path based on portable mode.
    /// </summary>
    private static string DetermineBasePath()
    {
        if (IsPortable)
        {
            var portablePath = Path.Combine(ExeDirectory, "data");
            Directory.CreateDirectory(portablePath);
            return portablePath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var normalPath = Path.Combine(appData, "Munin");
            Directory.CreateDirectory(normalPath);
            return normalPath;
        }
    }
    
    /// <summary>
    /// Resets the cached values (useful for testing or after mode change).
    /// </summary>
    public static void Reset()
    {
        _isPortable = null;
        _basePath = null;
    }
}
