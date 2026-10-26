using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>Generates the JSON Schemas that are committed under <c>schemas/</c>; a test keeps the files in sync.</summary>
public static class SchemaGenerator
{
    /// <summary>
    /// Schema of a settings file for the given definitions. Unknown keys and properties are allowed on purpose:
    /// the editor keeps them.
    /// </summary>
    public static JsonNode ForSettings(SettingsRegistry registry, StringTable english, int schemaVersion)
    {
        var properties = new JsonObject();
        foreach (var d in registry.Definitions)
        {
            var p = new JsonObject
            {
                ["type"] = d.Type,
                ["default"] = d.Default!.DeepClone(),
            };
            if (d.Values is not null)
            {
                p["enum"] = new JsonArray(d.Values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
            }

            if (d.Minimum is { } min) p["minimum"] = min;
            if (d.Maximum is { } max) p["maximum"] = max;
            p["description"] = english.Get(d.Title) + (d.Description is null ? "" : ". " + english.Get(d.Description));
            p["x-scope"] = d.Scope;
            properties[d.Key] = p;
        }

        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "Editor settings file",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["schemaVersion"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["default"] = schemaVersion },
                ["settings"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["additionalProperties"] = true,
                },
                ["languages"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = new JsonObject { ["$ref"] = "#/properties/settings" },
                },
            },
            ["additionalProperties"] = true,
        };
    }

    /// <summary>Schema of the contribution manifest, derived from the model types.</summary>
    public static JsonNode ForContributions() =>
        JsonSchemaExporter.GetJsonSchemaAsNode(ContributionJson.Default.ContributionManifest);

    /// <summary>Schema of the keymap file, derived from the model types.</summary>
    public static JsonNode ForKeymapFile() =>
        JsonSchemaExporter.GetJsonSchemaAsNode(ContributionJson.Default.KeymapFile);

    /// <summary>Indented text with LF line ends, as committed.</summary>
    public static string ToText(JsonNode schema)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            schema.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length) + "\n";
    }
}
