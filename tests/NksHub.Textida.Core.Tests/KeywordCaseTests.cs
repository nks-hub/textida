using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

public class KeywordCaseTests
{
    private const string Sql = "select a, 'from here' from t -- where it ends\nwhere x = 1; /* order\nby */ order by a\n";

    private static string Change(string text, KeywordCaseMode mode, string grammar = "sql", int? from = null, int? to = null)
    {
        var tokenizer = BuiltinGrammars.Tokenizer(grammar);
        return KeywordCaseChange.Apply(text, KeywordCaseChange.Edits(text, tokenizer, mode, from ?? 0, to ?? text.Length));
    }

    [Fact]
    public void Keywords_change_but_strings_comments_and_identifiers_do_not()
    {
        string upper = Change(Sql, KeywordCaseMode.Upper);
        Assert.Equal("SELECT a, 'from here' FROM t -- where it ends\nWHERE x = 1; /* order\nby */ ORDER BY a\n", upper);

        // the names t and a and x are identifiers; "from" in the string and "where" in the comment stay as they were, also the "order" and "by" inside the block comment that goes over two lines
        Assert.Contains("'from here'", upper);
        Assert.Contains("-- where it ends", upper);
        Assert.Contains("/* order\nby */", upper);
        Assert.Equal(Sql, Change(upper, KeywordCaseMode.Lower));
    }

    [Fact]
    public void Only_the_keywords_inside_the_range_change_and_the_state_before_it_is_known()
    {
        int from = Sql.IndexOf("where x", StringComparison.Ordinal), to = Sql.IndexOf("/*", StringComparison.Ordinal);
        Assert.Equal("select a, 'from here' from t -- where it ends\nWHERE x = 1; /* order\nby */ order by a\n", Change(Sql, KeywordCaseMode.Upper, from: from, to: to));

        // a range that begins inside a block comment: the words there are comment text, whatever the range says
        int inside = Sql.IndexOf("order\nby", StringComparison.Ordinal);
        Assert.Equal(Sql, Change(Sql, KeywordCaseMode.Upper, from: inside, to: inside + "order\nby".Length));
    }

    [Fact]
    public void As_defined_gives_the_spelling_of_the_grammar_and_a_case_sensitive_language_is_left_alone()
    {
        Assert.Equal("select a from t where x = 1", Change("SELECT a FrOm t WHERE x = 1", KeywordCaseMode.AsDefined)); // the list of the SQL grammar is lower case

        const string cs = "class A { void F() { if (x) return; } }";
        Assert.False(KeywordCaseChange.IsSafe(BuiltinGrammars.Registry.Find("csharp")!)); // a change of case would make another program
        Assert.Equal(cs, Change(cs, KeywordCaseMode.Upper, "csharp"));
    }

    [Fact]
    public void Line_breaks_of_any_kind_are_kept_and_offsets_stay_right()
    {
        string text = "select 1\r\nfrom t\rwhere 2\n";
        Assert.Equal("SELECT 1\r\nFROM t\rWHERE 2\n", Change(text, KeywordCaseMode.Upper));
    }
}
