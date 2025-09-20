# Syrx.Analyzers.Usings

A Roslyn analyzer and code fix provider for enforcing that all C# `using` statements are placed in a designated file (default: `Usings.cs`).

## Features
- **Analyzer**: Reports diagnostics (`USINGS001`) when `using` statements are found outside the designated file.
- **Code Fix Provider**: Offers a code fix to automatically move `using` statements to the designated file.
- **Configurable**: The designated file name can be overridden via `.editorconfig` using the `dotnet_usings_file_name` or `usings_file_name` keys.

## Usage
1. Add the analyzer to your project (as a NuGet package or project reference).
2. By default, all `using` statements should be placed in `Usings.cs`.
3. To use a custom file name, add the following to your `.editorconfig`:
   ```ini
   [*.cs]
   dotnet_usings_file_name = CustomUsings.cs
   ```
   Alternatively, you can use the legacy key:
   ```ini
   [*.cs]
   usings_file_name = CustomUsings.cs
   ```
4. When a violation is detected, the code fix provider can move the `using` statements to the correct file.

## Projects
- **src/Syrx.Analyzers.Usings**: The analyzer and code fix provider implementation.
- **tests/unit/Syrx.Analyzers.Usings.Tests.Unit**: Unit tests for the analyzer.
- **tests/integration/Syrx.Analyzers.Usings.Tests.Integration**: Integration tests (add your own scenarios).

## Build & Test
- Requires .NET 8 SDK.
- Run `dotnet build` to build all projects.
- Run `dotnet test` to execute all unit and integration tests.

## Extending
- Add more rules or code fixes by extending the analyzer and code fix provider classes.
- Add more tests to cover edge cases and integration scenarios.

## License
This project is open source. See the repository for license details.
