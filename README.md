# Munin

**Munin** is a modern, secure IRC client for Windows, built with .NET 8 and WPF.

*Named after Odin's raven who flies across the world to bring back news and information.*

![Munin IRC Client](https://raw.githubusercontent.com/brudvik/Munin/main/munin.png)

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

### Connection & Networking
- **Multi-server support** - Connect to multiple IRC servers simultaneously
- **SSL/TLS** - Secure connections with optional certificate validation
- **SASL authentication** - PLAIN and SCRAM-SHA-256 support
- **Client certificates** - Authentication with PFX/P12 certificates
- **Proxy support** - SOCKS4, SOCKS5, and HTTP proxy

### Security & Privacy
- **AES-256-GCM encryption** - All local data can be encrypted
- **PBKDF2 key derivation** - 150,000 iterations
- **Anonymous filenames** - Hides server/channel names in log files
- **Auto-lock** - Lock after configurable inactivity period
- **Secure deletion** - Overwrite files before deletion
- **Security log** - Track unlock attempts

### User Interface
- **Modern dark theme** - Professional polished design with Windows 11-style window chrome
- **Custom title bar** - Borderless window with rounded corners and integrated controls
- **Multi-language** - English and Norwegian (auto-detects system language)
- **Tab completion** - For nicknames and commands
- **Command history** - Arrow up/down navigation
- **Highlight words** - Custom alert words with red unread badges
- **Ignore list** - Hide messages from users
- **Sound and flash alerts** - Configurable notifications
- **Connection spinner** - Visual feedback during server connections
- **History loading** - Configurable message history on channel join (10-1000 lines)
- **History timestamps** - Shows date range when loading previous messages
- **Welcome screen** - Feature highlights when no servers are configured
- **Empty states** - Friendly icons for empty channels and no selection
- **Channel statistics** - Live message count, user count, and session duration in status bar
- **Server latency** - Real-time ping display in status bar
- **Topic bar** - Expandable channel topic display
- **Enhanced user list** - Initials-based avatars, status indicators, collapsible groups
- **Text emoticon picker** - Quick access to IRC-style emoticons
- **Character counter** - Shows message length (512 char limit)
- **Search messages** - Ctrl+F to search with animated slide-down bar
- **Smooth animations** - Fade-in effects for messages and UI elements

### Advanced
- **Auto-perform** - Run commands on connection
- **Aliases** - Custom command shortcuts
- **Flood protection** - Token bucket algorithm
- **Portable mode** - Run from USB drive
- **Encrypted logs** - Secure log storage

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build from Source

```bash
git clone https://github.com/your-username/Munin.git
cd Munin
dotnet build
dotnet run --project Munin.UI
```

### Publish as Standalone

```bash
dotnet publish Munin.UI -c Release -r win-x64 --self-contained
```

## 📁 Project Structure

```
Munin/
├── Munin.Core/               # Core logic and services
│   ├── Models/               # Data models
│   └── Services/             # Business logic
│       ├── IrcConnection.cs      # IRC protocol
│       ├── EncryptionService.cs  # AES-256-GCM
│       ├── SecureStorageService.cs
│       ├── LoggingService.cs
│       └── ...
└── Munin.UI/                 # WPF interface
    ├── Views/                # XAML windows
    ├── ViewModels/           # MVVM ViewModels
    ├── Controls/             # Custom controls
    ├── Themes/               # Styling and themes
    └── Converters/           # XAML converters
```

## ⚙️ Configuration

### Settings

| Setting | Description | Default |
|---------|-------------|---------|
| History lines | Number of history messages to load | 100 |
| Auto-lock minutes | Minutes of inactivity before lock | 5 |
| Auto-delete logs | Days to keep log files | 30 |
| Secure delete | Overwrite files before deletion | Off |

### Normal Mode
Data is stored in: `%APPDATA%\Munin\`

### Portable Mode
1. Create an empty file `portable.txt` next to `Munin.exe`
2. Data will now be stored in `[exe-folder]\data\`

## 🔐 Security

### Encryption
- **Algorithm:** AES-256-GCM (authenticated encryption)
- **Key derivation:** PBKDF2-SHA256, 150,000 iterations
- **Salt:** 32 bytes, unique per installation
- **Nonce:** 12 bytes, unique per encryption operation

### Privacy
- Filenames are anonymized with HMAC-SHA256 when encryption is enabled
- No telemetry or network requests outside of IRC

## 📝 IRC Commands

| Command | Description |
|---------|-------------|
| `/join #channel` | Join a channel |
| `/part [message]` | Leave current channel |
| `/msg nick message` | Send private message |
| `/me action` | Send action message |
| `/nick newnick` | Change nickname |
| `/quit [message]` | Disconnect from server |
| `/topic [text]` | View/set channel topic |
| `/whois nick` | View user info |
| `/ignore nick` | Ignore a user |
| `/alias name command` | Create alias |

## 🛠️ Technology

- **.NET 8.0** - Platform
- **WPF** - UI framework
- **Serilog** - Logging
- **CommunityToolkit.Mvvm** - MVVM framework

## 📄 License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

## 🤝 Contributing

Contributions are welcome!

1. Fork the project
2. Create a feature branch (`git checkout -b feature/MyNewFeature`)
3. Commit your changes (`git commit -m 'Add new feature'`)
4. Push to the branch (`git push origin feature/MyNewFeature`)
5. Open a Pull Request

## 📧 Contact

Create an issue for questions or bug reports.
