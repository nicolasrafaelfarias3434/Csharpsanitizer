using System.Text.RegularExpressions;

namespace Csharpsanitizer.Rules
{
    /// <summary>
    /// Lista de excepciones: valores que NUNCA se deben anonimizar, aunque matcheen
    /// alguna regla de SanitizationRules (ej. URLs públicas conocidas, dominios propios
    /// que sabés que no son sensibles, GUIDs de librerías de terceros, etc.).
    /// Coincidencia exacta de texto (case-insensitive) o por patrón regex, a elección.
    /// </summary>
    public static class DesanitizationRules
    {
        /// <summary>
        /// Términos exactos a excluir (case-insensitive). Ej: "https://cdn.unity3d.com",
        /// "contacto@miempresa.com" si ese mail ya es público, etc.
        /// </summary>
        public static readonly List<string> ExactExclusions = new()
        {
            // "https://cdn.unity3d.com",
            // "noreply@miempresa.com",
        };

        /// <summary>
        /// Patrones regex a excluir. Si un valor matchea alguno de estos, no se anonimiza
        /// aunque también matchee una regla de sanitización. Útil para excluir dominios
        /// enteros o rangos conocidos como "no sensibles".
        /// </summary>
        public static readonly List<Regex> PatternExclusions = new()
        {
            // new Regex(@"^https://.*\.unity3d\.com/.*$", RegexOptions.IgnoreCase),
            // new Regex(@".*@miempresa\.com$", RegexOptions.IgnoreCase),
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
