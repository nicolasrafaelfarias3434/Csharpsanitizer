using Csharpsanitizer.CodeSanitizer;
using Csharpsanitizer.Rules;

namespace CodeSanitizer
{

    /// <summary>
    /// Usage:
    ///   Sanitize (hides sensitive values):
    ///     dotnet run -- sanitize --input ./MyProject
    ///     dotnet run -- --input ./MyFile.cs                 (sanitize is the default command)
    ///     (--output is optional; by default uses "<input>_sanitized" next to the input)
    ///
    ///   Restore (reverts placeholders to original values, e.g., on the
    ///   AI response you pasted into a file):
    ///     dotnet run -- restore --input ./MyFile.cs --map ./MyProject_sanitized/_replacement-map.txt
    ///     (--output is optional; by default uses "<input>_restored" next to the input)
    ///
    /// The replacement map is stored in "<output>/_replacement-map.txt" (DO NOT upload this file anywhere,
    /// it is only for you to reconstruct names if you need to interpret the AI's response).
    /// </summary>
    public static class Program
    {
        // Extensions that we treat as plain text + regex rules.
        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".config", ".xml", ".yml", ".yaml", ".txt", ".md", ".env", ".ini"
    };

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            // The first argument can be the command ("sanitize"/"restore"). If it's not,
            // (e.g., it starts with "--"), we assume "sanitize" to avoid breaking the previous usage.
            var command = args[0].StartsWith("-") ? "sanitize" : args[0].ToLowerInvariant();
            var rest = args[0].StartsWith("-") ? args : args.Skip(1).ToArray();

            return command switch
            {
                "sanitize" => RunSanitize(rest),
                "restore" => RunRestore(rest),
                _ => Fail($"Unknown command: {command}")
            };
        }

        private static int Fail(string message)
        {
            Console.WriteLine(message);
            PrintUsage();
            return 1;
        }

        private static int RunSanitize(string[] args)
        {
            var options = ParseSanitizeArgs(args);
            if (options is null)
                return Fail("Missing arguments for 'sanitize'.");

            if (!File.Exists(options.Input) && !Directory.Exists(options.Input))
            {
                Console.WriteLine($"Input path not found: {options.Input}");
                return 1;
            }

            var output = options.Output ?? DefaultSuffixedOutput(options.Input, "sanitized");
            Directory.CreateDirectory(output);
            var map = new ReplacementMap();

            var filesToProcess = File.Exists(options.Input)
                ? new[] { options.Input }
                : Directory.EnumerateFiles(options.Input, "*", SearchOption.AllDirectories).ToArray();

            var inputRoot = File.Exists(options.Input)
                ? Path.GetDirectoryName(Path.GetFullPath(options.Input))!
                : Path.GetFullPath(options.Input);

            int processed = 0;
            foreach (var file in filesToProcess)
            {
                var ext = Path.GetExtension(file);
                var relative = Path.GetRelativePath(inputRoot, file);
                var destPath = Path.Combine(output, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var source = File.ReadAllText(file);
                    var sanitized = CSharpSanitizer.Sanitize(source, map);
                    File.WriteAllText(destPath, sanitized);
                    processed++;
                }
                else if (TextExtensions.Contains(ext))
                {
                    var content = File.ReadAllText(file);
                    var sanitized = SanitizationRules.ApplyRegexRules(content, map);
                    File.WriteAllText(destPath, sanitized);
                    processed++;
                }
                else
                {
                    // Binaries and others: copied as it is (no sanitization).
                    File.Copy(file, destPath, overwrite: true);
                }
            }

            var mapPath = Path.Combine(output, "_replacement-map.txt");
            map.SaveTo(mapPath);

            Console.WriteLine($"Processed {processed} text/code file(s).");
            Console.WriteLine($"Sanitized output in: {Path.GetFullPath(output)}");
            Console.WriteLine($"Replacement map (DO NOT SHARE) in: {mapPath}");
            Console.WriteLine($"Total replacements: {map.Entries.Count}");

            return 0;
        }

        private static int RunRestore(string[] args)
        {
            var options = ParseRestoreArgs(args);
            if (options is null)
                return Fail("Missing arguments for 'restore'. You need --input, --map and optionally --output.");

            if (!File.Exists(options.Map))
            {
                Console.WriteLine($"File not found: {options.Map}");
                return 1;
            }

            if (!File.Exists(options.Input) && !Directory.Exists(options.Input))
            {
                Console.WriteLine($"Input path not found: {options.Input}");
                return 1;
            }

            var placeholderToOriginal = ReplacementMap.LoadPlaceholderToOriginal(options.Map);
            if (placeholderToOriginal.Count == 0)
            {
                Console.WriteLine("The map is empty, there is nothing to restore.");
                return 1;
            }

            var output = options.Output ?? DefaultSuffixedOutput(options.Input, "restored");

            if (File.Exists(options.Input))
            {
                RestoreSingleFile(options.Input, output, placeholderToOriginal);
                Console.WriteLine($"Restored: {Path.GetFullPath(output)}");
            }
            else
            {
                Directory.CreateDirectory(output);
                var inputRoot = Path.GetFullPath(options.Input);
                var files = Directory.EnumerateFiles(options.Input, "*", SearchOption.AllDirectories);
                int restored = 0;

                foreach (var file in files)
                {
                    var relative = Path.GetRelativePath(inputRoot, file);
                    var destPath = Path.Combine(output, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    RestoreSingleFile(file, destPath, placeholderToOriginal);
                    restored++;
                }

                Console.WriteLine($"Restored {restored} file(s) in: {Path.GetFullPath(output)}");
            }

            return 0;
        }

        private static void RestoreSingleFile(string sourcePath, string destPath, Dictionary<string, string> placeholderToOriginal)
        {
            var content = File.ReadAllText(sourcePath);

            foreach (var (placeholder, original) in placeholderToOriginal)
            {
                content = content.Replace(placeholder, original);
            }

            File.WriteAllText(destPath, content);
        }

        /// <summary>
        /// Calculates the default output path by adding a suffix to the input name,
        /// keeping it as a sibling (same parent directory) instead of nesting it
        /// within a fixed folder. This avoids having it inside the project
        /// (and thus no need to exclude it when compiling/executing).
        ///   File:  ./MyFile.cs        -> ./MyFile_sanitized.cs
        ///   Folder:  ./MyProject          -> ./MyProject_sanitized
        /// </summary>
        private static string DefaultSuffixedOutput(string input, string suffix)
        {
            if (File.Exists(input))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
                var name = Path.GetFileNameWithoutExtension(input);
                var ext = Path.GetExtension(input);
                return Path.Combine(dir, $"{name}_{suffix}{ext}");
            }

            var fullInput = Path.GetFullPath(input.TrimEnd('/', '\\'));
            var parent = Path.GetDirectoryName(fullInput) ?? ".";
            var folderName = Path.GetFileName(fullInput);
            return Path.Combine(parent, $"{folderName}_{suffix}");
        }

        private sealed record SanitizeOptions(string Input, string? Output);
        private sealed record RestoreOptions(string Input, string Map, string? Output);

        private static SanitizeOptions? ParseSanitizeArgs(string[] args)
        {
            string? input = null;
            string? output = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--input":
                    case "-i":
                        if (i + 1 < args.Length) input = args[++i];
                        break;
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length) output = args[++i];
                        break;
                }
            }

            return input is null ? null : new SanitizeOptions(input, output);
        }

        private static RestoreOptions? ParseRestoreArgs(string[] args)
        {
            string? input = null;
            string? map = null;
            string? output = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--input":
                    case "-i":
                        if (i + 1 < args.Length) input = args[++i];
                        break;
                    case "--map":
                    case "-m":
                        if (i + 1 < args.Length) map = args[++i];
                        break;
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length) output = args[++i];
                        break;
                }
            }

            return (input is null || map is null) ? null : new RestoreOptions(input, map, output);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Sanitize:  dotnet run -- sanitize --input <file-or-folder> [--output <destination>]");
            Console.WriteLine("              (without --output, use '<input>_sanitized' next to input)");
            Console.WriteLine("  Restore:  dotnet run -- restore --input <file-or-folder> --map <_replacement-map.txt> [--output <destination>]");
            Console.WriteLine("              (without --output, use '<input>_restored' next to input)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- sanitize --input ./MiProject");
            Console.WriteLine("  dotnet run -- restore --input ./result-ia.cs --map ./MiProject_sanitized/_replacement-map.txt");
        }
    }
}