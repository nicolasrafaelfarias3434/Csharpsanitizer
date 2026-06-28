# Csharpsanitizer
This SW takes a project and remove sensitive information to use in an AI agent without compromise clients private information.

Usage:

  Sanitize:  dotnet run -- sanitize --input <'file-or-folder'> [--output <'destination'>]
    (without --output, use <'input'>_sanitized next to input)

  Restore:  dotnet run -- restore --input <'file-or-folder'> --map <'_replacement-map.txt'> [--output <'destination'>]
    (without --output, use <'input'>_restored next to input)

Examples:

  dotnet run -- sanitize --input ./MyProject
  
  dotnet run -- restore --input ./result-ia.cs --map ./MyProject_sanitized/_replacement-map.txt