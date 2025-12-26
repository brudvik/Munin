# Munin

**Munin** is a modern, secure IRC client for Windows, built with .NET 8 and WPF.

*Named after Odin's raven who flies across the world to bring back news and information.*

![Munin IRC Client](https://raw.githubusercontent.com/brudvik/Munin/refs/heads/main/munin.png)

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
- **Discord-style layout** - Vertical server rail, channel sidebar, and expandable chat area
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
- **Smooth animations** - Fade-in effects for messages and UI elements

### Chat Experience
- **Smart auto-scroll** - Pauses when you scroll up, resumes when you reach bottom
- **Jump to Latest** - Button with unread message count when scrolled up
- **Message grouping** - Consecutive messages from same user show compact headers
- **Zebra striping** - Alternating row backgrounds for better readability
- **Message search** - Ctrl+F to search with visual highlighting of matches
- **Right-click menu** - Copy message, copy nickname, whois, open query, ignore user
- **Double-click to copy** - Quickly copy any message to clipboard
- **Keyboard shortcuts** - Ctrl+End for latest, Page Up/Down for scrolling

### Advanced
- **Auto-perform** - Run commands on connection
- **Aliases** - Custom command shortcuts
- **Flood protection** - Token bucket algorithm
- **Portable mode** - Run from USB drive
- **Encrypted logs** - Secure log storage

### Scripting & Automation
- **Lua scripting** - Powerful Lua scripts with full IRC API (MoonSharp engine)
- **JSON triggers** - Simple pattern-based automation without coding
- **C# plugins** - Compiled plugins for maximum power and extensibility
- **Script storage** - Persistent storage API for scripts
- **Timer support** - Schedule one-shot and repeating actions
- **Custom commands** - Register your own /commands
- **Full event access** - Messages, joins, parts, kicks, mode changes, and more
- **Script console** - Test and debug scripts with live output
- **Auto-load** - Scripts in `%APPDATA%\Munin\scripts\` load automatically on startup

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
│   ├── Scripting/            # Scripting system
│   │   ├── Lua/              # MoonSharp Lua engine
│   │   ├── Triggers/         # JSON trigger automation
│   │   └── Plugins/          # C# plugin loader
│   └── Services/             # Business logic
│       ├── IrcConnection.cs      # IRC protocol
│       ├── EncryptionService.cs  # AES-256-GCM
│       ├── SecureStorageService.cs
│       ├── LoggingService.cs
│       └── ...
├── Munin.UI/                 # WPF interface
│   ├── Views/                # XAML windows
│   ├── ViewModels/           # MVVM ViewModels
│   ├── Controls/             # Custom controls
│   ├── Themes/               # Styling and themes
│   └── Converters/           # XAML converters
├── scripts/                  # User scripts
│   └── examples/             # Example scripts
└── docs/                     # Documentation
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

## 🔧 Scripting System

Munin supports three scripting methods for automation and extensibility.

### Script Location
Scripts are loaded automatically from: `%APPDATA%\Munin\scripts\`

### Lua Scripts (`.lua`)

Full-featured scripting with the MoonSharp Lua interpreter.

```lua
-- greet.lua - Auto-greet on join
irc.on("join", function(e)
    if e.nick ~= irc.me(e.server) then
        irc.say(e.server, e.channel, "Welcome, " .. e.nick .. "!")
    end
end)

-- Respond to messages
irc.on("message", function(e)
    if e.text:match("^!hello") then
        e:reply("Hello, " .. e.nick .. "!")
    end
end)

-- Timer example
timer.timeout(function()
    print("5 seconds have passed!")
end, 5000)
```

**Available IRC API:**
- `irc.say(server, target, message)` - Send message
- `irc.msg(server, target, message)` - Alias for say
- `irc.action(server, target, message)` - Send /me action
- `irc.notice(server, target, message)` - Send notice
- `irc.raw(server, command)` - Send raw IRC command
- `irc.join(server, channel, [key])` - Join channel
- `irc.part(server, channel, [reason])` - Leave channel
- `irc.kick(server, channel, nick, [reason])` - Kick user
- `irc.mode(server, target, modes)` - Set modes
- `irc.me(server)` - Get current nickname
- `irc.on(event, callback)` - Register event handler
- `timer.timeout(callback, ms)` - One-shot timer
- `timer.interval(callback, ms)` - Repeating timer
- `storage.get(key)` / `storage.set(key, value)` - Persistent storage
- `print(text)` - Print to script console

### JSON Triggers (`.json`)

Simple pattern-based automation without coding.

```json
{
  "name": "Auto-Response Bot",
  "triggers": [
    {
      "pattern": "^!help$",
      "response": "Available commands: !help, !info, !ping",
      "type": "message"
    },
    {
      "pattern": "^!ping$",
      "response": "Pong!",
      "type": "message"
    },
    {
      "pattern": ".*",
      "response": "Welcome to the channel!",
      "type": "join",
      "excludeSelf": true
    }
  ]
}
```

**Trigger types:** `message`, `join`, `part`, `quit`, `nick`, `topic`

### C# Plugins (`.dll`)

Compiled plugins for maximum power. Implement `IPlugin` interface:

```csharp
public class MyPlugin : IPlugin
{
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    
    public void Initialize(IPluginContext context)
    {
        context.RegisterCommand("greet", args => 
        {
            context.SendMessage(context.CurrentChannel, $"Hello {args}!");
        });
        
        context.OnMessage += (sender, e) => 
        {
            if (e.Content.Contains("hello"))
                context.SendMessage(e.Channel, $"Hi {e.Nick}!");
        };
    }
    
    public void Shutdown() { }
}
```

## 🛠️ Technology

- **.NET 8.0** - Platform
- **WPF** - UI framework
- **MoonSharp** - Lua scripting engine
- **Serilog** - Logging
- **CommunityToolkit.Mvvm** - MVVM framework

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Toggle message search |
| `Ctrl+End` | Jump to latest message |
| `Page Up/Down` | Scroll through messages |
| `Up/Down` | Navigate command history |
| `Tab` | Auto-complete nicknames/commands |
| `Escape` | Close search/dialogs |
| `Enter` | Send message |

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
