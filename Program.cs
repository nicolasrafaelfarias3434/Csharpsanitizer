using Csharpsanitizer.CodeSanitizer;
using Csharpsanitizer.Rules;

namespace CodeSanitizer
{
    /// <summary>
    /// Usage:
    ///   Sanitize (hides sensitive values):
    ///     dotnet run -- sanitize --input ./MyProject
    ///     dotnet run -- --input ./MyFile.cs                 (sanitize is the default command)
    ///     (--output is optional; defaults to "<input>_sanitized" next to the input)
    ///
    ///   Restore (reverts placeholders back to original values, e.g. on the
    ///   AI's response that you pasted into a file):
    ///     dotnet run -- restore --input ./ai-response.cs --map ./MyProject_sanitized/_replacement-map.txt
    ///     (--output is optional; defaults to "<input>_restored" next to the input)
    ///
    /// The replacement map is saved at "<output>/_replacement-map.txt" (NEVER upload it anywhere,
    /// it's only so you can reconstruct names if you need to interpret the AI's response).
    /// </summary>
    public static class Program
    {
        // Extensions treated as plain text + regex rules.
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

            // The first argument can be the command ("sanitize"/"restore"). If it isn't
            // (e.g. it starts with "--"), "sanitize" is assumed to keep prior usage working.
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

            if (!ConfirmOverwriteIfExists(output))
            {
                Console.WriteLine("Operation canceled by the user.");
                return 0;
            }

            CleanDestination(output);
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
                    // Binaries or other files: copied as-is (not sanitized).
                    File.Copy(file, destPath, overwrite: true);
                }
            }

            var mapPath = Path.Combine(output, "_replacement-map.txt");
            map.SaveTo(mapPath);

            Console.WriteLine($"Processed {processed} text/code file(s).");
            Console.WriteLine($"Sanitized output at: {Path.GetFullPath(output)}");
            Console.WriteLine($"Replacement map (do NOT share) at: {mapPath}");
            Console.WriteLine($"Total replacements: {map.Entries.Count}");

            return 0;
        }

        private static int RunRestore(string[] args)
        {
            var options = ParseRestoreArgs(args);
            if (options is null)
                return Fail("Missing arguments for 'restore'. You need --input, --map, and optionally --output.");

            if (!File.Exists(options.Map))
            {
                Console.WriteLine($"Map file not found: {options.Map}");
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

            if (!ConfirmOverwriteIfExists(output))
            {
                Console.WriteLine("Operation canceled by the user.");
                return 0;
            }

            CleanDestination(output);
            var foundPlaceholders = new HashSet<string>();

            if (File.Exists(options.Input))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                var found = RestoreSingleFile(options.Input, output, placeholderToOriginal);
                foundPlaceholders.UnionWith(found);
                Console.WriteLine($"Restored: {Path.GetFullPath(output)}");

                WriteUnresolvedReport(Path.GetDirectoryName(Path.GetFullPath(output))!, placeholderToOriginal, foundPlaceholders);
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
                    var found = RestoreSingleFile(file, destPath, placeholderToOriginal);
                    foundPlaceholders.UnionWith(found);
                    restored++;
                }

                Console.WriteLine($"Restored {restored} file(s) at: {Path.GetFullPath(output)}");

                WriteUnresolvedReport(output, placeholderToOriginal, foundPlaceholders);
            }

            return 0;
        }

        /// <summary>
        /// If the destination (file or folder) already exists, asks for confirmation
        /// ONCE before continuing. Returns true if it's safe to proceed (it didn't
        /// exist, or the user confirmed the overwrite), false if the user canceled.
        /// </summary>
        private static bool ConfirmOverwriteIfExists(string output)
        {
            var exists = File.Exists(output) || Directory.Exists(output);
            if (!exists)
                return true;

            Console.Write($"Destination '{Path.GetFullPath(output)}' already exists. Overwrite? (y/n): ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            return answer is "y" or "yes" or "s" or "si" or "sí";
        }

        /// <summary>
        /// Fully deletes the destination if it exists (file or folder, recursive),
        /// so the output reflects exactly the current state of the input and doesn't
        /// carry over orphaned files from a previous run (e.g. files an AI agent
        /// deleted between one run and the next).
        /// </summary>
        private static void CleanDestination(string output)
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
            else if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }

        /// <summary>
        /// Writes a "_unresolved-replacements.txt" report next to the restore output,
        /// listing every placeholder from the map that was NOT found in any processed
        /// file — so you know which ones, for whatever reason, couldn't be reverted
        /// to their original value (e.g. the AI reworded or removed that code).
        /// </summary>
        private static void WriteUnresolvedReport(string outputDir, Dictionary<string, string> placeholderToOriginal, HashSet<string> foundPlaceholders)
        {
            var unresolved = placeholderToOriginal
                .Where(kv => !foundPlaceholders.Contains(kv.Key))
                .ToList();

            var reportPath = Path.Combine(outputDir, "_unresolved-replacements.txt");

            if (unresolved.Count == 0)
            {
                // No leftovers from a previous run should linger if this run resolved everything.
                if (File.Exists(reportPath))
                    File.Delete(reportPath);

                Console.WriteLine("All placeholders from the map were resolved.");
                return;
            }

            var lines = unresolved.Select(kv => $"{kv.Key} => {kv.Value}");
            File.WriteAllLines(reportPath, lines);

            Console.WriteLine($"{unresolved.Count} placeholder(s) were NOT found in the restored output.");
            Console.WriteLine($"Unresolved replacements report at: {reportPath}");
        }

        private static HashSet<string> RestoreSingleFile(string sourcePath, string destPath, Dictionary<string, string> placeholderToOriginal)
        {
            var content = File.ReadAllText(sourcePath);
            var found = new HashSet<string>();

            foreach (var (placeholder, original) in placeholderToOriginal)
            {
                if (content.Contains(placeholder))
                {
                    found.Add(placeholder);
                    content = content.Replace(placeholder, original);
                }
            }

            File.WriteAllText(destPath, content);
            return found;
        }

        /// <summary>
        /// Computes the default output path by appending a suffix to the input's name,
        /// keeping it as a sibling (same parent directory) instead of nesting it inside
        /// a fixed folder. This keeps it out of the project (so there's no need to
        /// exclude it when building/running).
        ///   File:   ./MyFile.cs   -> ./MyFile_sanitized.cs
        ///   Folder: ./MyProject   -> ./MyProject_sanitized
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
            Console.WriteLine("             (without --output, defaults to '<input>_sanitized' next to the input)");
            Console.WriteLine("  Restore:   dotnet run -- restore --input <file-or-folder> --map <_replacement-map.txt> [--output <destination>]");
            Console.WriteLine("             (without --output, defaults to '<input>_restored' next to the input)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- sanitize --input ./MyProject");
            Console.WriteLine("  dotnet run -- restore --input ./ai-response.cs --map ./MyProject_sanitized/_replacement-map.txt");
        }
    }
}