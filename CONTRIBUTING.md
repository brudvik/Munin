# Contributing to IRC Client

Thank you for your interest in contributing! Here's how you can help.

## Getting Started

1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/your-username/IrcClient.git
   ```
3. Create a feature branch:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Setup

### Prerequisites
- Windows 10/11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 or VS Code with C# extension

### Building
```bash
dotnet restore
dotnet build
```

### Running
```bash
dotnet run --project IrcClient.UI
```

### Running Tests
```bash
dotnet test
```

## Code Style

- Follow the `.editorconfig` settings
- Use file-scoped namespaces
- Add XML documentation comments to public APIs
- Keep methods focused and under 50 lines when possible

## Commit Messages

Use clear, descriptive commit messages:
- `feat: Add SCRAM-SHA-256 authentication`
- `fix: Resolve connection timeout issue`
- `docs: Update README with proxy instructions`
- `refactor: Simplify encryption key derivation`

## Pull Request Process

1. Update documentation if needed
2. Add tests for new features
3. Ensure all tests pass
4. Update CHANGELOG.md
5. Submit PR with clear description

## Reporting Issues

When reporting bugs, please include:
- Windows version
- .NET version (`dotnet --version`)
- Steps to reproduce
- Expected vs actual behavior
- Relevant log excerpts (remove sensitive info)

## Security Issues

For security vulnerabilities, please **do not** open a public issue.
Instead, contact the maintainers directly.

## Code of Conduct

- Be respectful and inclusive
- Focus on constructive feedback
- Help others learn and grow

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
