using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>
/// The types of the plugin API for plugins in other languages (PZ.8.2). The JSON Schema comes from the same types the C# SDK sends and reads (<see cref="SdkJson"/> and the handshake of <see cref="PluginJson"/>), so the schema
/// and the SDK cannot disagree; the TypeScript and Python files are written from that schema. <c>TEXTIDA_UPDATE_SDK=1</c> writes the three files, a test compares them.
/// </summary>
internal static class SdkTypes
{
    /// <summary>The method, the type of its parameters and the type of its result (null: the answer holds nothing the plugin uses).</summary>
    internal static readonly (string Method, JsonTypeInfo Params, JsonTypeInfo? Result)[] Methods =
    [
        (PluginMethods.Initialize, PluginJson.Default.InitializeParams, PluginJson.Default.InitializeResult),
        (DocumentMethods.Info, SdkJson.Default.DocumentRef, SdkJson.Default.DocumentInfoResult),
        (DocumentMethods.Selections, SdkJson.Default.DocumentRef, SdkJson.Default.SelectionsResult),
        (DocumentMethods.ReadText, SdkJson.Default.ReadTextParams, SdkJson.Default.ReadTextResult),
        (DocumentMethods.Facts, SdkJson.Default.RevisionParams, SdkJson.Default.FactsResult),
        (DocumentMethods.OffsetToPosition, SdkJson.Default.PositionParams, SdkJson.Default.PositionResult),
        (DocumentMethods.EditApply, SdkJson.Default.ApplyParams, null),
        (DocumentMethods.EditPropose, SdkJson.Default.ProposeParams, null),
        (UiMethods.Notify, SdkJson.Default.NoticeParams, null),
        (UiMethods.TaskStart, SdkJson.Default.TaskStartParams, SdkJson.Default.TaskStartResult),
        (UiMethods.TaskProgress, SdkJson.Default.TaskProgressParams, null),
        (UiMethods.TaskEnd, SdkJson.Default.TaskEndParams, null),
    ];

    // nested types that get a name of their own, found by their members
    private static readonly Dictionary<string, string> NestedNames = new()
    {
        ["start,end,text"] = "RangeEdit",
        ["anchor,caret"] = "Selection",
        ["dominant"] = "LineEndingFacts",
    };

    private sealed record Member(string Name, JsonNode Schema, bool Required);

    private sealed record Shape(string Name, List<Member> Members);

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static List<string> TypesOf(JsonNode? node) =>
        node is not JsonObject o || o["type"] is not { } type ? [] : type is JsonArray list ? [.. list.Select(n => n!.GetValue<string>())] : [type.GetValue<string>()];

    private static string NameFor(JsonNode node, string context)
    {
        string key = string.Join(',', node["properties"]!.AsObject().Select(p => p.Key));
        return NestedNames.GetValueOrDefault(key, context);
    }

    /// <summary>The schema document: the methods with their parameters and results, and the named types.</summary>
    public static string SchemaText()
    {
        var defs = new JsonObject();
        var methods = new JsonObject();
        foreach (var (method, p, r) in Methods)
        {
            methods[method] = new JsonObject { ["params"] = "#/$defs/" + p.Type.Name, ["result"] = r is null ? null : "#/$defs/" + r.Type.Name };
            defs[p.Type.Name] = JsonSchemaExporter.GetJsonSchemaAsNode(p);
            if (r is not null) defs[r.Type.Name] = JsonSchemaExporter.GetJsonSchemaAsNode(r);
        }
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "Textida plugin API: the calls the SDKs make and the handshake",
            ["description"] = "Made from the types of the C# SDK. Names are camelCase; an integer is a 64-bit number; a member that is not in required may be left out.",
            ["methods"] = methods,
            ["$defs"] = defs,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Registers an object schema under its name and, below it, every object that a member (or the items of a member) holds.</summary>
    private static void Walk(JsonNode schema, string name, List<Shape> shapes)
    {
        if (schema is not JsonObject || schema["properties"] is not JsonObject properties) return;
        name = NameFor(schema, name);
        if (shapes.Any(s => s.Name == name)) return;
        var required = (schema["required"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToHashSet() ?? [];
        var shape = new Shape(name, [.. properties.Select(p => new Member(p.Key, p.Value!, required.Contains(p.Key)))]);
        shapes.Add(shape);
        foreach (var m in shape.Members)
        {
            var below = TypesOf(m.Schema).Contains("array") && m.Schema["items"] is { } items ? items : m.Schema;
            if (below is not null) Walk(below, shape.Name + Pascal(m.Name), shapes);
        }
    }

    private static List<Shape> Shapes()
    {
        var shapes = new List<Shape>();
        foreach (var (_, p, r) in Methods)
            foreach (var info in new[] { p, r }.OfType<JsonTypeInfo>()) Walk(JsonSchemaExporter.GetJsonSchemaAsNode(info), info.Type.Name, shapes);
        return shapes;
    }

    private static string Render(JsonNode node, string context, Func<string, string> scalar, Func<string, string> array, string nullSuffix)
    {
        if (node is not JsonObject) return scalar("any");
        var names = TypesOf(node);
        bool nullable = names.Remove("null");
        string type;
        if (names.Contains("array")) type = array(Render(node["items"]!, context + "Item", scalar, array, nullSuffix));
        else if (names.Contains("object") && node["properties"] is not null) type = NameFor(node, context);
        else type = scalar(names.Count == 1 ? names[0] : "any");
        return nullable ? type + nullSuffix : type;
    }

    public static string TypeScript()
    {
        static string Scalar(string t) => t switch { "string" => "string", "integer" or "number" => "number", "boolean" => "boolean", _ => "unknown" };
        static string Array(string item) => item.Contains(' ') ? $"({item})[]" : item + "[]";
        var sb = new StringBuilder();
        sb.Append("// Generated from the types of the SDK by SdkTypesTests (TEXTIDA_UPDATE_SDK=1 writes it). Do not edit.\n\n");
        foreach (var shape in Shapes())
        {
            sb.Append("export interface ").Append(shape.Name).Append(" {\n");
            foreach (var m in shape.Members)
                sb.Append("  ").Append(m.Name).Append(m.Required ? "" : "?").Append(": ").Append(Render(m.Schema, shape.Name + Pascal(m.Name), Scalar, Array, " | null")).Append(";\n");
            sb.Append("}\n\n");
        }
        sb.Append("/** The calls a plugin makes to the editor and the handshake: method name to the type of its parameters and of its result. */\nexport interface Methods {\n");
        foreach (var (method, p, r) in Methods)
            sb.Append("  \"").Append(method).Append("\": { params: ").Append(p.Type.Name).Append("; result: ").Append(r is null ? "null" : r.Type.Name).Append(" };\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    public static string Python()
    {
        static string Scalar(string t) => t switch { "string" => "str", "integer" => "int", "number" => "float", "boolean" => "bool", _ => "Any" };
        static string Array(string item) => $"list[{item}]";
        var shapes = Shapes();
        var sb = new StringBuilder();
        sb.Append("# Generated from the types of the SDK by SdkTypesTests (TEXTIDA_UPDATE_SDK=1 writes it). Do not edit.\nfrom __future__ import annotations\n\nfrom typing import Any, NotRequired, TypedDict\n\n\n");
        // a class comes after the classes its members name
        var ordered = new List<Shape>();
        void Add(Shape s)
        {
            if (ordered.Contains(s)) return;
            foreach (var other in shapes.Where(o => o != s && s.Members.Any(m => Render(m.Schema, s.Name + Pascal(m.Name), Scalar, Array, " | None").Contains(o.Name)))) Add(other);
            ordered.Add(s);
        }
        foreach (var shape in shapes) Add(shape);
        foreach (var shape in ordered)
        {
            sb.Append("class ").Append(shape.Name).Append("(TypedDict):\n");
            foreach (var m in shape.Members)
            {
                string type = Render(m.Schema, shape.Name + Pascal(m.Name), Scalar, Array, " | None");
                sb.Append("    ").Append(m.Name).Append(": ").Append(m.Required ? type : $"NotRequired[{type}]").Append('\n');
            }
            sb.Append("\n\n");
        }
        sb.Append("# method name -> (type of the parameters, type of the result or None)\nMETHODS: dict[str, tuple[str, str | None]] = {\n");
        foreach (var (method, p, r) in Methods)
            sb.Append("    \"").Append(method).Append("\": (\"").Append(p.Type.Name).Append("\", ").Append(r is null ? "None" : "\"" + r.Type.Name + "\"").Append("),\n");
        sb.Append("}\n");
        return sb.ToString();
    }
}
