// Roslyn Analyzer to enforce all using statements are placed in a specific file (default: Usings.cs)
// Configuration: .editorconfig key 'usings_file_name' can override the default file name

namespace Syrx.Analyzers.Usings
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UsingsFileAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "USINGS001";
        private const string Category = "Style";
        private static readonly LocalizableString Title = "Using statements must be in the designated file";
        private static readonly LocalizableString MessageFormat = "Move using statements to '{0}'";
        private static readonly LocalizableString Description = "All using statements should be placed in the designated file (default: Usings.cs).";
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
        }

        private void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
        {
            var root = context.Tree.GetRoot(context.CancellationToken);
            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            if (!usings.Any()) return;

            var fileName = context.Tree.FilePath != null ? Path.GetFileName(context.Tree.FilePath) : "";
            var configFileName = GetConfiguredUsingsFileName(context.Options, context.Tree);
            var targetFileName = string.IsNullOrWhiteSpace(configFileName) ? "Usings.cs" : configFileName;

            if (!fileName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var usingDirective in usings)
                {
                    var diagnostic = Diagnostic.Create(Rule, usingDirective.GetLocation(), targetFileName);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private string GetConfiguredUsingsFileName(AnalyzerOptions options, SyntaxTree tree)
        {
            var configOptions = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
            
            // Try both keys for compatibility
            if (configOptions.TryGetValue("dotnet_usings_file_name", out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
            if (configOptions.TryGetValue("usings_file_name", out var value2) && !string.IsNullOrWhiteSpace(value2))
            {
                return value2.Trim();
            }
            return null;
        }
    }
}
