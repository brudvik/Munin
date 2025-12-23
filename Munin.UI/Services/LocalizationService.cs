using System.ComponentModel;
using System.Globalization;
using Munin.UI.Resources;

namespace Munin.UI.Services;

/// <summary>
/// Provides localization services for the application.
/// Manages language switching and provides access to localized strings.
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    private static readonly object _lock = new();
    
    private CultureInfo _currentCulture;
    
    /// <summary>
    /// Gets the singleton instance of the LocalizationService.
    /// </summary>
    public static LocalizationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LocalizationService();
                }
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// Event raised when a property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;
    
    /// <summary>
    /// Event raised when the language changes.
    /// </summary>
    public event EventHandler? LanguageChanged;
    
    /// <summary>
    /// Gets the available languages.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> AvailableLanguages { get; } = new List<LanguageInfo>
    {
        new("en", "English", "🇬🇧"),
        new("nb-NO", "Norsk (Bokmål)", "🇳🇴"),
    };
    
    /// <summary>
    /// Gets or sets the current culture/language.
    /// </summary>
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Name != value.Name)
            {
                _currentCulture = value;
                
                // Update the resource manager's culture
                Strings.Culture = value;
                
                // Update thread culture
                Thread.CurrentThread.CurrentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                
                OnPropertyChanged(nameof(CurrentCulture));
                OnPropertyChanged(nameof(CurrentLanguageCode));
                OnPropertyChanged(string.Empty); // Notify all properties changed
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    
    /// <summary>
    /// Gets the current language code (e.g., "en", "nb-NO").
    /// </summary>
    public string CurrentLanguageCode => _currentCulture.Name == "nb-NO" ? "nb-NO" : "en";
    
    private LocalizationService()
    {
        // Default to system culture or English
        var systemCulture = CultureInfo.CurrentUICulture;
        
        // Check if we support this culture
        if (systemCulture.Name.StartsWith("nb") || systemCulture.Name.StartsWith("no"))
        {
            _currentCulture = new CultureInfo("nb-NO");
        }
        else
        {
            _currentCulture = new CultureInfo("en");
        }
        
        Strings.Culture = _currentCulture;
    }
    
    /// <summary>
    /// Sets the language by language code.
    /// </summary>
    /// <param name="languageCode">The language code (e.g., "en", "nb-NO").</param>
    public void SetLanguage(string languageCode)
    {
        CurrentCulture = new CultureInfo(languageCode);
    }
    
    /// <summary>
    /// Gets a localized string by key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string, or the key if not found.</returns>
    public string GetString(string key)
    {
        return Strings.ResourceManager.GetString(key, _currentCulture) ?? key;
    }
    
    /// <summary>
    /// Gets a localized string by key with format arguments.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string.</returns>
    public string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        return string.Format(format, args);
    }
    
    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">Name of the property that changed.</param>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    // Convenience properties for common strings - these update when language changes
    public string AppName => Strings.AppName;
    public string OK => Strings.OK;
    public string Cancel => Strings.Cancel;
    public string Save => Strings.Save;
    public string Close => Strings.Close;
    public string Yes => Strings.Yes;
    public string No => Strings.No;
    public string Error => Strings.Error;
    public string Warning => Strings.Warning;
    public string Confirm => Strings.Confirm;
    public string Connect => Strings.Connect;
    public string Disconnect => Strings.Disconnect;
    public string Settings => Strings.Settings;
    public string Language => Strings.Language;
}

/// <summary>
/// Represents information about a supported language.
/// </summary>
/// <param name="Code">The language code (e.g., "en", "nb-NO").</param>
/// <param name="Name">The display name of the language.</param>
/// <param name="Flag">An emoji flag representing the language.</param>
public record LanguageInfo(string Code, string Name, string Flag)
{
    /// <summary>
    /// Gets the display string for the language.
    /// </summary>
    public string DisplayName => $"{Flag} {Name}";
}
