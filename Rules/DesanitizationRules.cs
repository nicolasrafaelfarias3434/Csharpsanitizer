using System.Text.RegularExpressions;

namespace Csharpsanitizer.Rules
{
    /// <summary>
    /// Exceptions List: values that should NEVER be sanitized, even if they match
    /// any sanitization rule (e.g., known public URLs, internal domains
    /// that you know are not sensitive, third-party library GUIDs, etc.).
    /// Exact text match (case-insensitive) or regex pattern match, at your choice.
    /// </summary>
    public static class DesanitizationRules
    {
        /// <summary>
        /// The exact terms to exclude (case-insensitive). Ej: "https://cdn.unity3d.com",
        /// "contact@micompany.com" if that email is already public, etc.
        /// </summary>
        public static readonly List<string> ExactExclusions = new()
        {
            // "https://cdn.unity3d.com",
            // "noreply@micompany.com",
        };

        /// <summary>
        /// Regex patterns to exclude. If a value matches any of these, it will not be sanitized
        /// even if it also matches a sanitization rule. Useful for excluding entire domains
        /// or known non-sensitive ranges.
        /// </summary>
        public static readonly List<Regex> PatternExclusions = new()
        {
            // new Regex(@"^https://.*\.unity3d\.com/.*$", RegexOptions.IgnoreCase),
            // new Regex(@".*@micompany\.com$", RegexOptions.IgnoreCase),
        };

        public static bool IsExcluded(string value)
        {
            foreach (var exact in ExactExclusions)
            {
                if (string.Equals(exact, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var pattern in PatternExclusions)
            {
                if (pattern.IsMatch(value))
                    return true;
            }

            return false;
        }
    }
}
