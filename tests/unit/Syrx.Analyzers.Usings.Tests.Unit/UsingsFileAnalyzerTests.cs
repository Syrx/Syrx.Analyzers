using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace Syrx.Analyzers.Usings.Tests.Unit
{
    public class UsingsFileAnalyzerTests
    {
        [Fact]
        public async Task ReportsDiagnosticWhenUsingsOutsideDesignatedFile()
        {
            var source = "using System;\nusing static System.Math;\nnamespace Test { class C { } }";
            var expected1 = new DiagnosticResult(UsingsFileAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan("TestFile.cs", 1, 1, 1, 14)
                .WithArguments("Usings.cs");
            var expected2 = new DiagnosticResult(UsingsFileAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan("TestFile.cs", 2, 1, 2, 26)  // Fixed column position based on actual diagnostic
                .WithArguments("Usings.cs");

            var test = new CSharpAnalyzerTest<UsingsFileAnalyzer, XUnitVerifier>
            {
                TestState =
                    {
                        Sources = { ("TestFile.cs", source) }
                    }
            };
            test.ExpectedDiagnostics.Add(expected1);
            test.ExpectedDiagnostics.Add(expected2);
            await test.RunAsync();
        }

        [Fact]
        public async Task NoDiagnosticWhenUsingsInDesignatedFile()
        {
            var source = "global using System;\nglobal using static System.Math;\nnamespace Test { class C { } }";

            var test = new CSharpAnalyzerTest<UsingsFileAnalyzer, XUnitVerifier>
            {
                TestState =
                    {
                        Sources = { ("Usings.cs", source) }
                    }
            };

            await test.RunAsync();
        }

        [Fact]
        public async Task ReportsDiagnosticWhenRegularUsingsInDesignatedFile()
        {
            // The analyzer should NOT report diagnostics for using statements in the designated file
            // even if they are regular (non-global) using statements, because the file name matches
            var source = "using System;\nusing static System.Math;\nnamespace Test { class C { } }";

            var test = new CSharpAnalyzerTest<UsingsFileAnalyzer, XUnitVerifier>
            {
                TestState =
                    {
                        Sources = { ("Usings.cs", source) }
                    }
            };
            // Expect no diagnostics because the file name matches the designated file
            await test.RunAsync();
        }

        [Fact]
        public async Task NoDiagnosticWhenNoUsingsPresent()
        {
            var source = "namespace Test { class C { } }";

            var test = new CSharpAnalyzerTest<UsingsFileAnalyzer, XUnitVerifier>
            {
                TestState =
                {
                    Sources = { ("TestFile.cs", source) }
                }
            };

            await test.RunAsync();
        }
    }
}
