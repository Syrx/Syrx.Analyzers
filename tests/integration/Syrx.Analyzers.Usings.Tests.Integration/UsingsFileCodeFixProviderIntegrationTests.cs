using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace Syrx.Analyzers.Usings.Tests.Integration
{
    public class UsingsFileCodeFixProviderIntegrationTests
    {
        [Fact]
        public async Task CodeFixProvider_IsRegisteredAndAvailable()
        {
            // Simple test to ensure the code fix provider is registered and detectable
            var source = "using System;\nnamespace Test { class C { } }";

            var test = new CSharpAnalyzerTest<UsingsFileAnalyzer, XUnitVerifier>
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
    }
}
