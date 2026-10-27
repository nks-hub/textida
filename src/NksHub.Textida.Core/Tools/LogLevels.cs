using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

/// <summary>Severity classes of a log line, from the most to the least severe.</summary>
public enum LogLevel { Error, Warning, Info, Debug }

/// <summary>
/// Decides the level of a line of a log from one regular expression per level. The most severe level whose expression finds something in the line wins,
/// so a line with both a warning and an error counts as an error. Expressions are data: a plugin contributes them, or the defaults below are used.
/// </summary>
public sealed class LogLevelRules
{
    private readonly Regex?[] _rules;

    private LogLevelRules(Regex?[] rules) => _rules = rules;

    /// <summary>Upper-case level words as the common logging libraries write them. They are the starting point only; a log that writes levels differently brings its own expressions.</summary>
    public static LogLevelRules Default { get; } = Create(new Dictionary<string, string>
    {
        ["error"] = @"\b(?:FATAL|CRITICAL|SEVERE|ERROR|ERR)\b",
        ["warning"] = @"\b(?:WARNING|WARN)\b",
        ["info"] = @"\bINFO\b",
        ["debug"] = @"\b(?:DEBUG|TRACE)\b",
    });

    /// <summary>Builds the rules from expressions keyed by the lower-case level name (<c>error</c>, <c>warning</c>, <c>info</c>, <c>debug</c>); a level without one is never assigned.</summary>
    /// <exception cref="LogPatternException">An unknown level name or an invalid expression.</exception>
    public static LogLevelRules Create(IReadOnlyDictionary<string, string> patterns)
    {
        var rules = new Regex?[Enum.GetValues<LogLevel>().Length];
        foreach (var (name, pattern) in patterns)
        {
            LogLevel level = name switch
            {
                "error" => LogLevel.Error, "warning" => LogLevel.Warning, "info" => LogLevel.Info, "debug" => LogLevel.Debug,
                _ => throw new LogPatternException($"Unknown level '{name}': use error, warning, info or debug."),
            };
            try { rules[(int)level] = new Regex(pattern, RegexOptions.CultureInvariant | (RuntimeFeature.IsDynamicCodeCompiled ? RegexOptions.Compiled : RegexOptions.None), TimeSpan.FromSeconds(1)); }
            catch (ArgumentException e) { throw new LogPatternException($"Level '{name}': invalid regular expression: {e.Message}"); }
        }
        return new LogLevelRules(rules);
    }

    /// <summary>The expression of a level, or null when the rules have none for it.</summary>
    public Regex? RuleOf(LogLevel level) => _rules[(int)level];

    /// <summary>The level of the line, or null when no level word is in it.</summary>
    public LogLevel? Classify(ReadOnlySpan<char> line)
    {
        for (int i = 0; i < _rules.Length; i++)
        {
            if (_rules[i] is not { } regex) continue;
            try { if (regex.IsMatch(line)) return (LogLevel)i; }
            catch (RegexMatchTimeoutException) { /* a line the expression cannot handle has no level */ }
        }
        return null;
    }
}
