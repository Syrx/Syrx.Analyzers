# Installation and Usage Guide for Syrx.Analyzers.Usings

## Table of Contents
- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
  - [As a NuGet Package](#as-a-nuget-package)
  - [As a Project Reference](#as-a-project-reference)
- [Configuration](#configuration)
  - [Default Behavior](#default-behavior)
  - [Customizing the Usings File](#customizing-the-usings-file)
- [Usage](#usage)
  - [How the Analyzer Works](#how-the-analyzer-works)
  - [Responding to Diagnostics](#responding-to-diagnostics)
  - [Using the Code Fix Provider](#using-the-code-fix-provider)
- [Advanced Scenarios](#advanced-scenarios)
  - [Multi-project Solutions](#multi-project-solutions)
  - [CI Integration](#ci-integration)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)

---

## Overview
Syrx.Analyzers.Usings is a Roslyn analyzer and code fix provider that enforces all C# `using` statements are placed in a designated file (default: `Usings.cs`). It helps maintain a consistent code style and can be customized via `.editorconfig`.

## Prerequisites
- .NET 8 SDK or later
- Visual Studio 2022 or later, or any compatible IDE supporting Roslyn analyzers

## Installation

### As a NuGet Package
1. (If published) Open your project in Visual Studio.
2. Right-click your project and select **Manage NuGet Packages**.
3. Search for `Syrx.Analyzers.Usings` and install the latest version.
4. Confirm the package appears in your `.csproj` file:
   ```xml
   <PackageReference Include="Syrx.Analyzers.Usings" Version="x.y.z" />
   ```

### As a Project Reference
1. Clone or download the Syrx.Analyzers.Usings repository.
2. Add a project reference to `src/Syrx.Analyzers.Usings/Syrx.Analyzers.Usings.csproj` in your target project:
   ```xml
   <ProjectReference Include="../src/Syrx.Analyzers.Usings/Syrx.Analyzers.Usings.csproj" />
   ```
3. Build your solution to ensure the analyzer is loaded.

## Configuration

### Default Behavior
- All `using` statements should be placed in a file named `Usings.cs` at the root of your project.
- The analyzer will report a warning (`USINGS001`) for any `using` statement found outside this file.

### Customizing the Usings File
You can override the default file name using `.editorconfig`:
```ini
[*.cs]
dotnet_usings_file_name = CustomUsings.cs
```
Alternatively, you can use the legacy configuration key:
```ini
[*.cs]
usings_file_name = CustomUsings.cs
```
- Place this in your project's `.editorconfig` file.
- The analyzer will now expect all `using` statements in `CustomUsings.cs`.
- If both keys are present, `dotnet_usings_file_name` takes precedence.

## Usage

### How the Analyzer Works
- On build or in the IDE, the analyzer scans all `.cs` files.
- If a file contains `using` statements and is not the designated file, a diagnostic is reported for each `using` statement.
- The diagnostic message will indicate the correct file name.

### Responding to Diagnostics
- In Visual Studio, diagnostics appear in the Error List and as squiggles in the editor.
- Hover over the squiggle to see the message: `Move using statements to 'Usings.cs'` (or your configured file).

### Using the Code Fix Provider
- When a diagnostic is reported, Visual Studio will offer a quick action (lightbulb) to fix it.
- Click the lightbulb and select **Move using statements to designated file**.
- The code fix will:
  - Remove the `using` statements from the current file.
  - Add them to the designated file, creating it if necessary.
  - Avoid duplicate `using` statements.

## Advanced Scenarios

### Multi-project Solutions
- Each project can have its own designated usings file and `.editorconfig` settings.
- Ensure the analyzer is referenced in all projects where you want enforcement.

### CI Integration
- The analyzer runs as part of `dotnet build` and `dotnet test`.
- To fail builds on warnings, use:
  ```xml
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  ```
- Add analyzer NuGet/package/project reference to your CI pipeline configuration.

## Troubleshooting
- **Analyzer not running:** Ensure the analyzer is referenced and your IDE supports Roslyn analyzers.
- **Code fix not offered:** Make sure the code fix provider is included and your IDE is up to date.
- **Custom file not recognized:** Check your `.editorconfig` for typos and correct placement.
- **Duplicate usings:** The code fix provider deduplicates, but review the designated file after fixes.

## FAQ
**Q: Can I use multiple designated files?**
A: No, only one file name can be configured per project via `.editorconfig`.

**Q: Does this work with global usings?**
A: The analyzer targets regular `using` statements. Global usings may require additional rules.

**Q: Can I disable the analyzer for some files?**
A: Use standard Roslyn suppression comments or configure severity in `.editorconfig`.

**Q: How do I contribute?**
A: Fork the repository, add features or tests, and submit a pull request.

---

For more information, see the main [README.md](./README.md).
