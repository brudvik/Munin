# Copilot Instructions for Munin IRC Client

## Code Standards

### Comments and Documentation
- **All code must be properly commented** - methods, classes, and complex logic should have XML documentation comments
- Use `<summary>`, `<param>`, `<returns>`, and `<remarks>` tags for public APIs
- Complex algorithms or non-obvious code should have inline comments explaining the reasoning
- Norwegian comments are acceptable for UI strings and user-facing text

### Code Style
- Follow C# naming conventions (PascalCase for public members, camelCase for private fields with underscore prefix)
- Use meaningful variable and method names
- Keep methods focused and small (single responsibility)
- Use async/await properly - avoid `.Result` or `.Wait()` on the UI thread

## Project Documentation

### CHANGELOG.md
- **Must be updated with every feature or bugfix**
- Follow [Keep a Changelog](https://keepachangelog.com/) format
- Group changes under: Added, Changed, Deprecated, Removed, Fixed, Security
- Include date for each version
- Reference issue numbers if applicable

### README.md
- Keep the feature list up to date
- Document new configuration options
- Update screenshots if UI changes significantly
- Maintain accurate build/run instructions

## Architecture Guidelines

### Privacy & Security
- Never log sensitive information (passwords, tokens, private messages in debug logs)
- Use `PrivacyService` for anonymizing filenames when privacy mode is enabled
- Use `SecureStorageService` for encrypted data storage
- All encryption operations should use `EncryptionService`

### WPF/MVVM Patterns
- ViewModels should inherit from `ViewModelBase`
- Use `ObservableCollection<T>` for lists bound to UI
- Use `[ObservableProperty]` and `[RelayCommand]` attributes from CommunityToolkit.Mvvm
- Keep UI logic in ViewModels, not code-behind

### Localization
- **All user-facing strings MUST be localized** - never hardcode text in XAML or code-behind
- Add English strings to `Resources/Strings.resx`
- Add Norwegian translations to `Strings.nb-NO.resx`
- Use `LocalizeExtension` in XAML: `{loc:Localize Key=StringKeyName}`
- For ToolTips: `ToolTip="{loc:Localize Key=MainWindow_LeaveChannel}"`
- For Content/Text: `Content="{loc:Localize Key=ButtonText}"`
- String keys should follow pattern: `ViewName_ElementDescription` (e.g., `MainWindow_LeaveChannel`)
- Always add both English and Norwegian translations when adding new strings

## Testing Checklist
Before committing changes:
1. Build succeeds without errors
2. Application starts and connects to IRC servers
3. Encryption/decryption works correctly
4. Privacy mode anonymizes filenames properly
5. Localization works for both English and Norwegian

## File Structure
```
Munin/
├── Munin.Core/           # Core IRC logic, services, models
│   ├── Events/           # IRC event definitions
│   ├── Models/           # Data models (IrcMessage, IrcUser, etc.)
│   └── Services/         # Business logic services
├── Munin.UI/             # WPF application
│   ├── Controls/         # Custom WPF controls
│   ├── Converters/       # Value converters for XAML
│   ├── Resources/        # Localization files, assets
│   ├── Themes/           # XAML styles and themes
│   ├── ViewModels/       # MVVM ViewModels
│   └── Views/            # XAML views and windows
├── Munin.Relay/          # VPN relay companion tool
├── Munin.Agent/          # Standalone IRC bot/agent
└── docs/                 # Additional documentation
```
