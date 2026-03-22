using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Syrx.Analyzers.Usings.Tests.Integration
{
    public class UsingsFileCodeFixProviderIntegrationTests
    {
        [Fact]
        public async Task CodeFixProvider_IsRegisteredAndAvailable()
        {
            // Simple test to ensure the code fix provider is registered and detectable
            var source = "using System;\nnamespace Test { class C { } }";

            var test = new CSharpAnalyzerTest<UsingsFileAnalyzer, DefaultVerifier>
            {
                TestState =
                {
                    Sources = { ("TestFile.cs", source) }
                }
            };

            test.ExpectedDiagnostics.Add(
                new Microsoft.CodeAnalysis.Testing.DiagnosticResult(UsingsFileAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .WithSpan("TestFile.cs", 1, 1, 1, 14)
                    .WithArguments("Usings.cs"));

            await test.RunAsync();
        }

        [Fact]
        public async Task CodeFix_DoesNotMoveFileHeaderCommentsToUsingsFile()
        {
            var source = "// Header comment\nusing System;\n\nnamespace Test { class C { } }";

            var expectedDiagnostic = new DiagnosticResult(UsingsFileAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan("TestFile.cs", 2, 1, 2, 14)
                .WithArguments("Usings.cs");

            var fixedSource = "// Header comment\n\n\nnamespace Test { class C { } }";
            var expectedUsingsFile = "global using System;";

            var test = new CSharpCodeFixTest<UsingsFileAnalyzer, UsingsFileCodeFixProvider, DefaultVerifier>
            {
                TestState =
                {
                    Sources = { ("TestFile.cs", source) }
                },
                FixedState =
                {
                    Sources =
                    {
                        ("TestFile.cs", fixedSource),
                        ("Usings.cs", expectedUsingsFile)
                    }
                },
                CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
            };

            test.ExpectedDiagnostics.Add(expectedDiagnostic);

            await test.RunAsync();
        }
    }
}
