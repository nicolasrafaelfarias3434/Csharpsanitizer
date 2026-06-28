namespace Csharpsanitizer.CodeSanitizer
{
    /// <summary>
    /// Replacement map: stores the original value -> placeholder mapping, and vice versa,
    /// to be able to "desanonymize" if needed to map names back.
    /// Lives only in memory + is dumped to a LOCAL JSON file (never sent anywhere).
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
        /// Loads a previously saved map with SaveTo, returning the inverse dictionary
        /// (placeholder -> original value) ready to restore a text.
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