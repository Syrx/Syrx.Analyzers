using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using Xunit;

namespace Syrx.Analyzers.Usings.Tests.Unit
{
    /// <summary>
    /// Alternative test approach using manual configuration injection
    /// </summary>
    public class ManualConfigAnalyzerTests
    {
        private static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsWithManualConfigAsync(
            string source, 
            DiagnosticAnalyzer analyzer, 
            Dictionary<string, string>? configOptions = null)
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var workspace = new AdhocWorkspace();
            var solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
                .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddDocument(documentId, "TestFile.cs", SourceText.From(source));

            var project = solution.GetProject(projectId)!;
            var compilation = await project.GetCompilationAsync();

            // Create custom analyzer options if config is provided
            AnalyzerOptions? analyzerOptions = null;
            if (configOptions != null)
            {
                var configProvider = new TestAnalyzerConfigOptionsProvider(configOptions);
                analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, configProvider);
            }

            var compilationWithAnalyzers = compilation!.WithAnalyzers(
                ImmutableArray.Create(analyzer), 
                analyzerOptions);
            
            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        [Fact]
        public async Task RespectsCustomUsingsFileName()
        {
            var source = "using System;\nusing static System.Math;\nnamespace Test { class C { } }";
            var configOptions = new Dictionary<string, string>
            {
                ["dotnet_usings_file_name"] = "CustomUsings.cs"
            };

            var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, new UsingsFileAnalyzer(), configOptions);
            
            Assert.Equal(2, diagnostics.Count);
            Assert.All(diagnostics, d =>
            {
                Assert.Equal(UsingsFileAnalyzer.DiagnosticId, d.Id);
                Assert.Contains("CustomUsings.cs", d.GetMessage());
            });
        }

        [Fact]
        public async Task RespectsAlternativeConfigKey()
        {
            var source = "using System;\nusing static System.Math;\nnamespace Test { class C { } }";
            var configOptions = new Dictionary<string, string>
            {
                ["usings_file_name"] = "AlternativeUsings.cs"
            };

            var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, new UsingsFileAnalyzer(), configOptions);
            
            Assert.Equal(2, diagnostics.Count);
            Assert.All(diagnostics, d =>
            {
                Assert.Equal(UsingsFileAnalyzer.DiagnosticId, d.Id);
                Assert.Contains("AlternativeUsings.cs", d.GetMessage());
            });
        }

        [Fact]
        public async Task PrefersFirst_DotNetPrefixKey()
        {
            var source = "using System;\nnamespace Test { class C { } }";
            var configOptions = new Dictionary<string, string>
            {
                ["dotnet_usings_file_name"] = "PrimaryUsings.cs",
                ["usings_file_name"] = "SecondaryUsings.cs"
            };

            var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, new UsingsFileAnalyzer(), configOptions);
            
            Assert.Single(diagnostics);
            Assert.Contains("PrimaryUsings.cs", diagnostics.First().GetMessage());
        }

        [Fact]
        public async Task UsesDefaultWhenNoConfig()
        {
            var source = "using System;\nnamespace Test { class C { } }";

            var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, new UsingsFileAnalyzer());
            
            Assert.Single(diagnostics);
            Assert.Contains("Usings.cs", diagnostics.First().GetMessage());
        }

        [Fact]
        public async Task IgnoresWhitespaceOnlyValues()
        {
            var source = "using System;\nnamespace Test { class C { } }";
            var configOptions = new Dictionary<string, string>
            {
                ["dotnet_usings_file_name"] = "   ",
                ["usings_file_name"] = "ValidUsings.cs"
            };

            var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, new UsingsFileAnalyzer(), configOptions);
            
            Assert.Single(diagnostics);
            Assert.Contains("ValidUsings.cs", diagnostics.First().GetMessage());
        }

        [Fact]
        public async Task TrimsConfigValues()
        {
            var source = "using System;\nnamespace Test { class C { } }";
            var configOptions = new Dictionary<string, string>
            {
                ["dotnet_usings_file_name"] = "  TrimmedUsings.cs  "
            };

            var diagnostics = await GetDiagnosticsWithManualConfigAsync(source, new UsingsFileAnalyzer(), configOptions);
            
            Assert.Single(diagnostics);
            Assert.Contains("TrimmedUsings.cs", diagnostics.First().GetMessage());
        }
    }

    /// <summary>
    /// Test implementation of AnalyzerConfigOptionsProvider for manual config injection
    /// </summary>
    internal class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly Dictionary<string, string> _globalOptions;

        public TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions)
        {
            _globalOptions = globalOptions ?? new Dictionary<string, string>();
        }

        public override AnalyzerConfigOptions GlobalOptions => new TestAnalyzerConfigOptions(_globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new TestAnalyzerConfigOptions(_globalOptions);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new TestAnalyzerConfigOptions(_globalOptions);
    }

    /// <summary>
    /// Test implementation of AnalyzerConfigOptions for manual config injection
    /// </summary>
    internal class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _options;

        public TestAnalyzerConfigOptions(Dictionary<string, string> options)
        {
            _options = options ?? new Dictionary<string, string>();
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _options.TryGetValue(key, out value!);
        }
    }
}