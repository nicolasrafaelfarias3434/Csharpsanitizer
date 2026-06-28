using Csharpsanitizer.CodeSanitizer;
using System.Text.RegularExpressions;

namespace Csharpsanitizer.Rules
{
    /// <summary>
    /// Regex based rules for plain text (json, config, yml, appsettings, etc.)
    /// and as fallback inside C# literals that Roslyn already isolated.
    /// Adjust/add patterns due to your own internal naming conventions and sensitive data patterns
    /// (e.g. internal server names, DB prefixes, domains).
    /// </summary>
    public static class SanitizationRules
    {
        public sealed record Rule(string Category, Regex Pattern);

        public static readonly List<Rule> Rules = new()
        {
            // Full connection strings (SQL Server, etc.)
            new("CONNECTION_STRING", new Regex(
                @"(Server|Data Source)\s*=\s*[^;""']+;.*?(Password|Pwd)\s*=\s*[^;""']+;?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),

            // Password=something; / Pwd=something; others
            new("PASSWORD", new Regex(
                @"(Password|Pwd)\s*=\s*[^;""'\s]+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),

            // API keys style OpenAI/Anthropic/generic long alphanumeric tokens (sk-xxxx, AKIAxxxx, etc.)
            new("API_KEY", new Regex(
                @"\b(sk-[a-zA-Z0-9]{20,}|AKIA[0-9A-Z]{16}|[a-zA-Z0-9_\-]{32,})\b",
                RegexOptions.Compiled)),

            // JWT (3 base64 segments dot separated)
            new("JWT", new Regex(
                @"\beyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+\b",
                RegexOptions.Compiled)),

            // Emails
            new("EMAIL", new Regex(
                @"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b",
                RegexOptions.Compiled)),

            // Internal IPs (typical private ranges)
            new("INTERNAL_IP", new Regex(
                @"\b(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b",
                RegexOptions.Compiled)),

            // GUIDs (sometimes they are tenant IDs / client IDs sensitive; comment out if not applicable)
            new("GUID", new Regex(
                @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
                RegexOptions.Compiled)),

            // URLs of Asset Bundles / CDN internals of Unity (remote StreamingAssets,
            // distributed own bundles servers, etc.). Adjust domain according to your
            // real infrastructure (e.g. "assets.mycompany.com", own IPs).
            new("ASSET_BUNDLE_URL", new Regex(
                @"https?:\/\/[^\s""'<>]+\.(unity3d|bundle)(\?[^\s""'<>]*)?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),

            // Tipical URLs that point to typical asset bundle paths, regardless of extension
            // (e.g., https://myserver.internal/assetbundles/level1/...).
            new("ASSET_BUNDLE_URL", new Regex(
                @"https?:\/\/[^\s""'<>]+\/(assetbundle|asset-bundle|bundles)\/[^\s""'<>]*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        };

        /// <summary>
        /// Add here your organization's specific terms that you want to anonymize
        /// (client names, internal domains, confidential project names).
        /// Exact word match, case-insensitive.
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
