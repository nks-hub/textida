using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Services;

/// <summary>A macro saved by the user: <see cref="File"/> is the file name in the macro folder.</summary>
internal sealed record SavedMacro(string Name, string File, IReadOnlyList<MacroStep> Steps);

/// <summary>
/// The saved macros: one JSON file per macro in the <c>macros</c> folder of the profile, written through a temporary file like the user's clip library.
/// A step is <c>{"command": id}</c> or <c>{"text": typed}</c>; a command step may add <c>"argument"</c>. Saving under a name that makes the same file name replaces that macro.
/// </summary>
internal sealed class MacroStore(string directory)
{
    public string Directory { get; } = directory;

    public string PathOf(string name) => Path.Combine(Directory, ClipLibrary.Slug(name) + ".json");

    /// <summary>Writes the macro; false with the reason in <paramref name="detail"/> when the folder or file cannot be written.</summary>
    public bool Save(string name, IReadOnlyList<MacroStep> steps, out string detail)
    {
        detail = "";
        var array = new JsonArray();
        foreach (var step in steps)
        {
            var o = step.Command is { } id ? new JsonObject { ["command"] = id } : new JsonObject { ["text"] = step.Text };
            if (step.Argument is { } argument) o["argument"] = argument;
            array.Add((JsonNode)o);
        }
        var json = new JsonObject { ["name"] = name.Trim(), ["steps"] = array }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var path = PathOf(name);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            detail = e.Message;
            return false;
        }
    }

    /// <summary>Removes the file of the macro; false with the reason in <paramref name="detail"/> when it cannot be deleted.</summary>
    public bool Delete(SavedMacro macro, out string detail)
    {
        detail = "";
        try
        {
            File.Delete(Path.Combine(Directory, macro.File));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            detail = e.Message;
            return false;
        }
    }

    /// <summary>Saves the macro under <paramref name="newName"/> and removes its old file when the new name makes another one. The caller has checked that no other macro has that file name.</summary>
    public bool Rename(SavedMacro macro, string newName, out string detail)
    {
        if (!Save(newName, macro.Steps, out detail)) return false;
        return string.Equals(Path.GetFileName(PathOf(newName)), macro.File, StringComparison.OrdinalIgnoreCase) || Delete(macro, out detail);
    }

    /// <summary>Every readable macro by name; a file that cannot be read is left out and described in <paramref name="problems"/>.</summary>
    public IReadOnlyList<SavedMacro> Load(List<(string File, string Reason)> problems)
    {
        var list = new List<SavedMacro>();
        if (!System.IO.Directory.Exists(Directory)) return list;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (new FileInfo(path).Length > PluginLimitsPlaceholders.MacroMaxFileBytes) throw new JsonException($"larger than {PluginLimitsPlaceholders.MacroMaxFileBytes} bytes");
                var root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))?.AsObject() ?? throw new JsonException("not an object");
                var name = root["name"]?.GetValue<string>() is { Length: > 0 } n ? n : throw new JsonException("no name");
                var steps = new List<MacroStep>();
                foreach (var node in root["steps"]?.AsArray() ?? throw new JsonException("no steps"))
                {
                    var o = node?.AsObject() ?? throw new JsonException("step is not an object");
                    string? command = o["command"]?.GetValue<string>(), text = o["text"]?.GetValue<string>(), argument = o["argument"]?.GetValue<string>();
                    if ((command is null) == (text is null)) throw new JsonException("a step needs a command or a text");
                    steps.Add(new MacroStep(command, text, argument));
                }
                list.Add(new SavedMacro(name, Path.GetFileName(path), steps));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
            {
                problems.Add((Path.GetFileName(path), e.Message));
            }
        }
        return list;
    }
}
