# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Ident Server (RFC 1413)**: Built-in identd server like mIRC
  - Full RFC 1413 compliant implementation
  - Configurable port (default 113)
  - Custom username or use system username
  - Configurable OS type (UNIX/WIN32/OTHER)
  - HIDDEN-USER privacy option
  - Automatic connection tracking for IRC sessions
  - Settings UI in Settings window
- **Server Groups/Folders**: Organize servers into collapsible groups for better management
  - Create, rename, and delete server groups
  - Move servers between groups via context menu
  - Collapse/expand groups to save space in server rail
  - Groups persist across sessions in configuration
- **Server context menu**: Right-click on server icons for quick actions (Connect, Disconnect, Edit, Remove)
- **Bouncer/ZNC Support**: Better handling of IRC bouncers
  - Auto-detect ZNC/bouncer via CAP `znc.in/playback` and `draft/chathistory`
  - Suppress notifications during playback buffer replay
  - Mark messages as historical (IsFromPlayback flag)
  - IsBouncer and SuppressPlaybackNotifications server settings
- **Channel Mode Editor**: Visual dialog for editing channel modes
  - Toggle simple modes (+n, +t, +s, +m, +i, +p)
  - Set/remove channel key (+k) and user limit (+l)
  - Shows current modes and only sends changes
  - Access via channel context menu "Edit channel modes..."
- **Ban List Manager**: Manage channel bans, exceptions, and invite lists
  - View all bans (+b), exceptions (+e), and invite masks (+I)
  - Add new entries with hostmask input
  - Remove entries via right-click context menu
  - Shows who set each entry and when
  - Access via channel context menu "Manage bans/exceptions..."
- **Keyboard Focus**: Automatic focus to message input after JOIN or channel switch
  - FocusMessageInputRequested event for ViewModel-to-View communication
  - Dispatcher-based focus with Input priority for reliability
- **Loading States**: Visual indicators for async operations
  - History loading spinner when switching channels
  - IsLoadingHistory property on ChannelViewModel
- **Channel Drag & Drop**: Reorder channels by dragging
  - SortOrder property for channel positioning
  - Drag threshold detection for reliable interaction
  - Order persists in configuration
- **Unread Badge Limit**: Shows "99+" instead of large numbers in unread badges
  - UnreadCountConverter for display formatting
- Initial release of Munin IRC Client for Windows
- Multi-server support with tabbed interface
- SSL/TLS encrypted connections
- SASL authentication (PLAIN and SCRAM-SHA-256)
- Client certificate authentication
- Proxy support (SOCKS4, SOCKS5, HTTP)
- AES-256-GCM encryption for local data storage
- PBKDF2 key derivation with 150,000 iterations
- Anonymous filename hashing for privacy
- Auto-lock after configurable inactivity
- Secure file deletion (1-pass overwrite)
- Security audit log for unlock attempts
- Auto-delete old logs (GDPR-friendly)
- Portable mode for USB drives
- Dark theme UI
- Tab completion for nicknames and commands
- Command history (up/down arrows)
- Custom highlight words

### Fixed
- Localized hardcoded strings in ScriptConsoleWindow and RawIrcLogWindow
- Added missing Norwegian translations for new UI elements
- Ignore list for users
- Auto-perform commands on connect
- Custom command aliases
- Flood protection with token bucket algorithm
- Sound and taskbar flash notifications
- Multi-language support (English and Norwegian)
- Automatic language detection based on system settings
- Connection status spinner with button disable during connection
- Configurable history line count (10-1000 lines)
- History timestamp separator showing date range of loaded messages
- Copilot instructions file for development guidelines
- Close button (X) on sidebar channels and DMs for easy leave/close
- Topic editor dialog with FiSH encryption support
- Topic decryption for FiSH-encrypted channel topics
- Edit topic button (✏️) in topic bar for users with edit permissions
- **Discord-style layout**: New vertical server rail with round server icons on the left side
- Server icons with auto-generated initials and color-coded backgrounds based on server name
- Connection status indicators on server icons (green=connected, yellow=connecting, gray=disconnected)
- Collapsible user list panel with toggle button in header
- User list auto-hides for server console and private messages
- Localization strings for new UI elements (MainWindow_SelectServer, MainWindow_ToggleUserList)
- **Hybrid Scripting System**: Full automation and integration capabilities
  - **Lua Scripting** (MoonSharp): Primary scripting with sandboxed execution
  - **JSON Triggers**: Simple automation without coding for common tasks
  - **C# Plugins**: Advanced users can create compiled DLL plugins
- **Quote/Reply**: Right-click context menu to quote and reply to messages
- **Message Formatting Buttons**: Bold (Ctrl+B), Italic (Ctrl+I), Underline (Ctrl+U) buttons in message input
- **Away Status Toggle**: Moon/sun icon button to easily set/clear away status with custom message
- **Notify List**: Track when friends come online/offline
  - Add users from message context menu
  - NotifyListService with ISON support
  - Online/offline notification events
- **Favorite Channels**: Star-mark channels to sort them to the top of the list
  - Visual star indicator on favorite channels
  - Right-click context menu to toggle favorite status
  - Automatic sorting with favorites first
- **Certificate Pinning**: Security feature to detect SSL certificate changes
  - Stores SHA-256 fingerprints for each server
  - Alerts when server certificate changes unexpectedly
  - Persistent storage in certificate_pins.json
- **IPv6 Support**: Native IPv6 networking with dual-stack fallback
  - Per-server IPv6 preference setting in Add/Edit Server dialogs
  - DNS A/AAAA record resolution with configurable preference
  - Automatic fallback to IPv4 when IPv6 is unavailable
  - Connection status shows current IP version
- **FiSH Encryption**: End-to-end message encryption compatible with mIRC/HexChat
  - Custom Blowfish cipher implementation (no external dependencies)
  - ECB and CBC encryption modes (+OK and *OK prefixes)
  - FiSH-compatible Base64 encoding
  - **DH1080 Key Exchange**: Automatic secure key negotiation with `/keyx` command
  - Commands: `/setkey`, `/delkey`, `/keyx`, `/showkey`
  - Visual lock icon (🔒) on encrypted messages
  - Per-channel and per-user key management
  - **Context menu integration**: Right-click on channels/DMs for encryption options
  - Input dialog for setting encryption keys

### Changed
- **Chromeless window style**: All dialog windows now use custom windowless title bar matching MainWindow
  - Updated: AddServerDialog, EditServerDialog, SettingsWindow, ChannelListWindow, ChannelStatsWindow
  - Updated: WhoisWindow, RawIrcLogWindow, SecurityLogWindow, UserProfileWindow
  - Updated: ChangePasswordDialog, EncryptionSetupDialog, UnlockDialog, TriggerBuilderDialog
  - Updated: ScriptConsoleWindow, ScriptManagerWindow, InputDialog
- **Enhanced Chat Window UX**:
  - Smart auto-scroll: Pauses when user scrolls up, resumes when scrolling to bottom
  - "Jump to Latest" button with unread message count when scrolled up
  - Message grouping: Consecutive messages from same user show compact headers (just timestamp)
  - Alternating row backgrounds (zebra striping) for better message tracking
  - Right-click context menu on messages: Copy message, Copy nickname, Whois, Open query, Ignore user
  - Double-click to copy message text to clipboard
  - Keyboard shortcuts: Ctrl+End to jump to latest, Page Up/Down for scrolling, Ctrl+F for search
  - Search highlighting: Matching messages are visually highlighted in the message list
  - Complete IRC API: send messages, join/part channels, kick/ban, mode changes
  - Event system: message, join, part, quit, nick, topic, mode, connect, disconnect, CTCP, invite
  - Timer API: setTimeout, setInterval for scheduled tasks
  - Storage API: persistent key-value storage for scripts
  - Custom command registration for plugins
- **Command Autocomplete**: Popup with all available IRC commands when typing "/" in chat input
  - Shows command name, usage syntax, and description
  - Keyboard navigation with Up/Down arrows, Tab/Enter to select, Escape to close
  - Helps users discover available commands without memorizing them
- **Script Manager Window**: Comprehensive GUI for managing scripts and automation
  - **Editor Tab**: Built-in code editor with file tree, save/run buttons, output panel
  - **Scripts Tab**: List all loaded scripts with enable/disable toggles and reload buttons
  - **Triggers Tab**: Visual trigger builder - create automation without writing code
  - **Quick Actions Tab**: One-click templates for common automation:
    - Auto-Away, Highlight Logger, URL Logger, Auto-Rejoin
    - Greet Bot, Anti-Spam, Nick Highlighter, Auto-Op Friends
- **Extended /script Command**: Full scripting control from command line
  - `/script` or `/scripts` - Open Script Manager window
  - `/script list` - List all loaded scripts in channel
  - `/script enable <name>` - Enable/load a script
  - `/script disable <name>` - Disable/unload a script
  - `/script reload [name]` - Reload one or all scripts
  - `/script console` - Open the script console for REPL
- Example scripts in `scripts/examples/` folder

### Changed
- **Major UI refresh**: Professional polished dark theme with modern styling
- **Discord-style navigation**: Server rail on far left, channel sidebar for selected server, expandable chat area
- Enhanced color palette with subtle gradients and shadows for depth
- Improved header with branded logo section and channel info pill
- Redesigned sidebar with better visual hierarchy and colored status indicators
- Upgraded control styles with focus rings, rounded corners, and smooth hover effects
- New button variants: Accent, Success, and Danger styles
- Improved scrollbar styling with hover and drag states
- Card-based layout in Settings dialog for better organization
- Enhanced Add Server dialog with grouped SASL section
- Consistent typography using Segoe UI for UI elements, monospace for IRC messages
- Better spacing and padding throughout the application
- Mention/highlight messages now have colored left border indicator
- User count badge in sidebar header
- Latency and nickname indicators in status bar with pill styling
- Full localization support for all UI windows and dialogs
- Channel List window with icon header and count badge
- Raw IRC Log window with styled header and improved row styling
- Security Log window with professional card layout
- User Profile window with avatar ring, status badge, and card-based info section
- Encryption Setup dialog with card sections and warning styling using theme colors
- Change Password dialog with consistent styling and header icon
- Unlock dialog with centered icon and professional layout
- Channel Stats window with icon header and badge-styled message counts
- Edit Server dialog with card-based SASL section and icon header
- All dialogs now use consistent font (Segoe UI for UI, monospace for data)
- **Welcome screen** with feature highlights when no servers are configured
- **Empty state displays** with friendly icons for no-channel-selected and empty channels
- **Subtle background gradients** in chat area for visual depth
- **Ambient sidebar shadow** for panel separation and depth
- **Enhanced server icons** with connection status indicator dots
- **Fade-in animations** for messages and UI transitions for polished feel
- **Custom Window Chrome** with borderless design, rounded corners (Windows 11-style), and custom title bar with minimize/maximize/close buttons
- **Enhanced user list** with initials-based avatars, online/away status indicators, and collapsible user groups (Operators, Voiced, Users)
- **Channel topic bar** that can be expanded/collapsed to show full topic text
- **Improved message input** with character counter (512 limit), text emoticon picker popup
- **Unread message badges** on channels with blue for normal unreads, red for mentions
- **Search button** in header with animated search bar slide-down
- **Channel statistics in status bar** showing message count, user count, and time in channel
- **Server latency display** in status bar showing ping time in milliseconds

### Fixed
- User's own messages are now properly logged to chat history
- Fixed latency indicator not showing (was using wrong converter for int values)

### Changed
- PrivacyService now loads mappings asynchronously during startup
- LoggingService initialization is now async to prevent UI deadlock
- History loading now uses configurable line count from settings
- All UI strings now use localization system for multi-language support

### Fixed
- Fixed UI thread blocking when loading privacy mappings
- Fixed potential deadlock during application startup with encrypted storage
- Fixed encrypted log buffers not flushing on application exit (storage was locked before flush)
- Fixed active channel not highlighted in sidebar
- Fixed user's own messages not being logged (only received messages were logged)
- Fixed channels not being removed from sidebar when leaving with /part
- Fixed hardcoded strings in Settings, Add Server, Change Password, Security Log, and Raw Log windows

### Security
- AES-256-GCM authenticated encryption
- PBKDF2-SHA256 with 150,000 iterations for key derivation
- Unique salt (32 bytes) per installation
- Unique nonce (12 bytes) per encryption operation
- Memory wiping on application exit
- Brute-force protection with lockout

## [1.0.0] - TBD

- First stable release
