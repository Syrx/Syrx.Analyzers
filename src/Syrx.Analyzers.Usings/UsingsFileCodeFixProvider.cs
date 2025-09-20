// Stub for future code fix provider
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace Syrx.Analyzers.Usings
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UsingsFileCodeFixProvider)), Shared]
    public class UsingsFileCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(UsingsFileAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                context.RegisterCodeFix(
                    Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                        title: "Move using statements to designated file",
                        createChangedSolution: async c =>
                        {
                            var document = context.Document;
                            var root = await document.GetSyntaxRootAsync(c).ConfigureAwait(false);
                            if (root == null) return document.Project.Solution;

                            // Find all using directives in the document
                            var usings = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>().ToList();
                            if (!usings.Any()) return document.Project.Solution;

                            // Remove using directives from current document
                            var newRoot = root.RemoveNodes(usings, Microsoft.CodeAnalysis.SyntaxRemoveOptions.KeepNoTrivia);
                            if (newRoot == null) return document.Project.Solution;
                            var newDocument = document.WithSyntaxRoot(newRoot);

                            // Determine target file name from diagnostic message
                            var targetFileName = diagnostic.GetMessage().Split('\'').Skip(1).FirstOrDefault() ?? "Usings.cs";
                            var project = document.Project;
                            var designatedDoc = project.Documents.FirstOrDefault(d => 
                                !string.IsNullOrEmpty(d.FilePath) && 
                                System.IO.Path.GetFileName(d.FilePath).Equals(targetFileName, System.StringComparison.OrdinalIgnoreCase));

                            if (designatedDoc == null)
                            {
                                // Create new designated file with global using statements
                                var usingsText = string.Join("\r\n", usings.Select(u => $"global {u.ToFullString().Trim()}"));
                                var newDoc = project.AddDocument(targetFileName, usingsText);
                                return newDoc.Project.Solution.WithDocumentSyntaxRoot(newDocument.Id, newRoot);
                            }
                            else
                            {
                                // Append global using statements to existing designated file
                                var designatedRoot = await designatedDoc.GetSyntaxRootAsync(c).ConfigureAwait(false);
                                var designatedUsings = designatedRoot?.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>() ?? Enumerable.Empty<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
                                var globalUsings = usings.Select(u => 
                                {
                                    var name = u.Name;
                                    if (name == null) return null;
                                    return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.UsingDirective(name)
                                        .WithGlobalKeyword(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GlobalKeyword))
                                        .WithStaticKeyword(u.StaticKeyword);
                                }).Where(u => u != null).Cast<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
                                var combinedUsings = designatedUsings.Concat(globalUsings).Distinct(new UsingDirectiveComparer());
                                var newDesignatedRoot = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.CompilationUnit().WithUsings(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.List(combinedUsings)).NormalizeWhitespace();
                                var updatedDesignatedDoc = designatedDoc.WithSyntaxRoot(newDesignatedRoot);
                                var solution = updatedDesignatedDoc.Project.Solution.WithDocumentSyntaxRoot(newDocument.Id, newRoot);
                                solution = solution.WithDocumentSyntaxRoot(updatedDesignatedDoc.Id, newDesignatedRoot);
                                return solution;
                            }
                        },
                        equivalenceKey: "MoveUsingsToDesignatedFile"),
                    diagnostic);
            }
        }

        // Helper comparer to avoid duplicate using directives
        private class UsingDirectiveComparer : IEqualityComparer<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>
        {
            public bool Equals(Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax x, Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return x.ToString() == y.ToString();
            }
            public int GetHashCode(Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax obj)
            {
                return obj?.ToString()?.GetHashCode() ?? 0;
            }
        }
    }
}
