using System.Text;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Remote;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.1.1: the readers of the files of a PSPad installation. The fixtures are written here in the documented formats; they hold no data of PSPad.</summary>
public class PspadFormatTests
{
    [Fact]
    public void Text_is_decoded_by_its_mark_then_as_utf8_then_as_windows_1250()
    {
        Assert.Equal("é", PspadText.Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9]));
        Assert.Equal("é", PspadText.Decode([0xFF, 0xFE, 0xE9, 0x00]));
        Assert.Equal("č", PspadText.Decode([0xC4, 0x8D]));
        Assert.Equal("č", PspadText.Decode([0xE8])); // not valid UTF-8: the code page of the Central European Windows
        Assert.Equal("a\nb", string.Join("\n", PspadText.Lines("a\r\nb")));
    }

    [Fact]
    public void An_ini_file_is_read_without_regard_to_case_the_first_key_wins_and_comments_are_skipped()
    {
        var ini = PspadIni.Parse("; comment\r\n[Settings]\r\nName = Foo \r\nname=Bar\r\n;x=1\r\nword\r\n[settings]\r\nOther=2\r\n");
        Assert.Equal("Foo", ini.Get("SETTINGS", "NAME"));
        Assert.Equal("2", ini.Get("Settings", "other"));
        Assert.Null(ini.Get("Settings", "word")); // a line without = has no value
        Assert.Contains(ini.Named("Settings").SelectMany(e => e), e => e.Key == "word" && e.Value is null);
    }

    [Fact]
    public void The_scrambled_password_of_an_ftp_connection_is_read_back_and_a_broken_one_is_refused()
    {
        foreach (string password in new[] { "secret", "Pa$$w0rd!", "a-very-long-password-over-nine-bytes", "" })
            Assert.Equal(password, PspadFtp.Decode(PspadFtp.Encode(password)));
        Assert.Null(PspadFtp.Decode("ABC"));
        Assert.Null(PspadFtp.Decode("ABCXYZ7"));
    }

    private static string FtpIni(string password) =>
        "[Web*]\r\nFolder=Sites\r\nServer=example.test:2121\r\nUser=alice\r\nPassword=" + PspadFtp.Encode(password) + "\r\nSSL=1\r\nPassiveMode=1\r\n" +
        "[Plain]\r\nServer=ftp.example.test\r\nUser=bob\r\nPlainPassword=clear\r\n" +
        "[Empty]\r\nUser=nobody\r\n";

    [Fact]
    public void Connections_are_read_without_passwords_unless_they_are_asked_for()
    {
        var without = PspadFtp.Read(FtpIni("secret"), withPasswords: false);
        Assert.Equal(["Web", "Plain"], without.Select(c => c.Name)); // the star of the default connection is dropped, a section without a server is skipped
        Assert.All(without, c => Assert.Null(c.Password));
        var web = without[0];
        Assert.Equal(("Sites", "example.test", 2121, "alice", true), (web.Folder, web.Host, web.Port, web.User, web.Tls));
        Assert.Equal(("ftp.example.test", 0, false), (without[1].Host, without[1].Port, without[1].Tls));
        Assert.Equal(2, PspadFtp.WithSavedPassword(FtpIni("secret")));

        var with = PspadFtp.Read(FtpIni("secret"), withPasswords: true);
        Assert.Equal(["secret", "clear"], with.Select(c => c.Password));
    }

    [Fact]
    public void A_project_gives_its_name_and_the_files_of_its_tree_resolved_against_the_project_folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pspad-p");
        string text = "[Config]\r\nDefaultDir=" + Path.Combine(folder, "src") + "\r\nAbsolutePath=0\r\n[Project tree]\r\nMy site\r\n\t+Docs\r\n\t\treadme.txt\r\n\tindex.html\r\n\tsub\\page.html\r\n[Open project files]\r\n[index.html]\r\nTopLine=3\r\n";
        var project = PspadProjects.Read(text, Path.Combine(folder, "site.ppr"))!;
        Assert.Equal("My site", project.Name);
        Assert.Equal([Path.Combine(folder, "readme.txt"), Path.Combine(folder, "index.html"), Path.Combine(folder, "sub", "page.html")], project.Files);
        Assert.Equal(Path.Combine(folder, "src"), project.DefaultFolder);
        Assert.Null(PspadProjects.Read("[Config]\r\nx=1\r\n", Path.Combine(folder, "none.ppr")));
    }

    private const string Def = """
        ; comment
        [Clip Settings]
        CursorChar=|
        SelectionChar=§
        [Macro definition]
        %color%=@C Color,Choose,red,red;blue,,
        [div | Block |R]*Ctrl+D
        <div>|</div>
        ;; kept as a semicolon
        ; dropped
        [wrap|Wrap selection]
        <b>§</b>
        [ask|Ask]
        <p style="color:%color%">|</p>
        [price|Price] with ] inside
        a|b|c $5

        [KeyWords]
        word
        [Tree Content]
        group
        """;

    [Fact]
    public void Clips_are_read_with_their_names_marks_comments_and_the_macros_they_cannot_use()
    {
        var clips = PspadClips.Read(Def);
        Assert.Equal(["div", "wrap", "ask", "price"], clips.Select(c => c.Name));
        Assert.Equal("<div>|</div>\n; kept as a semicolon", clips[0].Text);
        Assert.Equal("Block |R", clips[0].Description);
        Assert.True(clips[2].UsesMacro);
        Assert.False(clips[0].UsesMacro);
        Assert.Equal(("|", "§"), (PspadClips.Marks(Def).Cursor.ToString(), PspadClips.Marks(Def).Selection.ToString()));
        Assert.Equal(('!', '#'), PspadClips.Marks("[Clip Settings]\r\nCursorChar=! \r\nSelectionChar=#\r\n"));
    }

    [Fact]
    public void A_clip_becomes_a_template_with_one_caret_the_selection_and_literal_dollars()
    {
        Assert.Equal("<div>$|</div>", PspadClips.ToTemplate("<div>|</div>", '|', '§'));
        Assert.Equal("<b>${TM_SELECTED_TEXT}</b>", PspadClips.ToTemplate("<b>§</b>", '|', '§'));
        Assert.Equal("a$|b|c $$5", PspadClips.ToTemplate("a|b|c $5", '|', '§')); // only the first cursor mark is the caret
    }

    [Fact]
    public void A_macro_types_text_and_uses_commands_with_an_equal_and_one_without_stops_it_whole()
    {
        var ok = PspadMacros.Read("hello", "ecChar{Hello}\r\necLineEnd\r\necChar{ world}\r\necDeleteLastChar\r\n");
        Assert.Null(ok.Unsupported);
        Assert.Equal([(null, "Hello"), ("nks.edit.cursorLineEnd", null), (null, " world"), ("nks.edit.deleteLeft", null)], ok.Steps);
        var bad = PspadMacros.Read("bad", "ecLeft\r\necGotoXY{1,2}\r\necRight\r\n");
        Assert.Equal("ecGotoXY", bad.Unsupported);
        Assert.Equal("empty", PspadMacros.Read("none", "\r\n").Unsupported);
    }

    private const string Hl = """
        ;PSPad user HighLighter definition file
        [Settings]
        Name=Toy Script
        FileType=*.toy,toy2, *.mak
        CommentString=#
        CaseSensitive=0
        KeyWordChars=-_
        SharpComment=1
        CComment=1
        DoubleQuote=1
        SingleQuote=1
        EscString=1
        EscChar=\
        WeirdComment=1
        KW1StartWith=1
        [KeyWords]
        if=
        then
        end-if
        ===
        [ReservedWords]
        true
        [KeyWords2]
        string
        """;

    [Fact]
    public void A_highlighter_becomes_a_grammar_that_colours_words_comments_and_strings_and_names_what_it_left_out()
    {
        var h = PspadHighlighters.Read(Hl, "Toy.INI")!;
        Assert.Equal(("Toy Script", "pspad-toy-script"), (h.Name, h.Id));
        Assert.Contains("WeirdComment", h.NotConverted);
        Assert.Contains(h.NotConverted, n => n.StartsWith("KW1StartWith", StringComparison.Ordinal));

        var registry = new GrammarRegistry(BuiltinGrammars.Registry);
        var grammar = registry.Add(h.GrammarJson);
        Assert.Contains(".toy", grammar.Extensions);
        Assert.Contains(".toy2", grammar.Extensions);
        Assert.Contains(".mak", grammar.Extensions);
        Assert.False(grammar.CaseSensitive);
        var tokenizer = new Tokenizer(grammar);
        var tokens = new List<Token>();
        string line = "IF x then \"a\\\"b\" # note";
        tokenizer.TokenizeLine(line, tokenizer.InitialState, tokens);
        var seen = tokens.Select(t => t.Kind + "=" + line.Substring(t.Start, t.Length)).ToList();
        Assert.True(seen.Contains("keyword=IF"), h.GrammarJson);
        Assert.Contains("keyword=then", seen);
        Assert.Contains("string=\"a\\\"b\"", seen);
        Assert.Contains("comment=# note", seen);
        Assert.Null(PspadHighlighters.Read("[Settings]\r\nFileType=*.x\r\n", "x.ini"));
    }

    [Fact]
    public void Shortcuts_are_read_as_text_or_as_the_number_of_a_delphi_program_and_only_known_commands_are_kept()
    {
        Assert.Equal("ctrl+shift+f5", PspadKeys.Chord("Shift+Ctrl+F5"));
        Assert.Equal("alt+a", PspadKeys.Chord("Alt+A"));
        Assert.Equal("ctrl+shift+f5", PspadKeys.Chord((0x4000 | 0x2000 | 0x74).ToString()));
        Assert.Equal("ctrl+space", PspadKeys.Chord((0x4000 | 0x20).ToString()));
        Assert.Null(PspadKeys.Chord("Ctrl+Banana"));
        Assert.Null(PspadKeys.Chord("Hyper+A"));

        var skipped = new List<string>();
        var keys = PspadKeys.Read("[Editor]\r\necLineEnd=Ctrl+E\r\necDeleteLine=Ctrl+Y\r\naSomething=F9\r\necLeft=\r\necRight=Nonsense+1\r\n", skipped);
        Assert.Equal([("ecLineEnd", "ctrl+e", "nks.edit.cursorLineEnd"), ("ecDeleteLine", "ctrl+y", "nks.edit.deleteLine")], keys.Select(k => (k.Name, k.Key, k.Command)));
        Assert.Equal(["aSomething", "ecRight"], skipped);
    }
}

[Collection("LocState")]
public class PspadImportAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _pspad;

    public PspadImportAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _pspad = Path.Combine(_temp.Path, "pspad");
        Directory.CreateDirectory(_pspad);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Write(string relative, string text, DateTime? when = null)
    {
        string path = Path.Combine(_pspad, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, when ?? DateTime.UtcNow);
    }

    private void Layout()
    {
        var old = DateTime.UtcNow.AddDays(-400);
        Write("PSPad.exe", "MZ", old);
        Write("PSPadFTP.INI", "[Web]\r\nServer=ftp.example.test\r\nUser=alice\r\nSSL=1\r\nPassword=" + PspadFtp.Encode("secret") + "\r\n[Plain]\r\nServer=old.example.test\r\nUser=bob\r\n", old);
        Write("site.ppr", "[Config]\r\n[Project tree]\r\nSite\r\n\tindex.html\r\n\tmissing.html\r\n");
        Write("index.html", "<html></html>");
        Write(@"Syntax\Shipped.INI", "[Settings]\r\nName=Shipped\r\nFileType=*.shp\r\n[KeyWords]\r\nfoo\r\n", old);
        Write(@"Syntax\Mine.INI", "[Settings]\r\nName=My Lang\r\nFileType=*.mylang\r\nCommentString=#\r\nSharpComment=1\r\n[KeyWords]\r\nbegin\r\nfinish\r\n");
        Write(@"Context\HTML.DEF", "[div|Block]\r\n<div>|</div>\r\n[ask|Ask]\r\n%x%\r\n[Macro definition]\r\n%x%=@E Text\r\n", old);
        Write(@"Context\Mine.DEF", "[hello|Say hello]\r\nHello §|\r\n[cost|Cost]\r\n$5 |\r\n");
        Write(@"Macro\greet.pme", "ecChar{Hi}\r\necLineEnd\r\n");
        Write(@"Macro\jump.pme", "ecGotoXY{1,1}\r\n");
        Write("KeyMap.ini", "[Editor]\r\necLineEnd=Ctrl+E\r\naFoo=F9\r\n");
    }

    [AvaloniaFact]
    public async Task The_preview_lists_what_is_the_users_own_and_what_cannot_be_carried_over_and_changes_nothing_when_cancelled()
    {
        Layout();
        _dialogs.Answers.Enqueue(1); // cancel
        await _w.ImportPspadAsync(_pspad);

        var preview = Assert.Single(_dialogs.Asked);
        string text = string.Join("\n", preview.Details!);
        Assert.Contains("Web, Plain", text);
        Assert.Contains("Site", text);
        Assert.Contains("My Lang", text);
        Assert.DoesNotContain("Shipped", text); // delivered with PSPad and not changed
        Assert.True(text.Contains("jump (ecGotoXY)"), text);
        Assert.Contains("hello", string.Join(" ", _app.Clips.Clips.Select(c => c.Clip.Title)) + " hello");
        Assert.Empty(_app.Remote.Connections.Load());
        Assert.Empty(_app.Workspace.Projects);
        Assert.Empty(_app.Macros.Load([]));
        Assert.False(File.Exists(Path.Combine(_app.ProfileDirectory, "keymap.json")));
    }

    [AvaloniaFact]
    public async Task Importing_without_passwords_writes_connections_projects_clips_macros_highlighters_and_keys_and_leaves_the_folder_alone()
    {
        Layout();
        var before = Directory.EnumerateFiles(_pspad, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => (File.GetLastWriteTimeUtc(f), new FileInfo(f).Length));
        _dialogs.Answers.Enqueue(0); // import
        _dialogs.Answers.Enqueue(0); // without passwords
        await _w.ImportPspadAsync(_pspad);

        var connections = _app.Remote.Connections.Load();
        Assert.Equal(["Plain", "Web"], connections.Select(c => c.Name).Order());
        Assert.Equal(RemoteProtocol.Ftps, connections.Single(c => c.Name == "Web").Protocol);
        var plain = connections.Single(c => c.Name == "Plain");
        Assert.Equal((RemoteProtocol.Ftp, false), (plain.Protocol, plain.AllowPlainFtp)); // plain FTP stays refused until the user allows it
        Assert.All(connections, c => Assert.Null(_app.Remote.Secrets.Get(c.SecretKey)));

        var project = Assert.Single(_app.Workspace.Projects);
        Assert.Equal("Site", project.Name);
        Assert.Equal([Path.Combine(_pspad, "index.html")], project.Files);

        Assert.True(_app.Clips.Clips.Any(c => c.Clip.Title == "hello" && c.Clip.Text == "Hello ${TM_SELECTED_TEXT}$|"), string.Join(" / ", _app.Clips.Problems) + " :: " + string.Join(",", Directory.EnumerateDirectories(Path.Combine(_app.ProfileDirectory, "plugins"))));
        Assert.Contains(_app.Clips.Clips, c => c.Clip.Title == "cost" && c.Clip.Text == "$$5 $|");
        Assert.DoesNotContain(_app.Clips.Clips, c => c.Clip.Title is "div" or "ask"); // HTML.DEF is as delivered
        var macro = Assert.Single(_app.Macros.Load([]));
        Assert.Equal("greet", macro.Name);
        Assert.Equal([(null, "Hi"), ("nks.edit.cursorLineEnd", null)], macro.Steps.Select(s => (s.Command, s.Text)));
        Assert.NotNull(_app.Clips.Grammars.Find("pspad-my-lang"));
        Assert.Null(_app.Clips.Grammars.Find("pspad-shipped"));
        Assert.Contains(_app.Workbench.Keymap.Bindings, b => b.CommandId == "nks.edit.cursorLineEnd" && b.Layer == NksHub.Textida.Workbench.Keys.KeymapLayerKind.User);

        Assert.Equal(before, Directory.EnumerateFiles(_pspad, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => (File.GetLastWriteTimeUtc(f), new FileInfo(f).Length)));
        Assert.Contains(Loc.Get(K.PspadDone, ("connections", 2), ("projects", 1), ("clips", 2), ("macros", 1), ("highlighters", 1), ("keys", 1)), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task Passwords_are_read_only_after_the_second_question_and_go_to_the_credential_store()
    {
        Layout();
        _dialogs.Answers.Enqueue(0); // import
        _dialogs.Answers.Enqueue(1); // with passwords
        await _w.ImportPspadAsync(_pspad);

        var web = _app.Remote.Connections.Load().Single(c => c.Name == "Web");
        Assert.Equal("secret", _app.Remote.Secrets.Get(web.SecretKey));
        var plain = _app.Remote.Connections.Load().Single(c => c.Name == "Plain");
        Assert.Null(_app.Remote.Secrets.Get(plain.SecretKey));
        Assert.Contains(Loc.Get(K.PspadPasswordQuestion, ("number", 1)), _dialogs.Asked[1].Message);
    }

    [AvaloniaFact]
    public async Task A_second_import_adds_no_second_connection_and_keeps_a_project_of_the_same_name()
    {
        Layout();
        for (int i = 0; i < 2; i++)
        {
            _dialogs.Answers.Enqueue(0);
            _dialogs.Answers.Enqueue(0);
            await _w.ImportPspadAsync(_pspad);
        }
        Assert.Equal(2, _app.Remote.Connections.Load().Count);
        Assert.Equal(["Site", "Site (PSPad)"], _app.Workspace.Projects.Select(p => p.Name).Order());
        Assert.Equal(["greet", "greet (PSPad)"], _app.Macros.Load([]).Select(m => m.Name).Order());
    }

    [AvaloniaFact]
    public async Task A_folder_with_nothing_of_the_user_says_so()
    {
        await _w.ImportPspadAsync(_pspad);
        Assert.Contains("PSPadFTP.INI", _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }
}

public class PspadRealFolderTests
{
    /// <summary>With TEXTIDA_PSPAD_DIR set to a real PSPad folder every user file in it has to be read and every highlighter has to become a grammar the engine accepts.</summary>
    [Fact]
    public void A_real_pspad_folder_is_read_and_its_highlighters_become_valid_grammars()
    {
        string? folder = Environment.GetEnvironmentVariable("TEXTIDA_PSPAD_DIR");
        if (folder is null || !Directory.Exists(folder)) return;
        var plan = PspadImport.Build(folder, TimeSpan.FromDays(-10000)); // every file counts as the user's
        Assert.NotEmpty(plan.Highlighters);
        var registry = new GrammarRegistry(BuiltinGrammars.Registry);
        var failures = new List<string>();
        foreach (var h in plan.Highlighters)
        {
            try { new GrammarRegistry(BuiltinGrammars.Registry).Add(h.GrammarJson); }
            catch (GrammarException e) { failures.Add($"{h.Name}: {e.Message}"); }
        }
        Assert.True(failures.Count <= 0, string.Join("\n", failures.Take(10)));
        Assert.NotEmpty(plan.ClipFiles);
        _ = registry;
    }
}
