using System.Text;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.App.Shell;

/// <summary>The files of the folder in the file panel, read once in the background for Go to Anything.</summary>
internal sealed class WorkspaceIndex
{
    private const int MaxFiles = 500_000;
    private readonly CancellationTokenSource _cts = new();

    public WorkspaceIndex(string root, Action completed)
    {
        Root = root;
        _ = Task.Run(() => Build(completed));
    }

    public string Root { get; }

    /// <summary>Paths relative to <see cref="Root"/>; null while the folder is still being read.</summary>
    public PathIndex? Index { get; private set; }

    public void Cancel() => _cts.Cancel();

    private void Build(Action completed)
    {
        try
        {
            var options = new FileWalkOptions { MaxFileSize = long.MaxValue };
            var paths = new List<string>();
            foreach (var file in FileWalker.Walk(Root, options, ct: _cts.Token))
            {
                paths.Add(Path.GetRelativePath(Root, file.Path));
                if (paths.Count >= MaxFiles) break;
            }
            Index = new PathIndex(paths);
            Dispatcher.UIThread.Post(completed);
        }
        catch (OperationCanceledException)
        {
            // another folder was opened; nothing waits for this index
        }
    }
}

internal sealed partial class MainWindow
{
    private WorkspaceIndex? _workspace;

    private string? FileRootFolder => _fileTree?.Root;

    private void EnsureWorkspaceIndex()
    {
        if (FileRootFolder is not { } root || (_workspace is not null && string.Equals(_workspace.Root, root, PathComparison))) return;
        _workspace?.Cancel();
        _workspace = new WorkspaceIndex(root, () => { if (IsPaletteOpen && _paletteMode == PaletteMode.Files) RefreshPalette(); });
    }

    /// <summary>The next Go to Anything reads the folder again; called when files in it changed.</summary>
    private void InvalidateWorkspaceIndex()
    {
        _workspace?.Cancel();
        _workspace = null;
    }

    private string Display(string path) =>
        FileRootFolder is { } root && PathMove.IsInside(path, root) && !string.Equals(path, root, PathComparison) ? Path.GetRelativePath(root, path).Replace('\\', '/') : path;

    private readonly record struct Candidate(string Display, string? Path, DocTab? Tab, int Kind);

    private List<QuickRow> FileRows(string query, out string highlight, out string? emptyText)
    {
        if (query.StartsWith('@')) return KeyRows(query[1..].Trim(), out highlight, out emptyText);
        emptyText = Loc.Get(FileRootFolder is null ? K.PaletteNoFolder : K.PaletteFilesNoResults);
        string name = query.Trim();
        int? line = null;
        int colon = name.LastIndexOf(':');
        if (colon >= 0 && (colon == name.Length - 1 || int.TryParse(name.AsSpan(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            if (colon < name.Length - 1) line = int.Parse(name.AsSpan(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            name = name[..colon].Trim();
        }
        highlight = name;

        if (name.Length == 0 && line is { } only)
        {
            string text = Loc.Get(K.PaletteGoToLine, ("line", only));
            return [new QuickRow(text, Loc.Get(K.PaletteCategoryLine), null, null, Active is not null && only >= 1, null, text, () => { if (Active is { } tab) { RecordJump(); MoveCaret(tab, only - 1, 0); } })];
        }

        var open = Tabs.GroupBy(t => t.Doc).Select(g => g.First())
            .Select(t => new Candidate(t.Path is { } p ? Display(p) : t.Title, t.Path, t, 0)).ToList();
        var openPaths = open.Where(c => c.Path is not null).Select(c => c.Path!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recent = _app.Recent.Files.Where(p => !openPaths.Contains(p)).Select(p => new Candidate(Display(p), p, null, 1)).ToList();

        var chosen = new List<(Candidate Item, int Score)>();
        if (name.Length == 0)
        {
            chosen.AddRange(open.Select((c, i) => (c, 1000 - i)));
            chosen.AddRange(recent.Select((c, i) => (c, 500 - i)));
        }
        else
        {
            var local = open.Concat(recent).ToList();
            foreach (var m in new PathIndex([.. local.Select(c => c.Display)]).Query(name, 60))
                chosen.Add((local[m.Index], m.Score + (local[m.Index].Kind == 0 ? 40 : 20)));
            var seen = local.Where(c => c.Path is not null).Select(c => c.Path!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_workspace is { Index: { } index } workspace)
                foreach (var m in index.Query(name, 80))
                {
                    string full = Path.Combine(workspace.Root, index.Paths[m.Index]);
                    if (!seen.Contains(full)) chosen.Add((new Candidate(index.Paths[m.Index].Replace('\\', '/'), full, null, 2), m.Score));
                }
        }
        var rows = chosen.OrderByDescending(c => c.Score).Take(60).Select(c => FileRow(c.Item, line)).ToList();
        if (_workspace is { Index: null })
        {
            string reading = Loc.Get(K.PaletteIndexing);
            rows.Add(new QuickRow(reading, null, null, null, false, null, reading, () => { }));
        }
        return rows;
    }

    private (Guid Document, long Revision)? _keysRead;

    /// <summary>
    /// Go to Anything with <c>@</c>: the keys of the active YAML or JSON file by their path, or the symbols of a code or markup file by name. Enter jumps to the bytes of the
    /// symbol, exactly as a click in the outline does. They are read through the plugin the first time and again after an edit; the rows appear when it has published them.
    /// </summary>
    private List<QuickRow> KeyRows(string text, out string highlight, out string? emptyText)
    {
        highlight = text;
        if (Active is not { Surface: not null } tab || PluginServices.SymbolPlugin(tab.Doc.Language.Id) is not { } plugin)
        {
            emptyText = Loc.Get(K.PaletteKeysNone);
            return [];
        }
        bool keys = plugin == PluginServices.WebDataPluginId;
        emptyText = Loc.Get(keys ? K.PaletteKeysNone : K.PaletteSymbolsNone);
        var symbols = _app.Plugins.TabSymbols(tab, out bool current);
        var version = (tab.Doc.Doc.Id, tab.Doc.Doc.Revision);
        if ((symbols.Count == 0 || !current) && _keysRead != version)
        {
            _keysRead = version;
            _ = ReadKeysAsync(tab);
        }
        if (symbols.Count == 0)
        {
            string reading = Loc.Get(keys ? K.PaletteKeysReading : K.PaletteSymbolsReading);
            return [new QuickRow(reading, null, null, null, false, null, reading, () => { })];
        }
        string category = Loc.Get(keys ? K.PaletteCategoryKey : K.PaletteCategorySymbol);
        return [.. symbols.Where(s => Matches(text, s.Name)).OrderBy(s => s.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(60)
            .Select(s => new QuickRow(s.Name, category, null, null, true, null, category + ": " + s.Name, () => { RecordJump(); _app.Plugins.RevealTabSymbol(tab, s.Id); }))];
    }

    private async Task ReadKeysAsync(DocTab tab)
    {
        await _app.Plugins.IndexSymbolsAsync(this, tab, reveal: false);
        if (IsPaletteOpen && _paletteMode == PaletteMode.Files && (_paletteInput.Text ?? "").StartsWith('@')) RefreshPalette();
    }

    private QuickRow FileRow(Candidate c, int? line)
    {
        string display = c.Display;
        bool source = c.Tab is null && c.Path is not null && UriMask.IsUri(c.Path); // a file of a plugin source, listed masked
        string file = source ? UriMask.DisplayName(c.Path!) : c.Tab is { Path: null } ? display : Path.GetFileName(display);
        string? directory = source ? c.Path : c.Tab is { Path: null } || Path.GetDirectoryName(display) is not { Length: > 0 } d ? null : d.Replace('\\', '/');
        bool missing = !source && c.Path is not null && c.Tab is null && !File.Exists(c.Path);
        string category = c.Kind switch { 0 => Loc.Get(K.PaletteCategoryOpen), 1 => Loc.Get(K.PaletteCategoryRecent), _ => "" };
        string reason = Loc.Get(K.PaletteFileMissing);
        string title = line is { } l ? $"{file}:{l}" : file;
        string a11y = (category.Length > 0 ? category + ": " : "") + title + (directory is null ? "" : ", " + directory) + (missing ? ", " + reason : "");
        Action run = () =>
        {
            if (missing) { SetStatusMessage(Loc.Get(K.PaletteFileMissingStatus, ("path", c.Path!))); return; }
            if (c.Tab is { } tab && tab.Group is not null)
            {
                RecordJump();
                SelectTab(tab);
                if (line is { } n) MoveCaret(tab, n - 1, 0);
                FocusView(tab);
            }
            else if (c.Path is { } path) { if (source) _ = OpenSourceAsync(path); else JumpToFile(path, line); }
        };
        Func<string>? preview = c.Tab is { Surface: { } surface } ? () => FirstLines(HideSecrets(c.Tab.Doc.Doc.SecretsPath, surface.Document.GetText(0, Math.Min(surface.Document.TextLength, 4096)))) : c.Path is { } p && !missing && !source ? () => ReadPreview(p) : null;
        return new QuickRow(title, category.Length > 0 ? category : null, directory, null, !missing, missing ? reason : null, a11y, run, preview);
    }

    // ---- preview of the highlighted file; reading it changes nothing, the recent list only grows when a file is opened ----

    private void UpdatePalettePreview()
    {
        bool show = _paletteMode is PaletteMode.Files or PaletteMode.Choice && _paletteList.SelectedIndex is >= 0 and var i && i < _quickRows.Count && _quickRows[i].Preview is not null;
        _palettePreviewHost.IsVisible = show;
        _palettePreview.Text = show ? _quickRows[_paletteList.SelectedIndex].Preview!() : "";
    }

    /// <summary>A preview or a list shows the values of a secrets file as the placeholder, whatever the tab does; the palette is no place to show them.</summary>
    private static string HideSecrets(string? path, string text) => SecretValues.IsSecretFile(path) ? SecretValues.Mask(text, SecretValues.Find(text)) : text;

    private static string FirstLines(string text)
    {
        var lines = text.Split('\n');
        return string.Join("\n", lines.Take(14)).TrimEnd('\r', '\n').Replace("\r", "");
    }

    internal static string ReadPreview(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[4096];
            int n = fs.Read(buffer);
            var span = buffer.AsSpan(0, n);
            if (span.Length >= 2 && ((span[0] == 0xFF && span[1] == 0xFE) || (span[0] == 0xFE && span[1] == 0xFF)))
                return FirstLines(HideSecrets(path, (span[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(span[2..])));
            if (span.Contains((byte)0)) return Loc.Get(K.PalettePreviewBinary);
            return FirstLines(HideSecrets(path, Encoding.UTF8.GetString(span)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Loc.Get(K.PalettePreviewError, ("reason", e.Message));
        }
    }

    // ---- recent edit locations ----

    private List<QuickRow> EditRows(string query)
    {
        var rows = new List<QuickRow>();
        foreach (var place in _edits.Places)
        {
            place.Refresh();
            if (SnippetOf(place) is { } fresh) place.Snippet = fresh;
            if (_paletteOnlyModified.IsChecked == true && place.Document is not { IsModified: true }) continue;
            string file = place.Path is { } p ? Path.GetFileName(p) : TitleOf(place.Document);
            string where = $"{file}:{place.Line + 1}";
            string snippet = string.IsNullOrWhiteSpace(place.Snippet) ? "…" : place.Snippet.Trim();
            if (SecretValues.IsSecretFile(place.Path)) snippet = SecretValues.MaskLine(snippet);
            if (!Matches(query, snippet + " " + where)) continue;
            var target = place;
            rows.Add(new QuickRow(snippet, where, null, null, true, null, $"{where}: {snippet}", () => { RecordJump(); GoToPlace(target); }));
        }
        return rows;
    }

    private string TitleOf(Document? doc) => _docs.FirstOrDefault(d => ReferenceEquals(d.Doc, doc))?.Title ?? "";
}
