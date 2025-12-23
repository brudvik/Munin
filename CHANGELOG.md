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

### Changed
- PrivacyService now loads mappings asynchronously during startup
- LoggingService initialization is now async to prevent UI deadlock
- History loading now uses configurable line count from settings

### Fixed
- Fixed UI thread blocking when loading privacy mappings
- Fixed potential deadlock during application startup with encrypted storage

### Security
- AES-256-GCM authenticated encryption
- PBKDF2-SHA256 with 150,000 iterations for key derivation
- Unique salt (32 bytes) per installation
- Unique nonce (12 bytes) per encryption operation
- Memory wiping on application exit
- Brute-force protection with lockout

## [1.0.0] - TBD

- First stable release
