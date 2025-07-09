# Contributing to Requestr

Thank you for your interest in contributing to Requestr! This document provides guidelines and instructions for contributing to the project.

## 🤝 How to Contribute

### Reporting Bugs

1. **Search existing issues** to avoid duplicates
2. **Create a new issue** with:
   - Clear, descriptive title
   - Steps to reproduce
   - Expected vs actual behavior
   - Environment details (OS, .NET version, etc.)
   - Screenshots if applicable

### Suggesting Features

1. **Check existing issues** for similar requests
2. **Create a feature request** with:
   - Clear description of the feature
   - Use case and benefits
   - Possible implementation approach

### Code Contributions

1. **Fork the repository**
2. **Create a feature branch**: `git checkout -b feature/your-feature-name`
3. **Make your changes**
4. **Test thoroughly**
5. **Commit with clear messages**
6. **Push to your fork**
7. **Create a Pull Request**

## 🛠️ Development Setup

### Prerequisites
- .NET 8 SDK
- Docker & Docker Compose
- Visual Studio, VS Code, or Rider

### Getting Started
```bash
# Clone your fork
git clone https://github.com/your-username/requestr.git
cd requestr

# Start with Docker (easiest)
docker compose up -d

# OR run locally
dotnet restore
cd src/Requestr.Web
dotnet run
```

## 📋 Code Standards

### C# Coding Conventions
- Follow [Microsoft C# coding conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use PascalCase for public members
- Use camelCase for private fields
- Use async/await for database operations

### Database Development
- Use parameterized queries (Dapper)
- Update schema files in `/database/`
- Add corresponding models in `Requestr.Core/Models/`

### Testing
- Write unit tests for new features
- Test both Docker and local environments
- Ensure all tests pass before submitting PR

## 🔍 Pull Request Process

### Before Submitting
- [ ] Code follows project conventions
- [ ] Tests pass locally
- [ ] Documentation updated if needed
- [ ] Commit messages are clear

### PR Requirements
- **Title**: Clear, descriptive
- **Description**: Explain what and why
- **Testing**: How you tested changes
- **Screenshots**: For UI changes
- **Breaking Changes**: Note any breaking changes

### Review Process
1. Maintainers review within 48 hours
2. Address feedback promptly
3. Once approved, PR will be merged
4. Your contribution will be in the next release!

## 🧪 Testing Guidelines

### Local Testing
```bash
# Run unit tests
dotnet test

# Test Docker build
docker compose build

# Test database connectivity
docker exec -it requestr-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'DevPassword123!' -Q "SELECT @@VERSION"
```

### Manual Testing
- Test form creation and submission
- Test approval workflows
- Test different user roles
- Test database connections

## 📝 Documentation

### Code Documentation
- Document public APIs
- Add XML comments for complex logic
- Update README for new features

### User Documentation
- Update usage examples
- Add configuration notes
- Include troubleshooting tips

## 🎯 Project Areas

### Areas for Contribution
- **Core Features**: Form builder, approval workflows
- **UI/UX**: Blazor components, responsive design
- **Security**: Authentication, authorization
- **Performance**: Database optimization, caching
- **Testing**: Unit tests, integration tests
- **Documentation**: User guides, API docs

### Good First Issues
Look for issues labeled:
- `good first issue`
- `help wanted`
- `documentation`
- `bug`

## 🚀 Release Process

### Versioning
- We use semantic versioning (SemVer)
- Major.Minor.Patch format
- Breaking changes increment major version

### Release Notes
- Changes documented in CHANGELOG.md
- Contributors credited in releases
- Migration guides for breaking changes

## 📞 Getting Help

### Communication Channels
- **GitHub Issues**: Bug reports, feature requests
- **GitHub Discussions**: General questions, ideas
- **Code Review**: PR discussions

### Questions?
- Check existing issues and discussions
- Review documentation
- Ask in GitHub Discussions

## 🙏 Recognition

All contributors are recognized in:
- GitHub contributors list
- Release notes
- Project documentation

Thank you for contributing to Requestr! 🎉
