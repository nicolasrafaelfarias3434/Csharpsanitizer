using Csharpsanitizer.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Csharpsanitizer.CodeSanitizer
{
    /// <summary>
    /// Sanitiza archivos .cs usando el AST de Roslyn: reescribe literales de string
    /// sospechosos sin tocar la estructura sintáctica del archivo (no rompe nada).
    /// Después de esta pasada estructural, igual corre las reglas regex sobre el
    /// resultado, para cubrir lo que quedó como texto plano.
    /// </summary>
    public static class CSharpSanitizer
    {
        public static string Sanitize(string sourceCode, ReplacementMap map)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            var rewriter = new SensitiveLiteralRewriter(map);
            var newRoot = rewriter.Visit(root);

            // Segunda pasada: regex sobre el texto ya reescrito (cubre comentarios,
            // strings que Roslyn no marcó como "obviamente sensibles" por patrón propio, etc.)
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

                // Heurística simple: si el literal "parece" sensible según las reglas,
                // lo reemplazamos directo acá (mantiene el literal como string válido).
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
