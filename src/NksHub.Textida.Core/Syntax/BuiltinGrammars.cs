using System.Reflection;

namespace NksHub.Textida.Core.Syntax;

/// <summary>The grammars shipped with the editor, read from the embedded <c>Syntax/Grammars/*.json</c> files.</summary>
public static class BuiltinGrammars
{
    private const string SchemaResource = "grammar.schema.json";
    private static readonly Lazy<GrammarRegistry> Shared = new(Load);

    /// <summary>Registry holding all built-in grammars. Add your own to a new registry, not to this one.</summary>
    public static GrammarRegistry Registry => Shared.Value;

    /// <summary>JSON Schema of the grammar file format.</summary>
    public static string Schema => ReadResource(SchemaResource);

    public static Tokenizer Tokenizer(string id) =>
        new(Registry.Find(id) ?? throw new KeyNotFoundException($"No built-in grammar '{id}'."));

    private static GrammarRegistry Load()
    {
        var registry = new GrammarRegistry();
        var assembly = typeof(BuiltinGrammars).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
            if (name.StartsWith("Grammars/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) && !name.EndsWith(SchemaResource, StringComparison.Ordinal))
                registry.Add(ReadResource(name["Grammars/".Length..]));
        SqlDialects.Register(registry, ReadResource("sql.json"));
        return registry;
    }

    private static string ReadResource(string file)
    {
        using var stream = typeof(BuiltinGrammars).Assembly.GetManifestResourceStream("Grammars/" + file)
            ?? throw new FileNotFoundException("Missing embedded grammar file " + file);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
