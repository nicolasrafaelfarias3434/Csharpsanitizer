using Csharpsanitizer.CodeSanitizer;
using Csharpsanitizer.Rules;

namespace CodeSanitizer
{
    /// <summary>
    /// Uso:
    ///   dotnet run -- --input ./MiProyecto --output ./sanitized-output
    ///   dotnet run -- --input ./MiArchivo.cs
    ///
    /// Si --output se omite, usa "./sanitized-output" al lado de donde se ejecuta.
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
            var options = ParseArgs(args);
            if (options is null)
            {
                PrintUsage();
                return 1;
            }

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

        private sealed record Options(string Input, string Output);

        private static Options? ParseArgs(string[] args)
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

            return input is null ? null : new Options(input, output);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Uso: dotnet run -- --input <archivo-o-carpeta> [--output <carpeta-salida>]");
            Console.WriteLine("Ejemplo: dotnet run -- --input ./MiProyecto --output ./sanitized-output");
        }
    }
}
