using System.Text;
using System.Text.Json;
using NksPad.Workbench.Commands;
using NksPad.Workbench.Keys;
using NksPad.Workbench.Settings;

namespace NksPad.Workbench.Contributions;

public enum ManifestTrust
{
    /// <summary>Shipped with the editor; ids start with <c>nks.</c>.</summary>
    Builtin,

    /// <summary>Third party; every id must start with the manifest id and the manifest id may not start with <c>nks.</c>.</summary>
    Plugin,
}

/// <summary>Result of loading a manifest: either a valid manifest or the list of all errors found.</summary>
public sealed record ManifestLoadResult(ContributionManifest? Manifest, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Manifest is not null;
}

/// <summary>Parses and validates contribution manifests. A manifest with any error is rejected as a whole.</summary>
public static class ManifestLoader
{
    public const int MaxBytes = 256 * 1024;
    public const int MaxCommands = 1000;
    public const int MaxMenus = 2000;
    public const int MaxKeybindings = 2000;
    public const int MaxSettings = 500;
    private const int MaxText = 128;

    /// <summary>Checks the id scheme: 2 to 8 dot-separated segments, each starting with a lower-case letter.</summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128)
        {
            return false;
        }

        var segments = id.Split('.');
        if (segments.Length is < 2 or > 8)
        {
            return false;
        }

        return segments.All(s => s.Length > 0 && char.IsAsciiLetterLower(s[0]) && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    public static ManifestLoadResult Load(string json, ManifestTrust trust)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            return Fail($"Manifest is larger than {MaxBytes} bytes.");
        }

        ContributionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, ContributionJson.Default.ContributionManifest);
        }
        catch (JsonException ex)
        {
            return Fail("Invalid manifest JSON: " + ex.Message);
        }

        if (manifest is null)
        {
            return Fail("Manifest is empty.");
        }

        var errors = Validate(manifest, trust);
        return errors.Count == 0 ? new ManifestLoadResult(manifest, []) : new ManifestLoadResult(null, errors);
    }

    public static IReadOnlyList<string> Validate(ContributionManifest m, ManifestTrust trust)
    {
        var errors = new List<string>();
        if (m.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schemaVersion {m.SchemaVersion}.");
        }

        if (!IsValidId(m.Id))
        {
            errors.Add($"Invalid manifest id '{m.Id}'.");
            return errors;
        }

        var builtin = trust == ManifestTrust.Builtin;
        if (builtin != m.Id.StartsWith("nks.", StringComparison.Ordinal))
        {
            errors.Add(builtin ? "A built-in manifest id must start with 'nks.'." : "A plugin manifest id may not start with 'nks.'.");
        }

        var prefix = builtin ? "nks." : m.Id + ".";
        if (m.Commands.Count > MaxCommands) errors.Add($"More than {MaxCommands} commands.");
        if (m.Menus.Count > MaxMenus) errors.Add($"More than {MaxMenus} menu items.");
        if (m.Keybindings.Count > MaxKeybindings) errors.Add($"More than {MaxKeybindings} keybindings.");
        if (m.Settings.Count > MaxSettings) errors.Add($"More than {MaxSettings} settings.");
        if (errors.Count > 0)
        {
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in m.Commands)
        {
            if (!IsValidId(c.Id) || !c.Id.StartsWith(prefix, StringComparison.Ordinal))
            {
                errors.Add($"Command id '{c.Id}' must follow the id scheme and start with '{prefix}'.");
            }
            else if (!ids.Add(c.Id))
            {
                errors.Add($"Duplicate command id '{c.Id}'.");
            }

            CheckText(errors, $"Command '{c.Id}' title", c.Title, required: true);
            CheckText(errors, $"Command '{c.Id}' category", c.Category, required: false);
            CheckWhen(errors, $"Command '{c.Id}' when", c.When);
            CheckWhen(errors, $"Command '{c.Id}' enablement", c.Enablement);
            if (c.Undo is not ("none" or "transaction"))
            {
                errors.Add($"Command '{c.Id}' undo must be 'none' or 'transaction'.");
            }

            if (c.Fallback is { } fb && (!IsValidId(fb) || fb == c.Id))
            {
                errors.Add($"Command '{c.Id}' has an invalid fallback '{fb}'.");
            }

            foreach (var alias in c.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias) || alias.Length > MaxText)
                {
                    errors.Add($"Command '{c.Id}' has an invalid alias.");
                }
            }
        }

        foreach (var menu in m.Menus)
        {
            if (string.IsNullOrWhiteSpace(menu.Menu) || menu.Menu.Length > 64 || !IsValidId(menu.Command))
            {
                errors.Add($"Invalid menu item '{menu.Menu}' / '{menu.Command}'.");
            }

            CheckWhen(errors, $"Menu item '{menu.Command}' when", menu.When);
        }

        foreach (var kb in m.Keybindings)
        {
            var command = kb.Command.StartsWith('-') ? kb.Command[1..] : kb.Command;
            if (!IsValidId(command))
            {
                errors.Add($"Keybinding '{kb.Key}' has an invalid command '{kb.Command}'.");
            }

            foreach (var text in new[] { kb.Key, kb.Mac }.OfType<string>())
            {
                foreach (var platform in new[] { Platform.Windows, Platform.MacOS })
                {
                    if (!KeySequence.TryParse(text, platform, out _, out var error))
                    {
                        errors.Add(error);
                    }
                }
            }

            CheckWhen(errors, $"Keybinding '{kb.Key}' when", kb.When);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in m.Settings)
        {
            ValidateSetting(errors, s, builtin ? "" : prefix);
            if (!keys.Add(s.Key))
            {
                errors.Add($"Duplicate setting '{s.Key}'.");
            }
        }

        return errors;
    }

    private static void ValidateSetting(List<string> errors, SettingDefinition s, string prefix)
    {
        if (!IsValidId(s.Key) || !s.Key.StartsWith(prefix, StringComparison.Ordinal))
        {
            errors.Add($"Setting key '{s.Key}' must follow the id scheme" + (prefix.Length > 0 ? $" and start with '{prefix}'." : "."));
        }

        CheckText(errors, $"Setting '{s.Key}' title", s.Title, required: true);
        CheckText(errors, $"Setting '{s.Key}' description", s.Description, required: false);
        if (!SettingRules.Types.Contains(s.Type))
        {
            errors.Add($"Setting '{s.Key}' has an unknown type '{s.Type}'.");
            return;
        }

        if (!SettingRules.TryParseScope(s.Scope, out _))
        {
            errors.Add($"Setting '{s.Key}' has an unknown scope '{s.Scope}'.");
        }

        if (s.Values is not null && s.Type != "string")
        {
            errors.Add($"Setting '{s.Key}': enum is only allowed for strings.");
        }

        if ((s.Minimum is not null || s.Maximum is not null) && s.Type is not ("integer" or "number"))
        {
            errors.Add($"Setting '{s.Key}': minimum and maximum are only allowed for numbers.");
        }

        if (s.Default is null)
        {
            errors.Add($"Setting '{s.Key}' has no default.");
        }
        else if (SettingRules.Validate(s, s.Default) is { } why)
        {
            errors.Add($"Setting '{s.Key}' default is invalid: {why}");
        }
    }

    private static void CheckText(List<string> errors, string what, string? text, bool required)
    {
        if (text is null ? required : text.Length is 0 or > MaxText)
        {
            errors.Add($"{what} must be a resource key of 1 to {MaxText} characters.");
        }
    }

    private static void CheckWhen(List<string> errors, string what, string? expression)
    {
        if (expression is not null && !WhenExpression.TryParse(expression, out _, out var error))
        {
            errors.Add($"{what}: {error}");
        }
    }

    private static ManifestLoadResult Fail(string error) => new(null, [error]);
}
