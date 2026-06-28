using Csharpsanitizer.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpsanitizer.CodeSanitizer
{
    /// <summary>
    /// Sanitizes .cs files using the Roslyn AST: rewrites suspicious string literals
    /// without touching the syntactic structure of the file (doesn't break anything).
    /// After this structural pass, it also applies regex rules to the
    /// result, to cover what remained as plain text.
    /// </summary>
    public static class CSharpSanitizer
    {
        public static string Sanitize(string sourceCode, ReplacementMap map)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            var rewriter = new SensitiveLiteralRewriter(map);
            var newRoot = rewriter.Visit(root);

            // Second pass: regex over the already rewritten text (covers comments,
            // strings that Roslyn didn't mark as "obviously sensitive" by their own pattern, etc.)
            return SanitizationRules.ApplyRegexRules(newRoot.ToFullString(), map);
        }

        private sealed class SensitiveLiteralRewriter : CSharpSyntaxRewriter
        {
            private readonly ReplacementMap _map;

            public SensitiveLiteralRewriter(ReplacementMap map) => _map = map;

            public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (!node.IsKind(SyntaxKind.StringLiteralExpression))
                    return base.VisitLiteralExpression(node);

                var value = node.Token.ValueText;

                // Simple heuristic: if the literal "looks" sensitive according to the rules,
                // we replace it right here (keeps the literal as a valid string).
                foreach (var rule in SanitizationRules.Rules)
                {
                    if (rule.Pattern.IsMatch(value))
                    {
                        if (DesanitizationRules.IsExcluded(value))
                            return base.VisitLiteralExpression(node);

                        var placeholder = _map.GetOrCreatePlaceholder(value, rule.Category);
                        var newLiteral = SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(placeholder))
                            .WithTriviaFrom(node);
                        return newLiteral;
                    }
                }

                return base.VisitLiteralExpression(node);
            }
        }
    }
}
