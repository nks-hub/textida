namespace NksHub.Textida.Plugin.Validate;

/// <summary>How serious a finding is: an error fails the validation, a warning is something the author should know, an info says what was checked.</summary>
public enum FindingSeverity
{
    /// <summary>What was checked and found fine.</summary>
    Info,
    /// <summary>Something that works but is not what a distributed plugin should be.</summary>
    Warning,
    /// <summary>The editor would refuse the plugin, or the plugin breaks a rule of the API.</summary>
    Error,
}

/// <summary>One result of the validator.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Check">The check that found it: <c>manifest</c>, <c>api</c>, <c>permissions</c>, <c>size</c>, <c>aot</c>, <c>source</c>, <c>handshake</c>, <c>rpc</c> or <c>scenario</c>.</param>
/// <param name="Message">What was found.</param>
public sealed record Finding(FindingSeverity Severity, string Check, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"[{Severity.ToString().ToLowerInvariant()}] {Check}: {Message}";
}

/// <summary>Everything the validator found in one plugin folder.</summary>
/// <param name="Folder">The folder, as a full path.</param>
/// <param name="Findings">The findings in the order the checks ran.</param>
public sealed record ValidationReport(string Folder, IReadOnlyList<Finding> Findings)
{
    /// <summary>True when there is no error.</summary>
    public bool Passed => Findings.All(f => f.Severity != FindingSeverity.Error);

    /// <summary>The findings of one severity.</summary>
    public IEnumerable<Finding> Of(FindingSeverity severity) => Findings.Where(f => f.Severity == severity);
}

/// <summary>What to check and how far to go. The limits have no defaults: they are the limits of the editor, read from one place by the caller.</summary>
public sealed record ValidatorOptions
{
    /// <summary>The plugin API version the manifest range must contain.</summary>
    public int HostApiVersion { get; init; } = PluginSdk.PluginApi.Version;

    /// <summary>Largest grammar file of the plugin, in bytes.</summary>
    public required long MaxGrammarBytes { get; init; }

    /// <summary>Largest icon file (SVG) of the plugin, in bytes; while it is 0 an icon file is refused and only the names of built-in icons are accepted.</summary>
    public long MaxIconBytes { get; init; }

    /// <summary>Largest size of the whole plugin folder, in bytes.</summary>
    public required long MaxFolderBytes { get; init; }

    /// <summary>Most files in the plugin folder.</summary>
    public required int MaxEntries { get; init; }

    /// <summary>How long the validator waits for the handshake of a plugin it starts, and for each check after it.</summary>
    public required TimeSpan PluginTimeout { get; init; }

    /// <summary>Start the plugin and check the handshake and the answer to an unknown method.</summary>
    public bool Run { get; init; }

    /// <summary>A recording (see <c>Recording</c> of the test host) to replay against the running plugin; it also tells which permissions the plugin really uses. Needs <see cref="Run"/>.</summary>
    public string? ScenarioPath { get; init; }

    /// <summary>The folder with the C# sources of the plugin; the analyzer that bans reflection runs over them.</summary>
    public string? SourcePath { get; init; }
}
