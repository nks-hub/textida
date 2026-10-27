using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Plugins.Formatters;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.6.3 and P4.6.5: SQL layout by dialect, lossless HTML entities and the search for HTML Tidy.</summary>
public class FormattersTests
{
    private static readonly SqlFormatOptions Upper4 = new(KeywordCase.Upper, 4);

    private static string Sql(string text, string dialect = SqlDialects.Generic, SqlFormatOptions? options = null) =>
        SqlFormatter.Format(text, dialect, "\n", options ?? Upper4);

    [Fact]
    public void A_query_gets_a_clause_per_line_one_item_per_line_and_upper_case_keywords()
    {
        const string input = "select a,b, count(*) as n from t1 left join t2 on t1.id=t2.id and t2.x>1 where a=1 and (b in (1,2) or c between 3 and 4) group by a,b order by n desc limit 10;";
        Assert.Equal(
            "SELECT\n    a,\n    b,\n    COUNT(*) AS n\nFROM t1\nLEFT JOIN t2 ON t1.id = t2.id\n    AND t2.x > 1\nWHERE a = 1\n    AND (b IN (1, 2) OR c BETWEEN 3 AND 4)\nGROUP BY\n    a,\n    b\nORDER BY\n    n DESC\nLIMIT 10;",
            Sql(input));
    }

    [Fact]
    public void Sub_queries_are_indented_and_statements_are_separated_with_the_blank_lines_the_author_left()
    {
        const string input = "select * from (select id from t where x = 1) s;\n\n\nupdate t set a=1,b=-2 where id=3; delete from t where id = 4";
        Assert.Equal(
            "SELECT\n    *\nFROM (\n    SELECT\n        id\n    FROM t\n    WHERE x = 1\n) s;\n\nUPDATE t\nSET\n    a = 1,\n    b = -2\nWHERE id = 3;\nDELETE FROM t\nWHERE id = 4",
            Sql(input));
    }

    [Fact]
    public void Strings_quoted_names_and_comments_are_copied_as_written_and_the_case_option_is_honoured()
    {
        const string input = "select 'It''s  FROM  here', \"Mixed Case\" -- trailing note\nfrom t /* keep   this */ where a = 'x';";
        var upper = Sql(input);
        Assert.Contains("'It''s  FROM  here'", upper);
        Assert.Contains("\"Mixed Case\" -- trailing note", upper);
        Assert.Contains("/* keep   this */", upper);
        var lower = Sql("SELECT A FROM T WHERE B = NULL", options: new SqlFormatOptions(KeywordCase.Lower, 2));
        Assert.Equal("select\n  A\nfrom T\nwhere B = null", lower);
        Assert.Equal("SELECT\n    a\nfrom t", Sql("SELECT a from t", options: new SqlFormatOptions(KeywordCase.Preserve, 4)));
    }

    [Fact]
    public void Formatting_the_result_again_changes_nothing()
    {
        string[] samples =
        [
            "select a,b from t where a=1 and b=2 -- note\n; -- after\nselect 1",
            "with c as (select id from t) select * from c join d on c.id=d.id where d.x in (select y from z) order by 1",
            "insert into t (a,b) values (1,2),(3,4); update t set a = 'x' where id = 1",
            "-- head\nselect case when a > 1 and b < 2 then 'x' else 'y' end as k, -a, 1 - 2, x::int, t.* from t /* c */ ",
            "select 1;\n\n\n-- own line\nselect 2",
        ];
        foreach (var sample in samples)
            foreach (var dialect in SqlDialects.All.Select(d => d.Id))
            {
                var once = Sql(sample, dialect);
                Assert.Equal(once, Sql(once, dialect));
            }
    }

    [Fact]
    public void Only_the_text_given_is_changed_and_white_space_around_it_stays()
    {
        Assert.Equal("  \n SELECT\n    1 \n\n", Sql("  \n select 1 \n\n"));
    }

    [Fact]
    public void Each_dialect_reads_its_own_quoting_and_comments()
    {
        Assert.Equal("SELECT\n    `a b`\nFROM t -- x # y", Sql("select `a b` from t -- x # y", "mysql"));
        Assert.Equal("SELECT\n    [a b]\nFROM t", Sql("select [a b] from t", "tsql"));
        Assert.Equal("SELECT\n    a\nFROM t # note\nWHERE b = 1", Sql("select a from t # note\nwhere b=1", "mysql"));
        Assert.Equal("SELECT\n    $$ from $$\nFROM t", Sql("select $$ from $$ from t", "postgres"));
        Assert.Equal("SELECT\n    arr[1]\nFROM t", Sql("select arr[1] from t", "postgres"));
    }

    [Fact]
    public void Words_of_a_dialect_are_cased_only_in_that_dialect()
    {
        Assert.Equal("CREATE TABLE t (id INT AUTO_INCREMENT)", Sql("create table t (id int auto_increment)", "mysql"));
        Assert.Equal("CREATE TABLE t (id INT auto_increment)", Sql("create table t (id int auto_increment)"));
    }

    [Fact]
    public void Html_entities_encode_the_markup_characters_and_decode_returns_the_same_text()
    {
        Assert.Equal("&lt;a href=&quot;x&quot;&gt;Tom &amp; Jerry&#39;s&lt;/a&gt;", HtmlEntities.Encode("<a href=\"x\">Tom & Jerry's</a>"));
        Assert.Equal("é &amp;amp; &amp;lt;", HtmlEntities.Encode("é &amp; &lt;"));
        Assert.Equal("é & < <", HtmlEntities.Decode("&eacute; &amp; &#60; &#x3C;"));

        var random = new Random(7);
        const string alphabet = "&;#<>\"'ab1 xé中\n\r\t";
        for (int k = 0; k < 20_000; k++)
        {
            var chars = new char[random.Next(0, 24)];
            for (int c = 0; c < chars.Length; c++) chars[c] = alphabet[random.Next(alphabet.Length)];
            var text = new string(chars) + (k % 7 == 0 ? "&amp;lt;&#38;#x41;😀" : "");
            Assert.Equal(text, HtmlEntities.Decode(HtmlEntities.Encode(text)));
        }
    }

    [Fact]
    public void Html_entities_encode_all_writes_characters_outside_ascii_as_numbers_and_decode_returns_the_text()
    {
        Assert.Equal("&lt;p&gt;caf&#233; &#20013; &#128512;&lt;/p&gt;", HtmlEntities.EncodeAll("<p>café 中 😀</p>"));
        Assert.Equal("<p>café 中 😀</p>", HtmlEntities.Decode(HtmlEntities.EncodeAll("<p>café 中 😀</p>")));
    }

    [Fact]
    public void Tidy_is_looked_for_at_the_set_path_next_to_the_plugin_in_the_application_folder_and_on_path_and_the_answer_names_every_place()
    {
        var root = Path.Combine(Path.GetTempPath(), "tidy-locate-" + Guid.NewGuid().ToString("N"));
        var plugin = Path.Combine(root, "app", "plugins", "formatters");
        var onPath = Path.Combine(root, "bin");
        Directory.CreateDirectory(plugin);
        Directory.CreateDirectory(onPath);
        try
        {
            var none = Tidy.Locate(Path.Combine(root, "own", Tidy.ProgramName), plugin, onPath);
            Assert.Null(none.Path);
            Assert.Equal(
                [Path.Combine(root, "own", Tidy.ProgramName), Path.Combine(plugin, "tidy", Tidy.ProgramName), Path.Combine(root, "app", "tools", "tidy", Tidy.ProgramName), Path.Combine(onPath, Tidy.ProgramName)],
                none.Searched);

            string Touch(string folder)
            {
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, Tidy.ProgramName);
                File.WriteAllText(file, "");
                return file;
            }
            var onPathFile = Touch(onPath);
            Assert.Equal(onPathFile, Tidy.Locate("", plugin, onPath).Path);
            var bundled = Touch(Path.Combine(root, "app", "tools", "tidy"));
            Assert.Equal(bundled, Tidy.Locate("", plugin, onPath).Path);
            var beside = Touch(Path.Combine(plugin, "tidy"));
            Assert.Equal(beside, Tidy.Locate("", plugin, onPath).Path);
            var own = Touch(Path.Combine(root, "own"));
            Assert.Equal(own, Tidy.Locate(own, plugin, onPath).Path);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
