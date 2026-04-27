namespace LambdaFlow.Cli;

internal sealed class CommandOptions
{
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string>            _flags  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<string> Positionals { get; }

    internal CommandOptions(string[] args) {
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                positionals.Add(arg);
                continue;
            }

            // Boolean flag: no following value, or next token is another option
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                _flags.Add(arg);
                continue;
            }

            _values[arg] = args[++i];
        }

        Positionals = positionals;
    }

    internal string? Get(string name) {
        return _values.TryGetValue(name, out var value) ? value : null;
    }

    internal bool HasFlag(string name) {
        return _flags.Contains(name) || _values.ContainsKey(name);
    }
}
