# IRC Client for Windows

A modern, secure IRC client built with .NET 8 and WPF.

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
- **Dark theme** - Easy on the eyes
- **Tab completion** - For nicknames and commands
- **Command history** - Arrow up/down navigation
- **Highlight words** - Custom alert words
- **Ignore list** - Hide messages from users
- **Sound and flash alerts** - Configurable notifications

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
git clone https://github.com/your-username/IrcClient.git
cd IrcClient
dotnet build
dotnet run --project IrcClient.UI
```

### Publish as Standalone

```bash
dotnet publish IrcClient.UI -c Release -r win-x64 --self-contained
```

## 📁 Project Structure

```
IrcClient/
├── IrcClient.Core/           # Core logic and services
│   ├── Models/               # Data models
│   └── Services/             # Business logic
│       ├── IrcConnection.cs      # IRC protocol
│       ├── EncryptionService.cs  # AES-256-GCM
│       ├── SecureStorageService.cs
│       ├── LoggingService.cs
│       └── ...
└── IrcClient.UI/             # WPF interface
    ├── Views/                # XAML windows
    ├── ViewModels/           # MVVM ViewModels
    ├── Controls/             # Custom controls
    ├── Themes/               # Styling and themes
    └── Converters/           # XAML converters
```

## ⚙️ Configuration

### Normal Mode
Data is stored in: `%APPDATA%\IrcClient\`

### Portable Mode
1. Create an empty file `portable.txt` next to `IrcClient.exe`
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
