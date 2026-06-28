using Csharpsanitizer.CodeSanitizer;
using Csharpsanitizer.Rules;

namespace CodeSanitizer
{

    /// <summary>
    /// Uso:
    ///   Sanitizar (oculta valores sensibles):
    ///     dotnet run -- sanitize --input ./MiProyecto --output ./sanitized-output
    ///     dotnet run -- --input ./MiArchivo.cs                 (sanitize es el comando por defecto)
    ///
    ///   Restaurar (revierte placeholders a valores originales, ej. sobre la
    ///   respuesta de la IA que pegaste en un archivo):
    ///     dotnet run -- restore --input ./respuesta-ia.cs --map ./sanitized-output/_replacement-map.txt --output ./respuesta-ia.restaurado.cs
    ///
    /// Si --output se omite en sanitize, usa "./sanitized-output" al lado de donde se ejecuta.
    /// El mapa de reemplazo queda en "<output>/_replacement-map.txt" (NO subir a ningún lado,
    /// es solo para que vos puedas reconstruir nombres si necesitás interpretar la respuesta de la IA).
    /// </summary>
    public static class Program
    {
        // Extensiones que tratamos como texto plano + reglas regex.
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

            // El primer argumento puede ser el comando ("sanitize"/"restore"). Si no lo es
            // (ej. empieza con "--"), se asume "sanitize" para no romper el uso anterior.
            var command = args[0].StartsWith("-") ? "sanitize" : args[0].ToLowerInvariant();
            var rest = args[0].StartsWith("-") ? args : args.Skip(1).ToArray();

            return command switch
            {
                "sanitize" => RunSanitize(rest),
                "restore" => RunRestore(rest),
                _ => Fail($"Comando desconocido: {command}")
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
                return Fail("Faltan argumentos para 'sanitize'.");

            if (!File.Exists(options.Input) && !Directory.Exists(options.Input))
            {
                Console.WriteLine($"No se encontró el path de entrada: {options.Input}");
                return 1;
            }

            Directory.CreateDirectory(options.Output);
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
                var destPath = Path.Combine(options.Output, relative);
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
                    // Binarios u otros: se copian tal cual (no se intenta sanitizar).
                    File.Copy(file, destPath, overwrite: true);
                }
            }

            var mapPath = Path.Combine(options.Output, "_replacement-map.txt");
            map.SaveTo(mapPath);

            Console.WriteLine($"Procesados {processed} archivo(s) de texto/código.");
            Console.WriteLine($"Salida sanitizada en: {Path.GetFullPath(options.Output)}");
            Console.WriteLine($"Mapa de reemplazo (NO compartir) en: {mapPath}");
            Console.WriteLine($"Reemplazos totales: {map.Entries.Count}");

            return 0;
        }

        private static int RunRestore(string[] args)
        {
            var options = ParseRestoreArgs(args);
            if (options is null)
                return Fail("Faltan argumentos para 'restore'. Necesitás --input, --map y opcionalmente --output.");

            if (!File.Exists(options.Map))
            {
                Console.WriteLine($"No se encontró el archivo de mapa: {options.Map}");
                return 1;
            }

            if (!File.Exists(options.Input) && !Directory.Exists(options.Input))
            {
                Console.WriteLine($"No se encontró el path de entrada: {options.Input}");
                return 1;
            }

            var placeholderToOriginal = ReplacementMap.LoadPlaceholderToOriginal(options.Map);
            if (placeholderToOriginal.Count == 0)
            {
                Console.WriteLine("El mapa está vacío, no hay nada para restaurar.");
                return 1;
            }

            var output = options.Output ?? DefaultRestoreOutput(options.Input);

            if (File.Exists(options.Input))
            {
                RestoreSingleFile(options.Input, output, placeholderToOriginal);
                Console.WriteLine($"Restaurado: {Path.GetFullPath(output)}");
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

                Console.WriteLine($"Restaurados {restored} archivo(s) en: {Path.GetFullPath(output)}");
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

        private static string DefaultRestoreOutput(string input)
        {
            if (File.Exists(input))
            {
                var dir = Path.GetDirectoryName(input) ?? ".";
                var name = Path.GetFileNameWithoutExtension(input);
                var ext = Path.GetExtension(input);
                return Path.Combine(dir, $"{name}.restored{ext}");
            }

            return input.TrimEnd('/', '\\') + "-restored";
        }

        private sealed record SanitizeOptions(string Input, string Output);
        private sealed record RestoreOptions(string Input, string Map, string? Output);

        private static SanitizeOptions? ParseSanitizeArgs(string[] args)
        {
            string? input = null;
            string output = "./sanitized-output";

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
            Console.WriteLine("Uso:");
            Console.WriteLine("  Sanitizar:  dotnet run -- sanitize --input <archivo-o-carpeta> [--output <carpeta-salida>]");
            Console.WriteLine("  Restaurar:  dotnet run -- restore --input <archivo-o-carpeta> --map <_replacement-map.txt> [--output <destino>]");
            Console.WriteLine();
            Console.WriteLine("Ejemplos:");
            Console.WriteLine("  dotnet run -- sanitize --input ./MiProyecto --output ./sanitized-output");
            Console.WriteLine("  dotnet run -- restore --input ./respuesta-ia.cs --map ./sanitized-output/_replacement-map.txt");
        }
    }
}