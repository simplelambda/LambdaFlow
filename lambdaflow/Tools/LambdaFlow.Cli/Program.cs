using System.Text.Json;

namespace LambdaFlow.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args) {
        try {
            if (args.Length == 0 || args[0] is "-h" or "--help") {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var rest    = args.Skip(1).ToArray();

            return command switch {
                "new"   => await NewCommand.Run(rest),
                "build" => await BuildCommand.Run(rest),
                _       => UnknownCommand(command)
            };
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"LambdaFlow error: {ex.Message}");
            return 1;
        }
    }

    internal static LambdaFlowConfig ReadConfig(string projectDir) {
        var configPath = Path.Combine(projectDir, "config.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"config.json not found at '{configPath}'.");

        var options = new JsonSerializerOptions {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        return JsonSerializer.Deserialize<LambdaFlowConfig>(File.ReadAllText(configPath), options)
            ?? throw new InvalidOperationException("config.json is malformed.");
    }

    private static int UnknownCommand(string command) {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp() {
        Console.WriteLine("""
        LambdaFlow CLI

        Commands:
                    lambdaflow new <AppName> [directory] [--framework <LambdaFlowRepo>] [--language <csharp|java|python|other>] [--frontend <basic|react>] [--backend-compile-command <cmd>] [--backend-compile-directory <dir>] [--debug] [--self-contained]
          lambdaflow build [projectDirectory] [--framework <LambdaFlowRepo>] [--debug]

        Examples:
                    dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- new MyApp Apps/MyApp --framework . --language csharp
                    dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- new MyReactApp Apps/MyReactApp --framework . --language python --frontend react --debug
                    dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- new MyJavaApp Apps/MyJavaApp --framework . --language java --backend-compile-command "mvn -q -DskipTests package" --backend-compile-directory target
          dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- build Apps/MyApp --framework .
        """);
    }
}
