using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.History;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// One document of the app: the <see cref="Document"/> (bytes, revision, undo), what the UI knows about it (language mode,
/// how the encoding was guessed, line ending summary) and the views that show it. Views of one document share the text,
/// the revision and the undo history because they all sit on the same <see cref="Document"/>.
/// </summary>
internal sealed class OpenDocument : IDisposable
{
    private const int SampleBytes = 1 << 20;
    private const int EolBlockChars = 1 << 14;

    /// <summary>Where saves and changes from outside are recorded; set once by the app, null leaves documents without history.</summary>
    public static FileHistory? History { get; set; }

    private static int _untitled;
    private HistoryRecorder? _recorder;

    /// <summary>Raised on the UI thread when a revision could not be written (for example the disk is full).</summary>
    public event Action<HistoryRecordResult>? HistoryProblem;
    private readonly DispatcherTimer _eolTimer;
    private Document _doc;
    private EolStats _eol;

    private OpenDocument(Document doc, int number, EncodingGuess? guess, LanguageMode language)
    {
        _doc = doc;
        Number = number;
        Bookmarks = new Bookmarks(doc);
        HexBookmarks = new HexBookmarks(doc);
        Guess = guess;
        Language = language;
        _eolTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _eolTimer.Tick += (_, _) => { _eolTimer.Stop(); RefreshEol(); Changed?.Invoke(); };
        RefreshEol();
        RefreshEditorConfig();
        Hook(doc);
    }

    public Document Doc => _doc;
    /// <summary>The bookmarks of the document; all views show the same ones.</summary>
    public Bookmarks Bookmarks { get; }
    /// <summary>The regions marked in the hex view of the document; they follow the bytes through every edit and are kept in the session.</summary>
    public HexBookmarks HexBookmarks { get; }
    public int Number { get; }
    public LanguageMode Language { get; private set; }
    /// <summary>The user picked the language; the file name no longer decides it, also after a reload or a save under another name.</summary>
    public bool LanguageChosen { get; private set; }
    /// <summary>Raised when <see cref="Language"/> changed, so the views choose another grammar.</summary>
    public event Action? LanguageChanged;
    /// <summary>The rules that give the lines of this document their level while its language is log; the defaults until the user picks others.</summary>
    public LogLevelRules LogLevels { get; private set; } = LogLevelRules.Default;
    /// <summary>The name of the level rules in use, shown in the confirmation when the user picked them.</summary>
    public string LogLevelsName { get; private set; } = "";
    /// <summary>Raised when <see cref="LogLevels"/> changed, so the views color the lines again.</summary>
    public event Action? LogLevelsChanged;
    /// <summary>The document is shown in log mode: lines are colored by level and the log commands apply.</summary>
    public bool IsLog => Language.Id == LanguageModes.LogId;

    public void SetLogLevels(LogLevelRules rules, string name)
    {
        LogLevels = rules;
        LogLevelsName = name;
        LogLevelsChanged?.Invoke();
    }

    /// <summary>How the encoding was chosen when the file was read; null for new documents and after an explicit choice.</summary>
    public EncodingGuess? Guess { get; private set; }
    public Guid? JournalId { get; set; }
    /// <summary>Indentation detected from the content of the file when it was opened; replaces the configured one for this document.</summary>
    public IndentSettings? IndentOverride { get; set; }
    public AutosaveService? Autosave { get; set; }
    /// <summary>How often a document with autosave on is written to its own file.</summary>
    public static TimeSpan AutosaveInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<DocTab> Views { get; } = [];

    /// <summary>Raised on the UI thread after every content or state change of the document.</summary>
    public event Action? Changed;
    /// <summary>Raised when the whole document was swapped (reload), so views must bind again.</summary>
    public event Action? Replaced;

    public AvaloniaEditBackend? TextBackend => _doc.Backend as AvaloniaEditBackend;
    /// <summary>Opened in the paged backend: shown and edited on the paged surface.</summary>
    public bool IsLarge => TextBackend is null && !_switchedForHex;
    public bool ReadOnly => IsResults || LockedReadOnly;
    /// <summary>The lock this document holds on its file, or the reason the file opened read only; null for a document without a file.</summary>
    public Core.Files.FileLockResult? Lock { get; set; }
    /// <summary>Another program or another editor window holds the file, so this tab only reads it.</summary>
    public bool LockedReadOnly => Lock is { IsReadOnly: true };
    /// <summary>Set when this document is the local copy of a remote file; the copy knows the server revision it was made from.</summary>
    public Remote.WorkingCopy? RemoteCopy { get; set; }
    /// <summary>The document behind the "Search Results" tab: it has no text of its own, is never saved and never journaled.</summary>
    public bool IsResults { get; private init; }
    /// <summary>The <c>.editorconfig</c> properties of the file; empty for a document without a path. Refreshed by <see cref="RefreshEditorConfig"/>.</summary>
    public EditorConfigResult EditorConfig { get; private set; } = EditorConfigResult.Empty;
    public string? Path => _doc.FilePath;

    /// <summary>Changes worth asking about: modified, and not just a new document nobody typed into.</summary>
    public bool HasUnsavedWork => !IsResults && _doc.IsModified && !(Path is null && _doc.ByteLength == _doc.BomLength);
    public EolStats Eol => _eol;

    /// <summary>The title of a tab with a view of its own other than the search results (a comparison); read again each time the tab strip is drawn, so it follows the language.</summary>
    public Func<string>? TitleOverride { get; set; }

    public string Title => TitleOverride?.Invoke() ?? (IsResults ? Loc.Get(K.ResultsTab) : Path is null ? Loc.Get(K.TabUntitled, ("number", Number)) : System.IO.Path.GetFileName(Path));

    /// <summary>True when the encoding was a guess the user should confirm (status bar marks it).</summary>
    public bool EncodingUncertain => Guess is { IsUncertain: true };

    /// <summary>Size badge for the tab of a large file, like "10 GiB".</summary>
    public string? SizeBadge => IsHex ? (IsLarge ? "HEX · " + FormatSize(_doc.ByteLength) : "HEX") : IsLarge ? FormatSize(_doc.ByteLength) : null;

    /// <summary>The change from outside that waits for review in this document (P6.1.1); null when there is none.</summary>
    internal NksHub.Textida.App.Services.ReviewState? Review { get; set; }
    /// <summary>How many changes the pending review lists, as of the last time it was built.</summary>
    public int ReviewCount { get; set; }
    public string? ReviewBadge => Review is null ? null : Loc.Get(K.ReviewBadge, ("count", ReviewCount));

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GiB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MiB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.#} KiB",
        _ => $"{bytes} B",
    };

    // ---- table ----

    private CsvDialect? _tableDialect;

    /// <summary>How the table views of this document split the text; the delimiter follows the file extension until the user picks one.</summary>
    public CsvDialect TableDialect
    {
        get => _tableDialect ??= CsvDialect.ForPath(Path);
        set => _tableDialect = value;
    }

    // ---- hex ----

    private static readonly PagedBackendFactory HexBackend = new(startIndexing: false);
    private bool _switchedForHex;

    /// <summary>The hex views show this document: its bytes sit in the byte backend, which takes any byte anywhere.</summary>
    public bool IsHex { get; private set; }

    /// <summary>
    /// Moves the bytes of a text document to the byte backend (nothing is written, revision, undo and the unsaved state stay) and marks it as shown in hex.
    /// The caller has closed the text views first.
    /// </summary>
    public void EnterHex()
    {
        if (IsHex) return;
        if (TextBackend is not null)
        {
            // the paged backend cannot scan UTF-16 or UTF-32 for newlines; the hex view does not need to
            var codec = _doc.Codec;
            _doc.SwitchBackend(HexBackend, codec.Kind is CodecKind.Utf16 or CodecKind.Utf32 ? TextCodec.Cp1252 : null);
            _switchedForHex = true;
        }
        IsHex = true;
    }

    /// <summary>The inverse of <see cref="EnterHex"/>: the bytes go back to the text backend they came from. The caller has closed the hex views first.</summary>
    public void LeaveHex()
    {
        if (!IsHex) return;
        if (_switchedForHex)
        {
            _doc.SwitchBackend(AvaloniaEditBackendFactory.Instance);
            _switchedForHex = false;
        }
        IsHex = false;
    }

    // ---- creation ----

    public static OpenDocument New(TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        var doc = Document.FromBytes(codec.Bom, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = codec });
        return new OpenDocument(doc, ++_untitled, null, LanguageModes.Plain);
    }

    /// <summary>An empty document that stands behind a tab with a view of its own (the search results).</summary>
    public static OpenDocument NewResults() =>
        new(Document.FromBytes(ReadOnlyMemory<byte>.Empty, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 }), 0, null, LanguageModes.Plain) { IsResults = true };

    /// <summary>Reads the <c>.editorconfig</c> files that apply to the path of the document again; call it when the document is activated or saved.</summary>
    public void RefreshEditorConfig()
    {
        EditorConfig = Path is { } p ? EditorConfigResolver.Resolve(System.IO.Path.GetFullPath(p), new DiskEditorConfigFiles()) : EditorConfigResult.Empty;
        UpdateSqlDialect();
    }

    // ---- SQL dialect ----

    private string? _sqlChosen;
    private string? _sqlGlobal;

    /// <summary>The dialect that colors and formats this document when it is SQL: the one chosen for the file, else the one of its folder (<c>sql_dialect</c> in <c>.editorconfig</c>), else the global setting.</summary>
    public string SqlDialect { get; private set; } = SqlDialects.Generic;

    /// <summary>The dialect chosen for this file alone; null when none was.</summary>
    public string? SqlDialectChosen => _sqlChosen;

    /// <summary>Chooses the dialect of this file (null takes it back); the views color the file again when the result changes.</summary>
    public void ChooseSqlDialect(string? id)
    {
        _sqlChosen = id;
        UpdateSqlDialect();
    }

    /// <summary>The global dialect (the <c>sql.dialect</c> setting) changed or is read for the first time.</summary>
    public void SetGlobalSqlDialect(string? id)
    {
        _sqlGlobal = id;
        UpdateSqlDialect();
    }

    private void UpdateSqlDialect()
    {
        var next = SqlDialects.Resolve(_sqlChosen, EditorConfig.Get(SqlDialects.FolderProperty), _sqlGlobal);
        if (next == SqlDialect) return;
        SqlDialect = next;
        if (Language.Id == "sql") LanguageChanged?.Invoke();
    }

    /// <summary>Opens a file; above <paramref name="largeFileBytes"/> (the <c>files.largeFileThreshold</c> setting) it maps the file in the paged backend. Throws <see cref="IOException"/> with a localised message.</summary>
    public static OpenDocument Open(string path, long largeFileBytes)
    {
        var (doc, guess) = OpenDocumentOnly(path, largeFileBytes);
        return new OpenDocument(doc, 0, guess, LanguageModes.ForPath(doc.FilePath));
    }

    /// <summary>The <see cref="Document"/> of a file and how its encoding was guessed; used to open and to reload.</summary>
    public static (Document Doc, EncodingGuess Guess) OpenDocumentOnly(string path, long largeFileBytes)
    {
        path = System.IO.Path.GetFullPath(path);
        try
        {
            FileSwap.RecoverAbandoned(path);
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException(null, path);
            IBackendFactory factory = info.Length > largeFileBytes ? PagedBackendFactory.Instance : AvaloniaEditBackendFactory.Instance;
            var guess = GuessFile(path, info.Length);
            try { return (Document.Open(path, factory, new DocumentOptions { Format = guess.Codec }), guess); }
            catch (NotSupportedException) when (factory is PagedBackendFactory)
            {
                throw new IOException(Loc.Get(K.ErrorOpenLargeEncoding, ("path", path), ("encoding", guess.Codec.Name)));
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            throw new IOException(e is IOException io && io.Message.Contains(path, StringComparison.Ordinal) ? io.Message : Loc.Get(K.ErrorOpenFailed, ("path", path), ("reason", e.Message)), e);
        }
    }

    private static EncodingGuess GuessFile(string path, long length)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var sample = new byte[(int)Math.Min(length, SampleBytes)];
        fs.ReadExactly(sample);
        return EncodingGuesser.Guess(sample, length <= SampleBytes);
    }

    /// <summary>Wraps a document the journal rebuilt after a crash or a clean exit; the journal already tracks it.</summary>
    public static OpenDocument FromRecovered(RecoveredDocument recovered, int? numberHint = null)
    {
        var doc = recovered.Document!;
        int number = 0;
        if (recovered.Path is null)
        {
            number = numberHint is > 0 ? numberHint.Value : _untitled + 1;
            _untitled = Math.Max(_untitled, number);
        }
        return new OpenDocument(doc, number, null, LanguageModes.ForPath(doc.FilePath)) { JournalId = recovered.DocId };
    }

    // ---- state ----

    private void Hook(Document doc)
    {
        doc.Changed += OnDocChanged;
        doc.Saved += OnSaved;
        doc.ExternalChangeDetected += OnExternalChange;
        // a file in the paged backend is not copied into the history: it would cost its whole size on every open and save (no size cap is decided)
        _recorder = History is { } history && !IsLarge ? new HistoryRecorder(history, doc, (_, result) => Dispatcher.UIThread.Post(() => HistoryProblem?.Invoke(result))) : null;
    }

    private void Unhook(Document doc)
    {
        doc.Changed -= OnDocChanged;
        doc.Saved -= OnSaved;
        doc.ExternalChangeDetected -= OnExternalChange;
        _recorder?.Dispose();
        _recorder = null;
        doc.StopWatching();
        _watching = null;
    }

    private string? _watching;

    /// <summary>Raised on the UI thread when the file of the document changed on disk behind the app.</summary>
    public event Action<ExternalChange>? ExternalChangeDetected;

    private void OnExternalChange(object? sender, ExternalChange change) =>
        Dispatcher.UIThread.Post(() => ExternalChangeDetected?.Invoke(change));

    /// <summary>Starts watching the file of a named document (again after the path changed).</summary>
    public void EnsureWatching()
    {
        if (_doc.FilePath is not { } path || string.Equals(_watching, path, StringComparison.Ordinal)) return;
        _doc.StartWatching();
        _watching = path;
    }

    private void OnDocChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (e.Change.TouchesLineBreaks && !_eolTimer.IsEnabled) _eolTimer.Start();
        Changed?.Invoke();
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        RefreshEditorConfig();
        if (!LanguageChosen) SetLanguage(LanguageModes.ForPath(Path));
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Sets the language by the user's choice; it wins over the one the file name gives.</summary>
    public void ChooseLanguage(LanguageMode mode)
    {
        LanguageChosen = true;
        SetLanguage(mode);
    }

    private void SetLanguage(LanguageMode mode)
    {
        if (ReferenceEquals(Language, mode)) return;
        Language = mode;
        LanguageChanged?.Invoke();
    }

    /// <summary>Recomputes the line ending summary now instead of after the typing pause.</summary>
    public void FlushEol()
    {
        _eolTimer.Stop();
        RefreshEol();
        Changed?.Invoke();
    }

    private void RefreshEol()
    {
        long t = Core.TypingTrace.Begin();
        RefreshEolCore();
        Core.TypingTrace.End("od.refreshEol", t);
    }

    private void RefreshEolCore()
    {
        if (TextBackend is { } text)
        {
            // block by block: the whole text as one string would double the memory of a large document at every pause in typing
            var scanner = new Core.Encodings.EolScanner();
            var d = text.TextDocument;
            for (int at = 0; at < d.TextLength; at += EolBlockChars) scanner.Add(d.GetText(at, Math.Min(EolBlockChars, d.TextLength - at)));
            _eol = scanner.Finish();
        }
        else
        {
            // shortcut: the paged surface reports the line endings of the first block only
            var head = new byte[(int)Math.Min(_doc.ByteLength, 64 * 1024)];
            int n = _doc.Read(0, head);
            var complete = _doc.Codec.CompleteLength(head.AsSpan(0, n));
            _eol = Core.Encodings.Eol.Analyze(_doc.Codec.GetString(head.AsSpan(0, complete)));
        }
    }

    /// <summary>The line ending the text the user types gets: the configured one, else the one the document already uses.</summary>
    public string NewLine(EolStyle? configured) => Core.Encodings.Eol.ForNewLine(_eol, configured, OperatingSystem.IsWindows() ? EolStyle.CrLf : EolStyle.Lf);

    public void SetEncodingChosen() => Guess = null;

    /// <summary>The file was renamed or moved on disk: the document, its tabs and its journal follow it, with text, undo history and unsaved changes intact.</summary>
    public void MoveTo(string newPath)
    {
        bool derived = Path is { } old && Language.Id == LanguageModes.ForPath(old).Id;
        _doc.MoveTo(newPath);
        if (derived) Language = LanguageModes.ForPath(newPath);
        EnsureWatching();
        Changed?.Invoke();
    }

    /// <summary>Replaces the document by a freshly opened one (reload). Views bind again through <see cref="Replaced"/>.</summary>
    public void Replace(Document next, EncodingGuess? guess = null)
    {
        var old = _doc;
        Unhook(old);
        _doc = next;
        Bookmarks.Rebind(next);
        HexBookmarks.Rebind(next);
        Hook(next);
        Guess = guess;
        RefreshEol();
        Replaced?.Invoke();
        old.Dispose();
        EnsureWatching();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _eolTimer.Stop();
        Autosave?.Dispose();
        Lock?.Dispose();
        Unhook(_doc);
        HexBookmarks.Dispose();
        _doc.Dispose();
    }
}
