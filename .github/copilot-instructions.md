# Copilot Instructions for Syrx.Analyzers.Usings

## Project Overview

Syrx.Analyzers.Usings is a Roslyn analyzer and code fix provider that enforces all C# `using` statements are placed in a designated file (default: `Usings.cs`). This helps maintain consistent code organization by centralizing using statements in one location.

## Architecture

### Core Components

- **UsingsFileAnalyzer**: The main analyzer that detects using statements outside the designated file
- **UsingsFileCodeFixProvider**: Provides automatic code fixes to move using statements to the designated file
- **Diagnostic ID**: `USINGS001` - "Using statements must be in the designated file"

### Project Structure

```
src/
??? Syrx.Analyzers.Usings/           # Main analyzer project (.NET Standard 2.0)
?   ??? UsingsFileAnalyzer.cs        # Core analyzer implementation
?   ??? UsingsFileCodeFixProvider.cs # Code fix provider implementation
?   ??? AnalyzerReleases.*.md        # Release tracking files
tests/
??? unit/                            # Unit tests (.NET 8)
?   ??? Syrx.Analyzers.Usings.Tests.Unit/
?       ??? UsingsFileAnalyzerTests.cs      # Core analyzer tests
?       ??? ManualConfigAnalyzerTests.cs    # Configuration testing
??? integration/                     # Integration tests (.NET 8)
    ??? Syrx.Analyzers.Usings.Tests.Integration/
        ??? EditorConfigIntegrationTests.cs # Real-world scenario tests
```

## Key Features

### 1. Analyzer Functionality
- Scans all `.cs` files for using statements
- Reports `USINGS001` warnings for using statements outside the designated file
- Supports both regular and static using directives
- Configurable via `.editorconfig`

### 2. Code Fix Provider
- Automatically moves using statements to the designated file
- Converts regular using statements to global using statements
- Creates the designated file if it doesn't exist
- **Deduplicates using statements** using `UsingDirectiveComparer`
- **Sorts global usings** via `NormalizeWhitespace()`

### 3. Configuration Support
- **Primary key**: `dotnet_usings_file_name` (preferred)
- **Alternative key**: `usings_file_name` (backward compatibility)
- **Default file**: `Usings.cs`
- **Precedence**: `dotnet_` prefix takes priority
- **Value processing**: Trims whitespace, ignores empty values

## Code Style Guidelines

### C# Language Features
- **Target Framework**: .NET Standard 2.0 (analyzer), .NET 8 (tests)
- **Language Version**: C# 13.0 (analyzer), C# 12.0 (tests)
- **Null handling**: Use nullable reference types where appropriate
- **Pattern matching**: Use modern C# patterns for cleaner code

### Roslyn Best Practices
- Always check for null values from Roslyn APIs
- Use `ConfigureAwait(false)` for async operations
- Implement proper cancellation token support
- Follow thread-safe patterns for analyzers

### Testing Strategy
- **Unit tests**: Test core analyzer functionality with simple scenarios
- **Manual config tests**: Test configuration logic with direct injection
- **Integration tests**: Test real-world scenarios with file system operations
- **Coverage**: Aim for comprehensive test coverage of all configuration paths

## Development Guidelines

### Adding New Features
1. Update the analyzer first, then tests
2. Add configuration support via `.editorconfig` when applicable
3. Implement corresponding code fixes for new diagnostics
4. Update documentation in `.docs/` folder
5. Add release notes to `AnalyzerReleases.Unshipped.md`

### Configuration Handling
When adding new configuration options:
```csharp
private string GetConfigValue(AnalyzerOptions options, SyntaxTree tree, string primaryKey, string fallbackKey = null)
{
    var configOptions = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
    
    // Try primary key first
    if (configOptions.TryGetValue(primaryKey, out var value) && !string.IsNullOrWhiteSpace(value))
    {
        return value.Trim();
    }
    
    // Try fallback key if provided
    if (!string.IsNullOrEmpty(fallbackKey) && 
        configOptions.TryGetValue(fallbackKey, out var fallbackValue) && 
        !string.IsNullOrWhiteSpace(fallbackValue))
    {
        return fallbackValue.Trim();
    }
    
    return null;
}
```

### Testing Configuration
Use the manual configuration approach for reliable testing:
```csharp
var configOptions = new Dictionary<string, string>
{
    ["dotnet_your_config_key"] = "YourValue"
};
var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, analyzer, configOptions);
```

## Documentation Maintenance

### Files to Update
- `.docs/README.md` - Main project overview
- `.docs/INSTALLATION_AND_USAGE.md` - Detailed usage instructions
- `.docs/NUGET_PACKAGE_SUMMARY.md` - Package implementation details
- `src/*/AnalyzerReleases.*.md` - Release tracking

### Configuration Documentation
Always document new `.editorconfig` keys with examples:
```ini
[*.cs]
dotnet_your_new_setting = YourValue
```

## Build and Packaging

### NuGet Package
- **Package ID**: `Syrx.Analyzers.Usings`
- **Target**: .NET Standard 2.0 for maximum compatibility
- **Dependencies**: All marked as `PrivateAssets="all"`
- **Structure**: Follows Roslyn analyzer conventions

### Build Commands
```bash
dotnet build                 # Build all projects
dotnet test                  # Run all tests
dotnet pack                  # Create NuGet package
```

## Common Patterns

### Diagnostic Creation
```csharp
var diagnostic = Diagnostic.Create(Rule, location, messageArgs);
context.ReportDiagnostic(diagnostic);
```

### Code Fix Registration
```csharp
context.RegisterCodeFix(
    CodeAction.Create(
        title: "Your fix title",
        createChangedSolution: async c => { /* implementation */ },
        equivalenceKey: "YourFixKey"),
    diagnostic);
```

### Syntax Tree Analysis
```csharp
private void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
{
    var root = context.Tree.GetRoot(context.CancellationToken);
    var nodes = root.DescendantNodes().OfType<YourSyntaxType>();
    // Process nodes...
}
```

## Known Limitations

1. **EditorConfig Testing**: Real `.editorconfig` file testing is complex in unit tests; use manual config injection instead
2. **File Path Handling**: Always use `Path.GetFileName()` for cross-platform compatibility
3. **Deduplication**: The current implementation uses string comparison; consider semantic comparison for future enhancements
4. **Sorting**: Uses basic `NormalizeWhitespace()` - could be enhanced with custom sorting logic

## Future Enhancements

- Support for namespace-specific using organization
- Custom sorting rules for global usings
- Integration with EditorConfig formatting rules
- Support for using aliases and global using static directives
- Performance optimizations for large solutions

## Questions to Consider

When working on this project, always consider:
- How does this change affect existing users?
- Is the configuration backward compatible?
- Does the code fix handle edge cases properly?
- Are there performance implications for large codebases?
- Is the behavior consistent across different IDEs?