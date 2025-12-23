using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Munin.UI.Resources;
using Munin.UI.Services;

namespace Munin.UI.Localization;

/// <summary>
/// Markup extension for localizing strings in XAML.
/// Usage: Text="{loc:Localize Key=MyResourceKey}"
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocalizeExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the default value if the key is not found.
    /// </summary>
    public string? Default { get; set; }
    
    /// <summary>
    /// Initializes a new instance of the LocalizeExtension.
    /// </summary>
    public LocalizeExtension()
    {
    }
    
    /// <summary>
    /// Initializes a new instance of the LocalizeExtension with a key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    public LocalizeExtension(string key)
    {
        Key = key;
    }
    
    /// <summary>
    /// Provides the localized value.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <returns>The localized string.</returns>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return Default ?? string.Empty;
        
        // Get the localized string
        var value = Strings.ResourceManager.GetString(Key, LocalizationService.Instance.CurrentCulture);
        
        return value ?? Default ?? $"[{Key}]";
    }
}

/// <summary>
/// Binding extension that updates when language changes.
/// Usage: Text="{loc:LocalizeBinding Key=MyResourceKey}"
/// </summary>
[MarkupExtensionReturnType(typeof(BindingExpression))]
public class LocalizeBindingExtension : MarkupExtension
{
    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Initializes a new instance of the LocalizeBindingExtension.
    /// </summary>
    public LocalizeBindingExtension()
    {
    }
    
    /// <summary>
    /// Initializes a new instance of the LocalizeBindingExtension with a key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    public LocalizeBindingExtension(string key)
    {
        Key = key;
    }
    
    /// <summary>
    /// Provides a binding that updates when language changes.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <returns>A binding expression.</returns>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding
        {
            Source = new LocalizedString(Key),
            Path = new PropertyPath(nameof(LocalizedString.Value)),
            Mode = BindingMode.OneWay
        };
        
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Helper class that provides a localized string value and updates when language changes.
/// </summary>
public class LocalizedString : DependencyObject
{
    private readonly string _key;
    
    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(LocalizedString), new PropertyMetadata(string.Empty));
    
    /// <summary>
    /// Gets the localized value.
    /// </summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        private set => SetValue(ValueProperty, value);
    }
    
    /// <summary>
    /// Initializes a new instance of the LocalizedString.
    /// </summary>
    /// <param name="key">The resource key.</param>
    public LocalizedString(string key)
    {
        _key = key;
        UpdateValue();
        
        // Subscribe to language changes
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateValue();
    }
    
    private void UpdateValue()
    {
        Value = Strings.ResourceManager.GetString(_key, LocalizationService.Instance.CurrentCulture) ?? $"[{_key}]";
    }
}
