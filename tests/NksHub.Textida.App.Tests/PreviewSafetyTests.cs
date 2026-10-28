using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Polygon = Avalonia.Controls.Shapes.Polygon;
using Polyline = Avalonia.Controls.Shapes.Polyline;

namespace NksHub.Textida.App.Tests;

/// <summary>A listener on the loopback address that counts the connections made to it and answers each request with what <see cref="Respond"/> returns for its first line.</summary>
internal sealed class CountingServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _connections;

    public Func<string, byte[]> Respond { get; set; } = _ => Http.Status(404);

    public CountingServer()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public string Authority => "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections => Volatile.Read(ref _connections);

    private async Task AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (Exception e) when (e is ObjectDisposedException or SocketException or InvalidOperationException) { return; }
            Interlocked.Increment(ref _connections);
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var received = new StringBuilder();
                var buffer = new byte[4096];
                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0) return;
                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                await stream.WriteAsync(Respond(received.ToString().Split("\r\n")[0]));
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
    }

    public void Dispose() => _listener.Stop();
}

internal static class Http
{
    public static byte[] Ok(byte[] body, string type = "image/png") =>
        [.. Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), .. body];

    public static byte[] Redirect(string url) => Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {url}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    public static byte[] Status(int code) => Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Status\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
}

/// <summary>
/// P6.2.7 and P9.3.1: the safety policy of the preview, proved by what the program does. A script never runs and nothing is fetched without the user's permission (a listener counts the
/// connections), a link to a local file is never opened, local pictures stay inside the folder of the document and are read off the UI thread, a flowchart is drawn without a script engine,
/// and the editor and the preview scroll together and can be used from the keyboard and a screen reader.
/// </summary>
[Collection("LocState")]
public class PreviewSafetyTests : IDisposable
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PreviewSafetyTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static byte[] Png => Convert.FromBase64String(OnePixelPng);

    private DocTab Open(string text, string language)
    {
        var tab = _w.NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById(language));
        tab.Editor!.Document.Text = text;
        return tab;
    }

    private Control Body()
    {
        _w.UpdateLayout();
        return _w.PreviewBody!;
    }

    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static string TextOf(Control root) => string.Join("\n", Tree(root).OfType<TextBlock>().Select(t =>
        t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? ""));

    private static IEnumerable<Run> RunsOf(Control root) => Tree(root).OfType<TextBlock>().Where(t => t.Inlines is not null).SelectMany(t => t.Inlines!.OfType<Run>());

    private static string NameOf(Control c) => AutomationProperties.GetName(c) ?? "";

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 10_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    /// <summary>Lets the background work and the timers run, so that something that would have happened has happened.</summary>
    private static async Task Settle(int milliseconds = 400)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // ---- the policy as functions ----

    [Fact]
    public void A_local_picture_is_a_relative_path_inside_the_folder_and_nothing_else()
    {
        string dir = Path.Combine(_temp.Path, "docs");
        Assert.Equal(Path.Combine(dir, "a.png"), PreviewPolicy.LocalImagePath(dir, "a.png"));
        Assert.Equal(Path.Combine(dir, "sub", "a b.png"), PreviewPolicy.LocalImagePath(dir, "sub/a%20b.png"));
        Assert.Equal(Path.Combine(dir, "a.png"), PreviewPolicy.LocalImagePath(dir, "./a.png?raw=true#frag"));
        Assert.Equal(Path.Combine(dir, "a.png"), PreviewPolicy.LocalImagePath(dir, "sub/../a.png"));
        string[] refused =
        [
            "../a.png", "sub/../../a.png", "%2e%2e/a.png", "/etc/passwd", "\\\\server\\share\\a.png", "//server/share/a.png", "C:\\x.png", "C:x.png", "file:///C:/x.png",
            "a.png:stream", "http://x.example/a.png", "%3A", "a\u0000.png", "", "  ",
        ];
        Assert.All(refused, target => Assert.Null(PreviewPolicy.LocalImagePath(dir, target)));
        Assert.Null(PreviewPolicy.LocalImagePath(null, "a.png"));
        Assert.Null(PreviewPolicy.LocalImagePath("", "a.png"));
    }

    [Fact]
    public void A_remote_picture_is_a_web_address_without_a_user_name_and_consent_is_kept_per_host_and_port()
    {
        Assert.Equal("http://x.example/a.png", PreviewPolicy.RemoteImage("http://x.example/a.png")!.AbsoluteUri);
        Assert.NotNull(PreviewPolicy.RemoteImage(" https://x.example:8443/a.png "));
        string[] refused = ["ftp://x.example/a.png", "file:///C:/a.png", "data:image/png;base64,AAAA", "//x.example/a.png", "a.png", "https://user:pw@x.example/a.png", "javascript:alert(1)", ""];
        Assert.All(refused, target => Assert.Null(PreviewPolicy.RemoteImage(target)));

        Assert.Equal("x.example:8443", PreviewPolicy.ConsentKey(new Uri("https://X.example:8443/a.png")));
        Assert.Equal("x.example", PreviewPolicy.ConsentKey(new Uri("https://X.example/a.png")));
        Assert.Equal("x.example", PreviewPolicy.ConsentKey(new Uri("http://x.example:80/b.png")));
    }

    [Fact]
    public void A_plugin_picture_is_named_by_preview_scheme_and_a_picture_must_be_a_known_raster_of_a_sane_size()
    {
        Assert.Equal("logo", PreviewPolicy.AttachedName("preview:logo"));
        Assert.Equal("logo", PreviewPolicy.AttachedName(" PREVIEW:logo "));
        Assert.Null(PreviewPolicy.AttachedName("preview:"));
        Assert.Null(PreviewPolicy.AttachedName("logo"));
        Assert.Null(PreviewPolicy.AttachedName("https://x.example/preview:logo"));

        Assert.True(PreviewPolicy.Drawable(Png));
        Assert.True(PreviewPolicy.Drawable(WithSize(Png, 8000, 5000)));
        Assert.False(PreviewPolicy.Drawable(WithSize(Png, 8001, 5000)));
        Assert.False(PreviewPolicy.Drawable(WithSize(Png, 50000, 50000)));
        Assert.False(PreviewPolicy.Drawable(WithSize(Png, 0, 10)));
        Assert.False(PreviewPolicy.Drawable(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>1</script></svg>")));
        Assert.False(PreviewPolicy.Drawable(Png[..20]));
    }

    private static byte[] WithSize(byte[] png, int width, int height)
    {
        var copy = (byte[])png.Clone();
        BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(width)).CopyTo(copy, 16);
        BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(height)).CopyTo(copy, 20);
        return copy;
    }

    [Fact]
    public void The_scroll_map_is_straight_between_blocks_and_runs_both_ways()
    {
        var map = new PreviewScrollMap([(0, 0.0), (10, 100.0), (30, 140.0)], lineCount: 40, height: 400);
        double[][] pairs = [[0, 0], [5, 50], [10, 100], [20, 120], [30, 140], [35, 270], [40, 400], [100, 400]];
        foreach (var pair in pairs) Assert.Equal(pair[1], map.OffsetFor(pair[0]), 0.001);
        foreach (var pair in pairs.Take(7)) Assert.Equal(pair[0], map.LineAt(pair[1]), 0.001);
        Assert.Equal(40, map.LineAt(1000), 0.001);
        Assert.Equal(0, map.LineAt(-5), 0.001);
    }

    [Fact]
    public void The_scroll_map_drops_anchors_that_run_backwards_and_works_without_any()
    {
        var map = new PreviewScrollMap([(5, 50.0), (3, 30.0), (8, 20.0), (9, 90.0)], lineCount: 20, height: 200);
        Assert.Equal(80, map.OffsetFor(8), 0.001); // the anchor at line 8 sits above the one at line 5 and is ignored
        var plain = new PreviewScrollMap([], lineCount: 100, height: 1000);
        Assert.Equal(500, plain.OffsetFor(50), 0.001);
        Assert.Equal(50, plain.LineAt(500), 0.001);
        var flat = new PreviewScrollMap([(10, 100.0), (12, 100.0)], lineCount: 20, height: 300);
        Assert.Equal(10, flat.LineAt(100), 0.001);
    }

    // ---- the web ----

    [AvaloniaFact]
    public async Task A_remote_picture_makes_no_connection_until_the_user_allowed_its_host_then_it_is_fetched_once_and_kept()
    {
        using var server = new CountingServer { Respond = _ => Http.Ok(Png) };
        string url = $"http://{server.Authority}/a.png";
        var tab = Open($"![chart]({url}) and ![again]({url})\n\n<img src=\"{url}\"> <script src=\"{url}\"></script>\n", "markdown");
        _w.ShowLivePreview(outline: false);
        await Settle();

        Assert.Equal(0, server.Connections);
        var buttons = Tree(Body()).OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal(Loc.Get(K.PreviewImageRemote, ("host", server.Authority), ("alt", "chart")), NameOf(buttons[0]));
        Assert.Contains($"<img src=\"{url}\"> <script src=\"{url}\"></script>", TextOf(Body())); // raw HTML inside Markdown is text, not a request

        // the user says no: still no connection and the buttons stay
        _dialogs.Answers.Enqueue(1);
        Click(buttons[0]);
        await Until(() => _dialogs.Asked.Count == 1, "the question");
        Assert.Equal(Loc.Get(K.PreviewImageConsentTitle), _dialogs.Asked[0].Title);
        Assert.Contains(server.Authority, _dialogs.Asked[0].Message);
        await Settle();
        Assert.Equal(0, server.Connections);
        Assert.Equal(2, Tree(Body()).OfType<Button>().Count());
        Assert.Empty(Tree(Body()).OfType<Image>());

        // the user says yes: both references are drawn from one request
        _dialogs.Answers.Enqueue(0);
        Click(buttons[0]);
        await Until(() => Tree(Body()).OfType<Image>().Count() == 2, "both pictures");
        Assert.Equal(1, server.Connections);
        Assert.Empty(Tree(Body()).OfType<Button>());
        Assert.Equal(["again", "chart"], Tree(Body()).OfType<Image>().Select(NameOf).Order().ToList());

        // drawing the preview again, as typing does, neither asks nor connects
        tab.Editor!.Document.Insert(0, "x");
        await Settle(PluginLimitsPlaceholders.PreviewLiveDebounceMs + 400);
        Assert.Contains("x and ", TextOf(Body()));
        Assert.Equal(2, Tree(Body()).OfType<Image>().Count());
        Assert.Equal(1, server.Connections);
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task A_redirect_is_not_followed_and_an_answer_that_is_not_a_picture_is_a_note()
    {
        using var other = new CountingServer { Respond = _ => Http.Ok(Png) };
        using var server = new CountingServer();
        server.Respond = line => line.Contains("/redirect", StringComparison.Ordinal) ? Http.Redirect($"http://{other.Authority}/b.png")
            : line.Contains("/svg", StringComparison.Ordinal) ? Http.Ok(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>1</script></svg>"), "image/svg+xml")
            : line.Contains("/missing", StringComparison.Ordinal) ? Http.Status(404)
            : Http.Ok(Png);
        string Url(string name) => $"http://{server.Authority}/{name}";
        Open($"![r]({Url("redirect")}) ![s]({Url("svg")}) ![m]({Url("missing")}) ![ok]({Url("ok.png")})\n", "markdown");
        _w.ShowLivePreview(outline: false);
        await Settle();
        Assert.Equal(0, server.Connections);

        _dialogs.Answers.Enqueue(0);
        Click(Tree(Body()).OfType<Button>().First());
        await Until(() => Tree(Body()).OfType<Image>().Count() == 1 && Tree(Body()).OfType<Button>().Count() == 0
            && new[] { "r", "s", "m" }.All(alt => TextOf(Body()).Contains(Loc.Get(K.PreviewPanelImage, ("alt", alt)), StringComparison.Ordinal)), "the answers");
        Assert.Equal(4, server.Connections);
        Assert.Equal(0, other.Connections); // the redirect to another host was not followed
        Assert.Equal(["ok"], Tree(Body()).OfType<Image>().Select(NameOf).ToList());
    }

    [AvaloniaFact]
    public async Task Active_content_in_html_never_runs_and_never_connects_and_the_rest_of_the_page_is_drawn()
    {
        using var server = new CountingServer { Respond = _ => Http.Ok(Png) };
        string u = $"http://{server.Authority}";
        string page = string.Join("\n",
            $"<!DOCTYPE html><html><head><title>t</title><link rel=\"stylesheet\" href=\"{u}/s.css\"><style>@import url({u}/i.css);</style></head>",
            $"<body onload=\"fetch('{u}/x')\"><script src=\"{u}/x.js\"></script><script>fetch('{u}/y')</script>",
            "<h2>Heading</h2>",
            "<p>Some <b>bold</b>, <i>italic</i> and <code>code</code> with <a href=\"https://docs.example.org/a\">a web link</a>, <a href=\"file:///C:/private/n.txt\">a file link</a> and <a href=\"javascript:alert(1)\">a script link</a>.</p>",
            "<ul><li>one</li><li>two<ul><li>nested</li></ul></li></ul>",
            "<table><tr><th>Name</th><th>Qty</th></tr><tr><td>apple</td><td>3</td></tr></table>",
            "<pre>raw  text\n  kept</pre>",
            $"<iframe src=\"{u}/f.html\"></iframe><object data=\"{u}/o\"></object><form action=\"{u}/post\"><input></form>",
            $"<img src=\"{u}/a.png\" alt=\"far\" onerror=\"fetch('{u}/z')\">",
            "</body></html>");
        Open(page, "html");
        _w.ShowLivePreview(outline: false);
        await Settle();

        var body = Body();
        var text = TextOf(body);
        Assert.Equal(0, server.Connections);
        Assert.DoesNotContain(Tree(body), c => c is Image);

        // what is active is shown as the text it is
        foreach (var literal in new[]
        {
            $"<script src=\"{u}/x.js\"></script>", $"<script>fetch('{u}/y')</script>", $"<link rel=\"stylesheet\" href=\"{u}/s.css\">", $"<style>@import url({u}/i.css);</style>",
            $"<body onload=\"fetch('{u}/x')\">", $"<iframe src=\"{u}/f.html\">", $"<object data=\"{u}/o\">", $"<form action=\"{u}/post\">", "<!DOCTYPE html>",
        })
            Assert.Contains(literal, text);

        // what is in the subset is drawn
        Assert.Equal("Heading", Assert.Single(Tree(body).OfType<SelectableTextBlock>(), t => t.Classes.Contains("previewhead2")).Inlines!.OfType<Run>().Single().Text);
        var runs = RunsOf(body).ToList();
        Assert.Contains(runs, r => r.Text == "bold" && r.FontWeight == FontWeight.Bold);
        Assert.Contains(runs, r => r.Text == "italic" && r.FontStyle == FontStyle.Italic);
        Assert.Contains(runs, r => r.Text == "code" && r.IsSet(Run.FontFamilyProperty));
        Assert.Contains("• one", text);
        Assert.Contains("• two", text);
        Assert.Contains("• nested", text);
        var grid = Assert.Single(Tree(body).OfType<Grid>());
        Assert.Equal((2, 2), (grid.ColumnDefinitions.Count, grid.RowDefinitions.Count));
        Assert.Equal(Loc.Get(K.PreviewTableName, ("rows", 2), ("columns", 2)), NameOf(grid));
        var cells = grid.Children.OfType<Border>().ToDictionary(b => (Grid.GetRow(b), Grid.GetColumn(b)), b => TextOf(b));
        Assert.Equal(["Name", "Qty", "apple", "3"], new[] { cells[(0, 0)], cells[(0, 1)], cells[(1, 0)], cells[(1, 1)] });
        Assert.Contains(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewcode") && TextOf(b) == "raw  text\n  kept");

        // links: the web link is a button, the file and script links are text and never a button
        var webLink = Loc.Get(K.PreviewPanelLink, ("text", "a web link"), ("url", "https://docs.example.org/a"));
        var buttons = Tree(body).OfType<Button>().Select(NameOf).ToList();
        Assert.Contains(webLink, buttons);
        Assert.Contains(Loc.Get(K.PreviewImageRemote, ("host", server.Authority), ("alt", "far")), buttons);
        Assert.Equal(2, buttons.Count);
        Assert.Contains(Loc.Get(K.PreviewPanelLink, ("text", "a file link"), ("url", "file:///C:/private/n.txt")), text);
        Assert.Contains(Loc.Get(K.PreviewPanelLink, ("text", "a script link"), ("url", "javascript:alert(1)")), text);
    }

    // ---- local files ----

    [AvaloniaFact]
    public async Task A_picture_next_to_the_document_is_read_off_the_UI_thread_and_kept_and_anything_outside_the_folder_is_refused()
    {
        string docs = Path.Combine(_temp.Path, "docs"), outside = Path.Combine(_temp.Path, "outside.png");
        Directory.CreateDirectory(Path.Combine(docs, "sub"));
        File.WriteAllBytes(Path.Combine(docs, "pic.png"), Png);
        File.WriteAllBytes(Path.Combine(docs, "sub", "two.png"), Png);
        File.WriteAllBytes(outside, Png);
        File.WriteAllBytes(Path.Combine(docs, "bomb.png"), WithSize(Png, 50000, 50000)); // a few bytes that claim 2.5 billion pixels
        File.WriteAllText(Path.Combine(docs, "notimage.png"), "this is text, not a picture");
        string path = Path.Combine(docs, "doc.md");
        File.WriteAllText(path, $"![a](pic.png) ![b](sub/two.png) ![c](../outside.png) ![d]({new Uri(outside).AbsoluteUri}) ![e](missing.png) ![f](bomb.png) ![g](notimage.png) ![h]({outside.Replace('\\', '/')})\n");
        _w.OpenFile(path, null);
        _w.ShowLivePreview(outline: false);

        // nothing has been read or decoded when the drawing returns, so the UI thread did none of it
        Assert.Empty(Tree(Body()).OfType<Image>());
        await Until(() => Tree(Body()).OfType<Image>().Count() == 2, "the two pictures");
        Assert.Equal(["a", "b"], Tree(Body()).OfType<Image>().Select(NameOf).ToList());
        string[] notes = ["c", "d", "e", "f", "g", "h"];
        await Until(() => notes.All(alt => TextOf(Body()).Contains(Loc.Get(K.PreviewPanelImage, ("alt", alt)), StringComparison.Ordinal)), "the notes");

        // drawing again, as typing does, shows the kept pictures at once
        _w.RefreshLive();
        Assert.Equal(2, Tree(Body()).OfType<Image>().Count());
    }

    [AvaloniaFact]
    public async Task A_picture_the_plugin_preview_names_by_a_relative_path_is_not_read_even_when_the_file_exists()
    {
        File.WriteAllBytes(Path.Combine(_temp.Path, "pic.png"), Png);
        _w.ShowPreview(new PreviewContent("p", Guid.NewGuid(), 1, PreviewFormats.Markdown, "t", "![near](pic.png)"));
        await Settle();
        Assert.Empty(Tree(Body()).OfType<Image>());
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "near")), TextOf(Body()));
    }

    // ---- diagrams ----

    [AvaloniaFact]
    public async Task A_flowchart_is_drawn_with_shapes_is_named_for_a_screen_reader_and_a_diagram_directive_does_not_connect()
    {
        using var server = new CountingServer { Respond = _ => Http.Ok(Png) };
        Open("# Plan\n\n```mermaid\ngraph TD\n  A[Start] --> B{Ready?}\n  B -->|yes| C((Go))\n  B -->|no| D(Wait)\n  D --> B\n  click A href \"http://" + server.Authority + "/x\"\n```\n", "markdown");
        _w.ShowLivePreview(outline: false);
        await Settle();

        var body = Body();
        var canvas = Assert.Single(Tree(body).OfType<Canvas>());
        Assert.True(canvas.Width > 0 && canvas.Height > 0);
        var texts = Tree(canvas).OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var expected in new[] { "Start", "Ready?", "Go", "Wait", "yes", "no" }) Assert.Contains(expected, texts);
        Assert.Equal(4, Tree(canvas).OfType<Polyline>().Count());
        Assert.Equal(4, Tree(canvas).OfType<Polygon>().Count(p => p.Classes.Contains("mermaidarrow")));
        Assert.Single(Tree(canvas).OfType<Polygon>(), p => p.Classes.Contains("mermaidshape")); // the diamond
        Assert.Single(Tree(canvas).OfType<Ellipse>());
        Assert.Equal(2, Tree(canvas).OfType<Border>().Count(b => b.Classes.Contains("mermaidshape")));

        var scroll = Assert.Single(Tree(body).OfType<ScrollViewer>(), s => ReferenceEquals(s.Content, canvas));
        Assert.True(scroll.Focusable);
        var name = NameOf(scroll);
        Assert.Contains(Loc.Get(K.PreviewMermaidEdge, ("from", "Start"), ("to", "Ready?")), name);
        Assert.Contains(Loc.Get(K.PreviewMermaidEdgeLabel, ("from", "Ready?"), ("to", "Go"), ("label", "yes")), name);
        Assert.Equal(0, server.Connections);
    }

    [AvaloniaFact]
    public async Task A_diagram_the_subset_does_not_know_is_shown_as_its_source_and_a_flowchart_in_html_is_drawn()
    {
        Open("<pre class=\"mermaid\">graph LR\nA[In] --&gt; B[Out]</pre>\n<pre><code class=\"language-mermaid\">pie title Pets\n\"Dogs\" : 3</code></pre>\n<pre>graph LR\nX --&gt; Y</pre>", "html");
        _w.ShowLivePreview(outline: false);
        await Settle();

        var body = Body();
        var canvas = Assert.Single(Tree(body).OfType<Canvas>());
        Assert.Contains("In", TextOf(canvas));
        Assert.Contains("Out", TextOf(canvas));
        var text = TextOf(body);
        Assert.Contains(Loc.Get(K.PreviewMermaidUnsupported), text);
        Assert.Contains("pie title Pets", text);
        Assert.Contains("graph LR\nX --> Y", text); // a plain code block stays code
    }

    // ---- scrolling and the keyboard ----

    private static string LongDocument() => string.Concat(Enumerable.Range(1, 120).Select(i => $"Paragraph number {i}\n\n"));

    [AvaloniaFact]
    public async Task The_editor_and_the_preview_scroll_together_in_both_directions()
    {
        var tab = Open(LongDocument(), "markdown");
        _w.ShowLivePreview(outline: false);
        var scroller = _w.PreviewScroller;
        var editor = tab.Surface!;
        var scrollable = (ILogicalScrollable)editor.TextArea.TextView;
        await Until(() =>
        {
            _w.UpdateLayout();
            return scroller.Viewport.Height > 0 && scroller.Extent.Height > scroller.Viewport.Height + 200 && scrollable.Viewport.Height > 0 && scrollable.Extent.Height > scrollable.Viewport.Height;
        }, "both to be laid out");
        await Settle(300);
        int lines = editor.Document.LineCount;

        // the editor scrolls, the preview follows to about the same place
        scrollable.Offset = new Vector(0, editor.TextArea.TextView.GetVisualTopByDocumentLine(lines / 2));
        await Until(() => scroller.Offset.Y > 0, "the preview to follow the editor");
        double share = scroller.Offset.Y / (scroller.Extent.Height - scroller.Viewport.Height);
        Assert.InRange(share, 0.3, 0.8);

        // the preview scrolls back to the top, the editor follows
        await Settle(PluginLimitsPlaceholders.PreviewSyncQuietMs + 150);
        scroller.Offset = new Vector(0, 0);
        await Until(() => MainWindow.TopLine(editor) < 5, "the editor to follow the preview");

        // and to the middle
        await Settle(PluginLimitsPlaceholders.PreviewSyncQuietMs + 150);
        scroller.Offset = new Vector(0, (scroller.Extent.Height - scroller.Viewport.Height) / 2);
        await Until(() => MainWindow.TopLine(editor) > lines * 0.25, "the editor to reach the middle");
        Assert.InRange(MainWindow.TopLine(editor) / lines, 0.25, 0.75);
    }

    [AvaloniaFact]
    public async Task The_preview_scrolls_from_the_keyboard()
    {
        Open(LongDocument(), "markdown");
        _w.ShowLivePreview(outline: false);
        var scroller = _w.PreviewScroller;
        await Until(() =>
        {
            _w.UpdateLayout();
            return scroller.Viewport.Height > 0 && scroller.Extent.Height > scroller.Viewport.Height + 200;
        }, "the preview to be laid out");

        Assert.True(scroller.Focusable);
        scroller.Focus();
        Dispatcher.UIThread.RunJobs();
        double max = scroller.Extent.Height - scroller.Viewport.Height;

        TestApp.Press(_w, Key.End);
        Assert.Equal(max, scroller.Offset.Y, 1.0);
        TestApp.Press(_w, Key.PageUp);
        Assert.InRange(scroller.Offset.Y, 1, max - 1);
        double page = scroller.Offset.Y;
        TestApp.Press(_w, Key.Down);
        Assert.True(scroller.Offset.Y > page);
        TestApp.Press(_w, Key.Up);
        TestApp.Press(_w, Key.Up);
        Assert.True(scroller.Offset.Y < page);
        TestApp.Press(_w, Key.Home);
        Assert.Equal(0, scroller.Offset.Y, 1.0);
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.PreviewPanelOpen);
    }

    [AvaloniaFact]
    public void Everything_in_the_preview_a_screen_reader_or_the_keyboard_needs_has_a_name_and_a_tab_stop()
    {
        Open($"# Title\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n[web](https://docs.example.org/a) ![dot](data:image/png;base64,{OnePixelPng}) ![far](https://remote.example/a.png)\n\n```mermaid\ngraph LR\nA-->B\n```\n", "markdown");
        _w.ShowLivePreview(outline: false);
        var body = Body();

        Assert.False(string.IsNullOrWhiteSpace(NameOf(_w.PreviewPanelControl)));
        Assert.False(string.IsNullOrWhiteSpace(NameOf(_w.PreviewScroller)));
        Assert.True(_w.PreviewScroller.Focusable);
        var named = Tree(body).Where(c => c is Button or Image or Grid or ScrollViewer).ToList();
        Assert.Equal(["Button", "Button", "Grid", "Image", "ScrollViewer"], named.Select(c => c.GetType().Name).Order().ToList());
        Assert.All(named, c => Assert.False(string.IsNullOrWhiteSpace(NameOf(c)), c.GetType().Name));
        Assert.All(Tree(body).OfType<Button>(), b => Assert.True(b.Focusable && b.IsTabStop && b.IsVisible));
        Assert.Equal(Loc.Get(K.A11yPreviewBody), NameOf(_w.PreviewScroller));
    }
}
