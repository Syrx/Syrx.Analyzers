# Syrx.Analyzers Security Research Report

## Metadata

| Field | Value |
|---|---|
| **Report name** | `Syrx.Analyzers-security-research-report-20260322.md` |
| **Generated on** | 2026-03-22 |
| **Assessor** | `security-researcher` |
| **Scope** | Syrx.Analyzers solution — full workspace at `c:\Projects\Syrx.Analyzers` |
| **Report location** | `.docs/research/security/` |
| **Evidence sources** | Source code, project files, CI/CD workflows, NuGet dependency manifest, build output, `dotnet list package --vulnerable`, `dotnet list package --deprecated` |

---

## Scope and Constraints

**In scope:**

- `src/Syrx.Analyzers.Usings/UsingsFileAnalyzer.cs` — Roslyn analyzer implementation
- `src/Syrx.Analyzers.Usings/UsingsFileCodeFixProvider.cs` — Roslyn code fix provider implementation
- `src/Syrx.Analyzers.Usings/Usings.cs` — global using declarations
- `src/Syrx.Analyzers.Usings/Syrx.Analyzers.Usings.csproj` — project and package metadata
- `Directory.Build.props` — shared build configuration
- `tests/unit/...` — unit test projects (both test files + project files)
- `tests/integration/...` — integration test projects (both test files + project files)
- `.github/workflows/security.yml` — security CI workflow
- `.github/workflows/publish.yml` — publish and build CI workflow
- `.github/dependabot.yml` — automated dependency update configuration
- `.editorconfig`, `.config/.editorconfig` — editor configuration files
- `.github/security-triage.md` — security triage process document
- NuGet dependency graph (direct and transitive)

**Out of scope:**

- Runtime/live process analysis (no test execution trace was collected)
- NuGet.org package registry validation (external service; URL reachability not tested)
- GitHub repository access controls and Actions secrets (external service)
- IDE host behavior when applying the code fix in Visual Studio or Rider (runtime environment)

**Constraints:**

- No static analysis tool output (e.g., Semgrep, Roslyn security analyzers output) was produced separately; CodeQL results from CI are not accessible in this workspace session
- The `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` files are absent despite being referenced in project instructions; diagnostic change history could not be assessed
- Strong-name signing key presence could not be fully verified (no `.snk` files found; no signing attributes in the project files)
- NuGet package published on `api.nuget.org` was not inspected directly (not downloaded during this assessment)

---

## Methodology

- **Workspace instructions reviewed:** `security-and-secure-coding.instructions.md`, `csharp-development-and-standards.instructions.md`, `github-actions-ci-cd-best-practices.instructions.md`, `copilot-instructions.md`
- **Skills used:** `security-research` (primary), manual code review, dependency hygiene review
- **Research methods:** Full source file reads, `grep_search` for path/file/parsing patterns, `dotnet build` (clean build — no warnings or errors), `dotnet list package --vulnerable` (no CVEs found), `dotnet list package --deprecated` (deprecations catalogued), file system enumeration for lock files and signing keys, CI/CD workflow inspection

---

## Executive Summary

The Syrx.Analyzers.Usings solution is a lean Roslyn analyzer and code fix provider. The overall security posture is **good for its threat surface**: the codebase has no hardcoded secrets, no SQL/shell injection risks, and the CI pipeline implements strong controls — pinned action SHAs, CodeQL SAST, dependency review gates, and secret scanning via Gitleaks.

The most significant finding is a **path traversal risk in the code fix provider** (SEC-002): the target filename read from an `.editorconfig` configuration entry is not sanitized with `Path.GetFileName()` before being passed to `project.AddDocument()`. An adversarially prepared `.editorconfig` checked into source control could cause the code fix to create a file outside the intended project directory when another developer accepts the fix in the IDE. This is a medium-severity supply-chain vector specific to code fix providers.

A closely related architectural weakness (SEC-001) is the code fix's fragile message-text parsing to reconstruct the target filename: the Roslyn-idiomatic approach is to transmit structured data through `Diagnostic.Properties`, not by splitting a human-readable, locale-sensitive string.

No known CVEs were found in any direct or transitive NuGet dependency. Three test packages are deprecated and should be migrated. No `packages.lock.json` files exist, which is a supply-chain hygiene gap (SEC-004). The NuGet package is unsigned (SEC-006). Minor CI hygiene gaps exist in the publish workflow (SEC-007, SEC-008).

The solution is **research-only assessed** — no fixes have been implemented.

---

## Findings Summary

| ID | Severity | Confidence | Category | Affected area | Short title |
|---|---|---|---|---|---|
| SEC-001 | Medium | High | Input Validation / Fragile Parsing | `UsingsFileCodeFixProvider.cs:33` | Diagnostic message split to reconstruct filename |
| SEC-002 | Medium | High | Path Traversal | `UsingsFileCodeFixProvider.cs:37,43` | Unsanitized target filename in `project.AddDocument()` |
| SEC-003 | Low | High | Dependency Hygiene | `Directory.Build.props`, test projects | No `packages.lock.json` — transitive dependency graph unpinned |
| SEC-004 | Low | High | Dependency Hygiene | Test projects (`Directory.Build.props`) | Deprecated test packages with no migration path |
| SEC-005 | Low | High | Supply Chain Integrity | `Syrx.Analyzers.Usings.csproj` | NuGet package unsigned; no strong-name signing |
| SEC-006 | Low | Medium | CI/CD Hardening | `.github/workflows/publish.yml` | `run_test` job missing explicit `permissions:` declaration |
| SEC-007 | Informational | High | CI/CD Hardening | `.github/workflows/publish.yml` | `validate_nuget` job downloads artifact but performs no verification |
| SEC-008 | Informational | High | Error Handling | `EditorConfigIntegrationTests.cs:88` | Silent empty `catch` block in test `Dispose()` |
| SEC-009 | Informational | High | Roslyn Conventions / Traceability | `src/Syrx.Analyzers.Usings/` | `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` absent |
| SEC-010 | Informational | High | Code Quality / Security Design | `UsingsFileCodeFixProvider.cs` | `UsingDirectiveComparer` uses `ToString()` — deduplication is whitespace-sensitive |

---

## Detailed Findings

---

### SEC-001 — Diagnostic Message Text Split to Reconstruct Target Filename

- **Severity:** Medium
- **Confidence:** High
- **Category:** Input Validation / Fragile Parsing / Security Design
- **CWE:** CWE-116 (Improper Encoding or Escaping of Output); CWE-807 (Reliance on Untrusted Inputs in a Security Decision)
- **OWASP:** A03:2021 — Injection (text injection into structured output consumed as input)
- **Affected files or symbols:**
  - `src/Syrx.Analyzers.Usings/UsingsFileCodeFixProvider.cs`, line 33

**Evidence:**

```csharp
// UsingsFileCodeFixProvider.cs, line 33
var targetFileName = diagnostic.GetMessage().Split('\'').Skip(1).FirstOrDefault() ?? "Usings.cs";
```

The diagnostic message format is defined in `UsingsFileAnalyzer.cs` as:
```csharp
// UsingsFileAnalyzer.cs, lines 10-11
private static readonly LocalizableString MessageFormat = "Move using statements to '{0}'";
```

The code fix reconstructs the target filename by splitting the human-readable message text on the single-quote character and taking the second element (`Skip(1)`). The direct consequence is that this fragment — `Split('\'').Skip(1).FirstOrDefault()` — is the channel through which an untrusted `.editorconfig` value re-enters the code fix as an executable instruction (which file to write to and which existing file to locate). Roslyn's standard mechanism for passing structured data from analyzer to code fix provider is `Diagnostic.Properties` (an `ImmutableDictionary<string, string>`).

**Impact:**

1. **Silent fallback on filename containing single quotes:** A configured value such as `O'Brian.cs` would cause `Split('\'')` to return `["Move using statements to ", "O", "Brian.cs"]`. `.Skip(1).FirstOrDefault()` yields `"O"` — which is not a valid filename. The code silently falls back to `"Usings.cs"`, a different target than the one configured. The developer has no indication the fix misapplied.
2. **Locale fragility:** `GetMessage()` returns the localized string. While the current `LocalizableString` declaration is a constant (not actually localized via resource files), future localization of this message would silently break the parsing.
3. **Maintenance risk:** Any change to the `MessageFormat` string in the analyzer would break the code fix silently at deploy time, with no compiler enforcement.
4. **Design bypass:** The intent of `.editorconfig` configuration is to route the fix to the correct file. A parsing failure silently routes all usings to `"Usings.cs"` regardless of project configuration.

**Recommended remediation:**

Use `Diagnostic.Properties` to carry the structured target filename from the analyzer to the code fix:

```csharp
// In UsingsFileAnalyzer, populate a properties dictionary:
var properties = ImmutableDictionary<string, string>.Empty
    .Add("TargetFileName", targetFileName);
var diagnostic = Diagnostic.Create(Rule, location, properties, targetFileName);
```

In the code fix, read it cleanly:

```csharp
var targetFileName = diagnostic.Properties.TryGetValue("TargetFileName", out var fn)
    ? fn
    : "Usings.cs";
```

This is the Roslyn-idiomatic pattern, is not locale-sensitive, does not depend on string format stability, and is the approach used in Microsoft's Roslyn analyzer samples.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-002 — Unsanitized Target Filename Passed to `project.AddDocument()` — Path Traversal Risk

- **Severity:** Medium
- **Confidence:** High
- **Category:** Path Traversal
- **CWE:** CWE-22 (Improper Limitation of a Pathname to a Restricted Directory — Path Traversal)
- **OWASP:** A01:2021 — Broken Access Control
- **Affected files or symbols:**
  - `src/Syrx.Analyzers.Usings/UsingsFileCodeFixProvider.cs`, lines 33, 37, 43
  - `src/Syrx.Analyzers.Usings/UsingsFileAnalyzer.cs`, lines 34–36

**Evidence:**

```csharp
// UsingsFileAnalyzer.cs — configuration read without path sanitization
private string GetConfiguredUsingsFileName(AnalyzerOptions options, SyntaxTree tree)
{
    var configOptions = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
    if (configOptions.TryGetValue("dotnet_usings_file_name", out var value) && !string.IsNullOrWhiteSpace(value))
    {
        return value.Trim();  // No Path.GetFileName() stripping
    }
    ...
}
```

```csharp
// UsingsFileCodeFixProvider.cs — targetFileName used in file-system-affecting operations
var targetFileName = diagnostic.GetMessage().Split('\'').Skip(1).FirstOrDefault() ?? "Usings.cs";

// Line 37: comparison does NOT normalize targetFileName with Path.GetFileName
var designatedDoc = project.Documents.FirstOrDefault(d =>
    !string.IsNullOrEmpty(d.FilePath) &&
    System.IO.Path.GetFileName(d.FilePath).Equals(targetFileName, System.StringComparison.OrdinalIgnoreCase));

// Line 43: targetFileName used directly as the name for a new document
var newDoc = project.AddDocument(targetFileName, usingsText);
```

**Impact:**

An `.editorconfig` entry such as:

```ini
[*.cs]
dotnet_usings_file_name = ../../SharedLib/Usings.cs
```

would cause:

1. The _analyzer_ to embed `../../SharedLib/Usings.cs` into the diagnostic message without sanitization.
2. The _code fix_ to extract that value via message parsing.
3. The comparison at line 37 to fail — `Path.GetFileName(d.FilePath)` returns only `"Usings.cs"` for every document, but `targetFileName` is `"../../SharedLib/Usings.cs"`, so the `Equals()` check never matches.
4. The code always falls through to the `AddDocument` branch (line 43) with the unsanitized path, adding an in-workspace document named `../../SharedLib/Usings.cs`.

In the Roslyn/Visual Studio workspace model, when the IDE applies the suggested fix and triggers an implicit save, it will resolve the path relative to the project folder. The resulting write could land outside the project directory — for example, creating or overwriting a file in a parent or sibling directory. In Visual Studio `AdhocWorkspace` behavior, the document name is used as a file path hint; in project-backed solutions, the document `FilePath` is constructed by combining the project folder with the document name.

**Threat model:**

- **Actor:** A developer or CI actor who controls or can modify the `.editorconfig` file (commonly checked into source control).
- **Victim:** A developer using Visual Studio or Rider who applies the USINGS001 code fix.
- **Scope of damage:** A file is created or overwritten outside the intended project directory. With a relative traversal like `../../src/Program.cs`, existing production files could be overwritten with the aggregated using directives.
- **Precondition:** The victim must apply the code fix (one click in the IDE) while working with a repository that contains the adversarial `.editorconfig`.

Since `.editorconfig` files are routinely committed to source control and applied project-wide, this is a meaningful supply-chain vector for teams consuming this analyzer.

**Note:** The `copilot-instructions.md` in this repository explicitly states, "File Path Handling: Always use `Path.GetFileName()` for cross-platform compatibility," acknowledging this requirement — but it is not consistently applied in the code fix path.

**Recommended remediation:**

1. Apply `Path.GetFileName()` to strip any directory component from the configured value in `GetConfiguredUsingsFileName()`:  
   `return Path.GetFileName(value.Trim());`

2. Apply the same sanitization defensively in the code fix provider before any use of `targetFileName`:  
   `targetFileName = Path.GetFileName(targetFileName);`

3. Optionally validate that the resulting filename contains no invalid path characters using `Path.GetInvalidFileNameChars()`.

These steps should be applied at both the read site (analyzer) and the use site (code fix) following the defence-in-depth principle.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-003 — No NuGet Package Lock Files (Supply Chain — Unpinned Transitive Dependency Graph)

- **Severity:** Low
- **Confidence:** High
- **Category:** Dependency Hygiene / Supply Chain
- **CWE:** CWE-1395 (Dependency on Vulnerable Third-Party Component)
- **OWASP:** A06:2021 — Vulnerable and Outdated Components / Supply Chain
- **Affected files or symbols:**
  - All project files: `Syrx.Analyzers.Usings.csproj`, `Syrx.Analyzers.Usings.Tests.Unit.csproj`, `Syrx.Analyzers.Usings.Tests.Integration.csproj`, `Directory.Build.props`

**Evidence:**

```powershell
# No packages.lock.json found anywhere in the workspace:
Get-ChildItem -Path "c:\Projects\Syrx.Analyzers" -Recurse -Filter "packages.lock.json"
# Result: (empty)
```

No `RestorePackagesWithLockFile` or `RestoreLockedMode` properties are set in any project file. There is also no `nuget.config` enforcing a known-good feed or specific sources.

The dependency graph includes transitive packages such as `Humanizer.Core 2.14.1`, `System.Runtime.CompilerServices.Unsafe 6.1.0`, `System.Text.Encoding.CodePages 8.0.0`, and others (full list in Appendix). Without a lock file, NuGet performs floating resolution at each restore, meaning the resolved version of a transitive package could silently change between builds if a new patch is published.

**Impact:**

- Reproducible builds are not guaranteed; two developers or two CI runs could resolve different transitive packages.
- A compromised NuGet feed (e.g., a compromised `api.nuget.org` package or a package hijack) could serve malicious content for a transitive dependency version that is within the allowed range, and the change would not be detectable without a lock file diff.
- No `nuget.config` is present, so restore defaults to the system-level configured sources (`api.nuget.org`, local `C:\Nuget`, `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\`) — the local sources introduce a potential for package shadowing if anyone with access puts a package in `C:\Nuget`.

**Recommended remediation:**

1. Enable `RestorePackagesWithLockFile` in `Directory.Build.props` for all projects.
2. Commit the resulting `packages.lock.json` files to source control.
3. Add `--locked-mode` to all `dotnet restore` / `dotnet build` calls in GitHub Actions to enforce the lock.
4. Add a `nuget.config` that explicitly lists allowed sources, removing the implicit dependency on system-level source configuration.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-004 — Deprecated Test Package References

- **Severity:** Low
- **Confidence:** High
- **Category:** Dependency Hygiene
- **CWE:** CWE-1395 (Dependency on Vulnerable Third-Party Component)
- **OWASP:** A06:2021 — Vulnerable and Outdated Components
- **Affected files or symbols:**
  - `Directory.Build.props` (shared test dependencies)
  - `Syrx.Analyzers.Usings.Tests.Integration.csproj`

**Evidence:**

```
# dotnet list package --deprecated output:
Project `Syrx.Analyzers.Usings.Tests.Integration` has the following deprecated packages
   > Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit  1.1.2  Legacy  → Microsoft.CodeAnalysis.CSharp.Analyzer.Testing >= 0.0.0
   > Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.XUnit   1.1.2  Legacy  → Microsoft.CodeAnalysis.CSharp.CodeFix.Testing >= 0.0.0
   > Microsoft.CodeAnalysis.Testing.Verifiers.XUnit        1.1.2  Legacy  → (no suggested alternative listed)
   > xunit                                                  2.9.3  Legacy  → xunit.v3 >= 0.0.0

Project `Syrx.Analyzers.Usings.Tests.Unit` has the following deprecated packages
   > Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit  1.1.2  Legacy
   > Microsoft.CodeAnalysis.Testing.Verifiers.XUnit        1.1.2  Legacy
   > xunit                                                  2.9.3  Legacy  → xunit.v3 >= 0.0.0
```

The `Microsoft.CodeAnalysis.*.XUnit` packages are legacy wrappers that have been superseded by non-XUnit-specific packages. The `xunit` 2.x package line has been superseded by `xunit.v3`.

**Impact:**

Deprecated packages may not receive future security patches. If a vulnerability is found in the legacy `xunit` 2.9.x runtime or the legacy CodeAnalysis testing wrappers after their end-of-support, fixes will only be available in the successor packages. This is a low immediacy risk because no known CVEs affect these packages today (confirmed via `dotnet list package --vulnerable`).

**Recommended remediation:**

Migrate test projects to:
- `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` (non-XUnit-specific)
- `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` (non-XUnit-specific)
- `xunit.v3` (per NuGet suggestion)

This migration should be done incrementally with test verification.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-005 — NuGet Package Published Without Package Signing

- **Severity:** Low
- **Confidence:** High
- **Category:** Supply Chain Integrity
- **CWE:** CWE-347 (Improper Verification of Cryptographic Signature)
- **OWASP:** A08:2021 — Software and Data Integrity Failures
- **Affected files or symbols:**
  - `src/Syrx.Analyzers.Usings/Syrx.Analyzers.Usings.csproj`
  - `Directory.Build.props`

**Evidence:**

No `.snk` strong name key file was found in the workspace:
```powershell
Get-ChildItem -Recurse -Filter "*.snk" # Result: (empty)
```

No signing-related MSBuild properties (`SignAssembly`, `AssemblyOriginatorKeyFile`, `PackageSigningCertificate`) are present in `Syrx.Analyzers.Usings.csproj` or `Directory.Build.props`.

The publish workflow (`publish.yml`) uses `dotnet nuget push` without a `--certificate-fingerprint` argument, confirming no package signing is applied at publish time.

**Impact:**

An unsigned NuGet package cannot be cryptographically verified by consumers. Consumers relying on NuGet's package signature verification will receive a warning or (depending on their policy) fail to install. More critically, an attacker who gains publish access to the NuGet account can push a malicious version of the package (e.g., `1.0.1`) that developers would install without any integrity signal. Roslyn analyzers run in the compiler/IDE process and have full process trust — a malicious analyzer could read files, exfiltrate code, or modify build output.

**Recommended remediation:**

1. Obtain a code-signing certificate (can use an Azure Key Vault certificate or a self-hosted cert).
2. Add `dotnet nuget sign` to the publish CI step.
3. Consider also applying strong-name signing (`SignAssembly = true`) for predictable assembly identity, particularly relevant for analyzers that must be loaded in specific trust contexts.

- **Recommended validating agent or skill:** `csharp-engineering`, `architecture-and-ddd`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-006 — `run_test` CI Job Missing Explicit `permissions:` Declaration

- **Severity:** Low
- **Confidence:** High
- **Category:** CI/CD Hardening / Least Privilege
- **CWE:** CWE-272 (Least Privilege Violation)
- **OWASP:** A05:2021 — Security Misconfiguration
- **Affected files or symbols:**
  - `.github/workflows/publish.yml`, `run_test` job

**Evidence:**

```yaml
# publish.yml — workflow-level permissions deny-all:
permissions: {}

# run_test job — no explicit permissions block:
run_test:
  runs-on: ubuntu-latest
  # permissions: <-- ABSENT
  steps:
    - uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
    ...
```

Other jobs in the same workflow declare explicit permissions:
```yaml
create_nuget:
  permissions:
    contents: read  # ← explicitly declared
```

The `actions/checkout` action requires at minimum `contents: read`. With `permissions: {}` at the workflow level and no job-level override, the effective permission for `run_test` may be insufficient or undefined depending on the GitHub Actions runtime version and whether the workflow-level empty set is inherited.

**Impact:**

While this is unlikely to be directly exploitable, it is a configuration inconsistency. GitHub Actions documentation recommends declaring permissions explicitly at every job that requires them, even when inheriting a restrictive default, so that engineers can audit the permission set without needing to trace inheritance rules. More importantly, if different GitHub Actions runner versions interpret empty-inheritance differently, the job could fail or unexpectedly gain permissions.

**Recommended remediation:**

Add an explicit `permissions:` block to the `run_test` job:

```yaml
run_test:
  runs-on: ubuntu-latest
  permissions:
    contents: read
```

- **Recommended validating agent or skill:** `csharp-engineering` (can be authored by any engineer familiar with GitHub Actions)
- **Implementation status:** Not implemented by security-researcher

---

### SEC-007 — `validate_nuget` Job Downloads Artifact But Performs No Actual Validation

- **Severity:** Informational
- **Confidence:** High
- **Category:** CI/CD Hardening
- **CWE:** N/A
- **OWASP:** A08:2021 — Software and Data Integrity Failures (process gap)
- **Affected files or symbols:**
  - `.github/workflows/publish.yml`, `validate_nuget` job

**Evidence:**

```yaml
validate_nuget:
  runs-on: ubuntu-latest
  needs: [ create_nuget ]
  steps:
    # Install the .NET SDK indicated in the global.json file
    - name: Setup .NET
      uses: actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7

    # Download the NuGet package created in the previous job
    - uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c
      with:
        name: nuget
        path: ${{ env.NuGetDirectory }}
    # <-- No validation steps follow
```

The job name `validate_nuget` implies it validates the package, but the only steps are: set up the SDK and download the artifact. No `dotnet nuget verify`, `dotnet package-validation`, or content inspection command is run.

**Impact:**

The validation gate is a no-op. It provides false assurance that the package has been validated before deployment. A malformed, incorrectly structured, or missing `.nupkg` would still allow the `deploy` job to proceed.

**Recommended remediation:**

Add meaningful validation steps such as:
```yaml
- name: Verify NuGet package structure
  run: |
    Get-ChildItem -Path ${{ env.NuGetDirectory }} -Filter *.nupkg | ForEach-Object {
      dotnet package-validation $_.FullName
    }
```
Or at minimum inspect that the package contains the expected analyzer DLL in the `analyzers/dotnet/cs/` path.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-008 — Silent Empty `catch` Block in Integration Test `Dispose()`

- **Severity:** Informational
- **Confidence:** High
- **Category:** Error Handling
- **CWE:** CWE-390 (Detection of Error Condition Without Action)
- **OWASP:** A09:2021 — Security Logging and Monitoring Failures
- **Affected files or symbols:**
  - `tests/integration/Syrx.Analyzers.Usings.Tests.Integration/EditorConfigIntegrationTests.cs`, lines 83–91

**Evidence:**

```csharp
public void Dispose()
{
    try
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
    catch
    {
        // Ignore cleanup errors
    }
}
```

The workspace security instruction `security-and-secure-coding.instructions.md` explicitly states: "No silent catches. Log with actionable context, then rethrow or translate to a safe exception."

This is test code and the failure mode (leftover temp files) is relatively benign. However:

1. Temp files created in integration tests could contain sensitive content (source code snippets used as test inputs, `.editorconfig` samples).
2. A repeated failure to clean up temp directories (e.g., due to a handle leak or access denial) would go undetected, accumulating test artifacts at `Path.GetTempPath()`.
3. The silent catch suppresses OS-level errors that could indicate misuse of the temp directory (e.g., a privilege escalation attempt by another process preventing deletion).

**Recommended remediation:**

Replace the empty catch with either:
- A logged warning (using `Console.Error.WriteLine` or `Trace.TraceWarning`) so the failure is observable in test output, or
- A more targeted `catch (IOException)` / `catch (UnauthorizedAccessException)` to avoid swallowing unexpected exceptions.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-009 — `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` Tracking Files Absent

- **Severity:** Informational
- **Confidence:** High
- **Category:** Roslyn Conventions / Diagnostic Traceability
- **CWE:** N/A
- **OWASP:** N/A
- **Affected files or symbols:**
  - `src/Syrx.Analyzers.Usings/` (missing files)

**Evidence:**

```powershell
Get-ChildItem -Recurse -Filter "AnalyzerReleases*"
# Result: (empty)
```

The Roslyn SDK convention for Roslyn analyzer packages requires `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` to track when diagnostics were introduced or changed. The `copilot-instructions.md` mentions these files:
> "Update `AnalyzerReleases.*.md` — Release tracking"

The `Syrx.Analyzers.Usings.csproj` has a comment referencing them but they are not present.

**Impact:**

Without these files, the Roslyn package build emits analyzer release tracking warnings (RS2000 category). More importantly, from a security standpoint, the diagnostic release history is invisible — there is no record of when `USINGS001` was introduced or what its documented behavior is. Any future change to the diagnostic severity, suppression, or behavior would be untracked. Untracked diagnostic changes could be used to suppress legitimate security-related diagnostics in downstream projects silently.

**Recommended remediation:**

Create `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` per the Roslyn SDK documentation for analyzer release tracking.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

### SEC-010 — `UsingDirectiveComparer` Uses `ToString()` for Equality — Deduplication Bypass

- **Severity:** Informational
- **Confidence:** High
- **Category:** Code Quality / Security Design
- **CWE:** CWE-697 (Incorrect Comparison)
- **OWASP:** N/A
- **Affected files or symbols:**
  - `src/Syrx.Analyzers.Usings/UsingsFileCodeFixProvider.cs`, `UsingDirectiveComparer` class (lines ~70–82)

**Evidence:**

```csharp
private class UsingDirectiveComparer : IEqualityComparer<UsingDirectiveSyntax>
{
    public bool Equals(UsingDirectiveSyntax x, UsingDirectiveSyntax y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        return x.ToString() == y.ToString();  // ← text comparison, not semantic
    }
    public int GetHashCode(UsingDirectiveSyntax obj)
    {
        return obj?.ToString()?.GetHashCode() ?? 0;
    }
}
```

`UsingDirectiveSyntax.ToString()` serializes the syntax node to text including trivia (whitespace, comments). Two directives that are semantically identical but have different trivia will have different `ToString()` representations and will thus not be deduplicated.

**Impact:**

Duplicate `global using` directives in the designated file produce a compile-time error (`CS0105`). A user who applies the code fix in a project where the same using directive appears in two files — and where each directive has differing leading/trailing whitespace or comments — would produce a broken `Usings.cs` file. This is not directly exploitable as a security vulnerability, but it is a correctness flaw in the fix that silently introduces build-breaking state. The `copilot-instructions.md` acknowledges this as a known limitation.

**Recommended remediation:**

Use `u.Name?.ToFullString().Trim()` (the namespace name only, without trivia) for both `Equals` and `GetHashCode` in `UsingDirectiveComparer`. This provides semantic equality independent of trivia.

- **Recommended validating agent or skill:** `csharp-engineering`
- **Implementation status:** Not implemented by security-researcher

---

## Missing Skills, Information, or Tooling

| Gap | Details | Confidence impact |
|---|---|---|
| CodeQL SAST results | The `security.yml` workflow runs CodeQL `security-extended` on CI. The output (SARIF results) was not accessible during this assessment. Any CodeQL findings on live code are not captured here. | Medium — CodeQL may surface findings not visible through manual review |
| Runtime IDE behavior when applying code fix | The path traversal risk in SEC-002 depends on how the Roslyn IDE workspace (`VisualStudioWorkspace`) resolves `project.AddDocument(name, ...)` into a filesystem path. This was assessed based on Roslyn documentation and workspace model knowledge, but was not verified against a live IDE session. | Low — the mechanism is well-documented; the risk exists in theory and should be addressed |
| NuGet.org package registry state | Whether the package is already published, whether the `PackageProjectUrl` (`https://github.com/syrx/Syrx.Analyzers.Usings`) resolves to an existing repository, and whether package signing is enforced were not verified against the live registry. | Low — concerns are based on absence of evidence in the workspace |
| Strong-name signing infrastructure | The absence of `.snk` files is confirmed, but the possibility that signing is handled outside the workspace (e.g., in a CI secret, HSM, or Azure Key Vault) cannot be ruled out from workspace inspection alone. | Low |
| Formal threat model documentation | No `THREAT_MODEL.md` or equivalent exists. This limits the ability to reason about assumed trust boundaries (e.g., is `.editorconfig` considered a trusted input?). | Medium — absence of a stated trust model forces assumptions about attacker scope |

---

## Cross-Agent Remediation Handoff Recommendations

| Recommendation | Owner | Why |
|---|---|---|
| Refactor `UsingsFileCodeFixProvider` to use `Diagnostic.Properties` for filename transfer (SEC-001) | `csharp-engineering` | Requires Roslyn API change in both analyzer and code fix — pure engineering task |
| Apply `Path.GetFileName()` sanitization to filename at analyzer read site and code fix use site (SEC-002) | `csharp-engineering` | Direct code change in `UsingsFileAnalyzer.cs` and `UsingsFileCodeFixProvider.cs` |
| Enable `RestorePackagesWithLockFile` and commit lock files; add `nuget.config` (SEC-003) | `csharp-engineering` | MSBuild and config changes |
| Migrate deprecated test packages to current equivalents (SEC-004) | `csharp-engineering` | Package reference updates with test verification |
| Investigate and implement NuGet package signing (SEC-005) | `csharp-engineering` + consider `adr-generator` ADR | Signing infrastructure decision may warrant an ADR |
| Add `permissions: contents: read` to `run_test` job (SEC-006) | `csharp-engineering` | One-line CI YAML change |
| Implement actual NuGet package validation in `validate_nuget` job (SEC-007) | `csharp-engineering` | CI YAML change |
| Replace silent `catch` with observable cleanup logging in `EditorConfigIntegrationTests` (SEC-008) | `csharp-engineering` | Test code change |
| Create `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` (SEC-009) | `csharp-engineering` | Roslyn convention compliance |
| Fix `UsingDirectiveComparer` to use name-only comparison (SEC-010) | `csharp-engineering` | Code quality fix in code fix provider |

---

## Appendix

### Files and Symbols Reviewed

| File | Purpose |
|---|---|
| `src/Syrx.Analyzers.Usings/UsingsFileAnalyzer.cs` | Core analyzer |
| `src/Syrx.Analyzers.Usings/UsingsFileCodeFixProvider.cs` | Code fix provider |
| `src/Syrx.Analyzers.Usings/Usings.cs` | Global using declarations |
| `src/Syrx.Analyzers.Usings/Syrx.Analyzers.Usings.csproj` | Package and project metadata |
| `Directory.Build.props` | Shared build and dependency configuration |
| `tests/unit/.../UsingsFileAnalyzerTests.cs` | Unit tests for analyzer |
| `tests/unit/.../ManualConfigAnalyzerTests.cs` | Configuration-path unit tests |
| `tests/integration/.../EditorConfigIntegrationTests.cs` | Integration tests with temp files |
| `tests/integration/.../UsingsFileCodeFixProviderIntegrationTests.cs` | Code fix integration tests |
| `tests/unit/.../Syrx.Analyzers.Usings.Tests.Unit.csproj` | Unit test project |
| `tests/integration/.../Syrx.Analyzers.Usings.Tests.Integration.csproj` | Integration test project |
| `.github/workflows/security.yml` | Security CI workflow |
| `.github/workflows/publish.yml` | Build and publish CI workflow |
| `.github/dependabot.yml` | Automated dependency updates |
| `.editorconfig` | Root editor configuration |
| `.config/.editorconfig` | Nested editor configuration |
| `.github/security-triage.md` | Security triage ownership document |

### Searches and Diagnostics Used

| Tool / Query | Purpose |
|---|---|
| `file_search "**/*.yml"` | Locate CI/CD workflows |
| `file_search "**/.editorconfig"` | Locate editor config files |
| `file_search "**/nuget.config"` | Check for NuGet source configuration |
| `file_search "**/*.snk"` | Check for strong-name signing keys |
| `file_search "**/AnalyzerReleases*.md"` | Check for release tracking files |
| `file_search "**/packages.lock.json"` | Check for NuGet lock files |
| `grep_search "path.combine|AddDocument|WriteAll|File."` (src) | Path and file operation audit |
| `grep_search "diagnostic.GetMessage|Split|targetFileName"` | Trace filename extraction logic |
| `grep_search "catch"` (integration tests) | Error handling audit |
| `dotnet build --configuration Release` | Build validation — result: 0 warnings, 0 errors |
| `dotnet list package --vulnerable` | CVE scan — result: no vulnerable packages |
| `dotnet list package --deprecated` | Deprecation scan — result: 4 deprecated test packages |
| `dotnet list package --include-transitive` | Full dependency inventory |
| `Get-ChildItem -Recurse -Filter "packages.lock.json"` | Lock file presence check |

### Transitive Dependency Inventory (Production Package)

| Package | Version |
|---|---|
| `Microsoft.CodeAnalysis.Analyzers` | 5.3.0 |
| `Microsoft.CodeAnalysis.CSharp` | 5.3.0 |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 5.3.0 |
| `Microsoft.CodeAnalysis.Common` | 5.3.0 |
| `Microsoft.CodeAnalysis.Workspaces.Common` | 5.3.0 |
| `Humanizer.Core` | 2.14.1 |
| `Microsoft.Bcl.AsyncInterfaces` | 9.0.0 |
| `System.Collections.Immutable` | 9.0.0 |
| `System.Composition` (+ subtypes) | 9.0.0 |
| `System.IO.Pipelines` | 9.0.0 |
| `System.Reflection.Metadata` | 9.0.0 |
| `System.Text.Encoding.CodePages` | 8.0.0 |
| `System.Runtime.CompilerServices.Unsafe` | 6.1.0 |
| `System.Memory` | 4.6.0 |
| `NETStandard.Library` | 2.0.3 |

### References

- [Roslyn Analyzer Diagnostic.Properties pattern](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)
- [CWE-22: Path Traversal](https://cwe.mitre.org/data/definitions/22.html)
- [OWASP A01:2021 — Broken Access Control](https://owasp.org/Top10/A01_2021-Broken_Access_Control/)
- [OWASP A06:2021 — Vulnerable and Outdated Components](https://owasp.org/Top10/A06_2021-Vulnerable_and_Outdated_Components/)
- [OWASP A08:2021 — Software and Data Integrity Failures](https://owasp.org/Top10/A08_2021-Software_and_Data_Integrity_Failures/)
- [NuGet Package Signing documentation](https://docs.microsoft.com/en-us/nuget/create-packages/sign-a-package)
- [NuGet lock files for reproducible builds](https://docs.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)
- [Roslyn analyzer release tracking](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md)
- [EnforceExtendedAnalyzerRules](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20API%20Enforcement.md)
- [GitHub Actions — least privilege permissions](https://docs.github.com/en/actions/security-guides/automatic-token-authentication#permissions-for-the-github_token)
- Workspace instruction: `security-and-secure-coding.instructions.md`
- Workspace instruction: `csharp-development-and-standards.instructions.md`
- Workspace instruction: `github-actions-ci-cd-best-practices.instructions.md`

### Assumptions

1. `.editorconfig` files are treated as semi-trusted inputs — they are commonly checked into source control and may be modified by contributors or attackers with repository write access.
2. The Roslyn `project.AddDocument(name, content)` call ultimately contributes to a filesystem write when the user applies the fix in a project-backed IDE workspace (Visual Studio / Rider), consistent with the Roslyn workspace model documentation.
3. `diagnostic.GetMessage()` returns the English-language message in the test and development context, since no resource-file-based localization is implemented. In a localized IDE environment, the message format could differ, further amplifying SEC-001.
4. No additional `.nupkg` signing infrastructure (e.g., Azure Key Vault signing in CI) exists beyond what is visible in the workspace files.
5. The `C:\Nuget` and `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\` sources visible in the `dotnet list package` output are local machine sources not relevant to the production build pipeline.
