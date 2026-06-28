namespace Csharpsanitizer.CodeSanitizer
{
    /// <summary>
    /// Mapa de reemplazo: guarda el valor original -> placeholder, y al revés,
    /// para poder "desofuscar" si necesitás mapear nombres de vuelta.
    /// Vive solo en memoria + se vuelca a un JSON LOCAL (nunca se envía a ningún lado).
    /// </summary>
    public sealed class ReplacementMap
    {
        private readonly Dictionary<string, string> _originalToPlaceholder = new();
        private readonly Dictionary<string, int> _counters = new();

        public string GetOrCreatePlaceholder(string originalValue, string category)
        {
            if (_originalToPlaceholder.TryGetValue(originalValue, out var existing))
                return existing;

            _counters.TryGetValue(category, out var count);
            count++;
            _counters[category] = count;

            var placeholder = $"__{category}_{count}__";
            _originalToPlaceholder[originalValue] = placeholder;
            return placeholder;
        }

        public IReadOnlyDictionary<string, string> Entries => _originalToPlaceholder;

        public void SaveTo(string path)
        {
            var lines = _originalToPlaceholder.Select(kv => $"{kv.Value} => {kv.Key}");
            File.WriteAllLines(path, lines);
        }

        /// <summary>
        /// Carga un mapa previamente guardado con SaveTo, devolviendo el diccionario
        /// inverso (placeholder -> valor original) listo para restaurar un texto.
        /// </summary>
        public static Dictionary<string, string> LoadPlaceholderToOriginal(string path)
        {
            var result = new Dictionary<string, string>();

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var separatorIndex = line.IndexOf(" => ", StringComparison.Ordinal);
                if (separatorIndex < 0)
                    continue;

                var placeholder = line[..separatorIndex];
                var original = line[(separatorIndex + 4)..];
                result[placeholder] = original;
            }

            return result;
        }
    }
}