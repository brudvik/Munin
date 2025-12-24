# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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

### Changed
- **Major UI refresh**: Professional polished dark theme with modern styling
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
