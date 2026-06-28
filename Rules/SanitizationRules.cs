using Csharpsanitizer.CodeSanitizer;
using System.Text.RegularExpressions;

namespace Csharpsanitizer.Rules
{
    /// <summary>
    /// Reglas basadas en regex para texto plano (json, config, yml, appsettings, etc.)
    /// y como fallback dentro de literales de C# que Roslyn ya aisló.
    /// Ajustá/agregá patrones según lo que sea sensible en tu organización
    /// (ej. nombres de servidores internos, prefijos de DB, dominios).
    /// </summary>
    public static class SanitizationRules
    {
        public sealed record Rule(string Category, Regex Pattern);

        public static readonly List<Rule> Rules = new()
        {
            // Connection strings completas (SQL Server, etc.)
            new("CONNECTION_STRING", new Regex(
                @"(Server|Data Source)\s*=\s*[^;""']+;.*?(Password|Pwd)\s*=\s*[^;""']+;?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),

            // Password=algo; / Pwd=algo; sueltos
            new("PASSWORD", new Regex(
                @"(Password|Pwd)\s*=\s*[^;""'\s]+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),

            // API keys estilo OpenAI/Anthropic/genéricas largas alfanuméricas
            new("API_KEY", new Regex(
                @"\b(sk-[a-zA-Z0-9]{20,}|AKIA[0-9A-Z]{16}|[a-zA-Z0-9_\-]{32,})\b",
                RegexOptions.Compiled)),

            // JWT (3 segmentos base64 separados por puntos)
            new("JWT", new Regex(
                @"\beyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+\b",
                RegexOptions.Compiled)),

            // Emails
            new("EMAIL", new Regex(
                @"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b",
                RegexOptions.Compiled)),

            // IPs internas (rangos privados típicos)
            new("INTERNAL_IP", new Regex(
                @"\b(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b",
                RegexOptions.Compiled)),

            // GUIDs (a veces son tenant IDs / client IDs sensibles; comentá si no aplica)
            new("GUID", new Regex(
                @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
                RegexOptions.Compiled)),

            // URLs de Asset Bundles / CDN internos de Unity (StreamingAssets remotos,
            // servidores propios de distribución de bundles, etc.). Ajustá el dominio
            // según tu infraestructura real (ej. "assets.miempresa.com", IPs propias).
            new("ASSET_BUNDLE_URL", new Regex(
                @"https?:\/\/[^\s""'<>]+\.(unity3d|bundle)(\?[^\s""'<>]*)?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),

            // URLs que apuntan a rutas típicas de asset bundles propios, sin importar
            // la extensión (ej. https://miservidor.interno/assetbundles/nivel1/...).
            new("ASSET_BUNDLE_URL", new Regex(
                @"https?:\/\/[^\s""'<>]+\/(assetbundle|asset-bundle|bundles)\/[^\s""'<>]*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        };

        /// <summary>
        /// Agregá aquí nombres propios de tu organización que quieras anonimizar
        /// (nombres de clientes, dominios internos, nombres de proyectos confidenciales).
        /// Coincidencia exacta de palabra, case-insensitive.
        /// </summary>
        public static readonly List<string> CustomTerms = new()
        {
            // "NombreClienteX",
            // "miempresa.com.ar",
            // "ProyectoConfidencial",
        };

        public static string ApplyRegexRules(string text, ReplacementMap map)
        {
            foreach (var rule in Rules)
            {
                text = rule.Pattern.Replace(text, m =>
                {
                    if (DesanitizationRules.IsExcluded(m.Value))
                        return m.Value;

                    return map.GetOrCreatePlaceholder(m.Value, rule.Category);
                });
            }

            foreach (var term in CustomTerms)
            {
                var pattern = new Regex(Regex.Escape(term), RegexOptions.IgnoreCase);
                text = pattern.Replace(text, m =>
                {
                    if (DesanitizationRules.IsExcluded(m.Value))
                        return m.Value;

                    return map.GetOrCreatePlaceholder(m.Value, "CUSTOM_TERM");
                });
            }

            return text;
        }
    }
}
