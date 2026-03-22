namespace Syrx.Analyzers.Usings
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UsingsFileCodeFixProvider)), Shared]
    public class UsingsFileCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(UsingsFileAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override Task RegisterCodeFixesAsync(CodeFixContext context)
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

                            var globalUsings = usings
                                .Select(ToGlobalUsingDirective)
                                .Where(u => u != null)
                                .Cast<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();

                            // Remove using directives from current document
                            var newRoot = root.RemoveNodes(usings, Microsoft.CodeAnalysis.SyntaxRemoveOptions.KeepExteriorTrivia);
                            if (newRoot == null) return document.Project.Solution;
                            var newDocument = document.WithSyntaxRoot(newRoot);

                            // Determine target file name from diagnostic properties
                            var targetFileName = diagnostic.Properties.TryGetValue("TargetFileName", out var fn) && !string.IsNullOrWhiteSpace(fn)
                                ? fn
                                : "Usings.cs";
                            targetFileName = Path.GetFileName(targetFileName);
                            if (string.IsNullOrWhiteSpace(targetFileName)) targetFileName = "Usings.cs";
                            var project = document.Project;
                            var designatedDoc = project.Documents.FirstOrDefault(d => 
                                !string.IsNullOrEmpty(d.FilePath) && 
                                System.IO.Path.GetFileName(d.FilePath).Equals(targetFileName, System.StringComparison.OrdinalIgnoreCase));

                            if (designatedDoc == null)
                            {
                                // Create new designated file with global using statements
                                var newDesignatedRoot = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.CompilationUnit()
                                    .WithUsings(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.List(globalUsings.Distinct(new UsingDirectiveComparer())))
                                    .NormalizeWhitespace();
                                var newDoc = project.AddDocument(targetFileName, newDesignatedRoot);
                                return newDoc.Project.Solution.WithDocumentSyntaxRoot(newDocument.Id, newRoot);
                            }
                            else
                            {
                                // Append global using statements to existing designated file
                                var designatedRoot = await designatedDoc.GetSyntaxRootAsync(c).ConfigureAwait(false);
                                var designatedUsings = designatedRoot?.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>() ?? Enumerable.Empty<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
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

            return Task.CompletedTask;
        }

        private static Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax? ToGlobalUsingDirective(Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax usingDirective)
        {
            var name = usingDirective.Name;
            if (name == null) return null;

            var globalUsing = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.UsingDirective(name.WithoutTrivia())
                .WithGlobalKeyword(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GlobalKeyword));

            if (usingDirective.Alias != null)
            {
                globalUsing = globalUsing.WithAlias(usingDirective.Alias.WithoutTrivia());
            }

            if (usingDirective.StaticKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
            {
                globalUsing = globalUsing.WithStaticKeyword(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword));
            }

            return globalUsing;
        }

        // Helper comparer to avoid duplicate using directives
        private class UsingDirectiveComparer : IEqualityComparer<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>
        {
            public bool Equals(Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax x, Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return x.Name?.ToFullString().Trim() == y.Name?.ToFullString().Trim();
            }
            public int GetHashCode(Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax obj)
            {
                return obj?.Name?.ToFullString().Trim()?.GetHashCode() ?? 0;
            }
        }
    }
}
