using System.Text.Json;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Tests;

public class JsonTypeGeneratorTests
{
    private const string Sample = """{"id": 7, "user_name": "ann", "price": 1.5, "tags": ["a", "b"], "owner": {"name": "x", "age": 3}, "items": [{"sku": "s1", "qty": 1}, {"sku": "s2", "note": "n"}], "gone": null, "ok": true}""";

    [Fact]
    public void A_Go_struct_has_tags_pointers_for_optional_fields_and_nested_types()
    {
        string go = JsonTypeGenerator.Generate(Sample, TypeLanguage.Go);
        Assert.Contains("type Root struct {", go);
        Assert.Contains("\tId int64 `json:\"id\"`", go);
        Assert.Contains("\tUserName string `json:\"user_name\"`", go);
        Assert.Contains("\tPrice float64 `json:\"price\"`", go);
        Assert.Contains("\tTags []string `json:\"tags\"`", go);
        Assert.Contains("\tOwner Owner `json:\"owner\"`", go);
        Assert.Contains("\tItems []Item `json:\"items\"`", go);
        Assert.Contains("\tGone interface{} `json:\"gone,omitempty\"`", go);
        Assert.Contains("\tOk bool `json:\"ok\"`", go);
        Assert.Contains("type Owner struct {", go);
        // the objects of the array are one type; a field one of them lacks is optional
        Assert.Contains("\tQty *int64 `json:\"qty,omitempty\"`", go);
        Assert.Contains("\tNote *string `json:\"note,omitempty\"`", go);
        Assert.Contains("\tSku string `json:\"sku\"`", go);
    }

    [Fact]
    public void A_CSharp_class_has_property_names_nullable_optional_fields_and_lists()
    {
        string cs = JsonTypeGenerator.Generate(Sample, TypeLanguage.CSharp);
        Assert.StartsWith("using System.Text.Json.Serialization;", cs);
        Assert.Contains("public class Root", cs);
        Assert.Contains("[JsonPropertyName(\"user_name\")]\n    public string UserName { get; set; }", cs);
        Assert.Contains("public long Id { get; set; }", cs);
        Assert.Contains("public double Price { get; set; }", cs);
        Assert.Contains("public List<string> Tags { get; set; }", cs);
        Assert.Contains("public List<Item> Items { get; set; }", cs);
        Assert.Contains("public long? Qty { get; set; }", cs);
        Assert.Contains("public string? Note { get; set; }", cs);
        Assert.Contains("public object Gone { get; set; }", cs);
    }

    [Fact]
    public void A_TypeScript_interface_marks_optional_fields_and_keeps_names_that_are_not_identifiers_quoted()
    {
        string ts = JsonTypeGenerator.Generate("""{"a-b": 1, "list": [1, 2.5], "mixed": [1, "x"], "maybe": null, "nested": {"x": true}}""", TypeLanguage.TypeScript, "Doc");
        Assert.Contains("export interface Doc {", ts);
        Assert.Contains("  \"a-b\": number;", ts);
        Assert.Contains("  list: number[];", ts); // an integer and a float are one numeric type
        Assert.Contains("  mixed: unknown[];", ts); // values of different kinds have no one type
        Assert.Contains("  maybe?: unknown;", ts);
        Assert.Contains("  nested: Nested;", ts);
        Assert.Contains("export interface Nested {\n  x: boolean;\n}", ts);
    }

    [Fact]
    public void A_document_that_is_an_array_gets_an_alias_for_the_array_and_the_types_of_its_objects()
    {
        string go = JsonTypeGenerator.Generate("""[{"id": 1}, {"id": 2}]""", TypeLanguage.Go, "users");
        Assert.StartsWith("type Users []User\n\ntype User struct {", go); // the element type is the singular of the name of the document
        string ts = JsonTypeGenerator.Generate("[1, 2]", TypeLanguage.TypeScript);
        Assert.Equal("export type Root = number[];\n", ts);
    }

    [Fact]
    public void Names_that_collide_are_numbered_and_names_that_start_with_a_digit_are_made_valid()
    {
        string go = JsonTypeGenerator.Generate("""{"a": {"x": 1}, "A": {"y": 2}, "1st": 3}""", TypeLanguage.Go);
        Assert.Contains("type A struct {", go);
        Assert.Contains("type A2 struct {", go);
        Assert.Contains("\t_1st int64 `json:\"1st\"`", go);
    }

    [Fact]
    public void Text_that_is_not_JSON_is_refused()
    {
        Assert.ThrowsAny<JsonException>(() => JsonTypeGenerator.Generate("{not json", TypeLanguage.Go));
    }
}
