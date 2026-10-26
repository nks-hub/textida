using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;

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

    private static int _untitled;
    private readonly DispatcherTimer _eolTimer;
    private Document _doc;
    private EolStats _eol;

    private OpenDocument(Document doc, int number, EncodingGuess? guess, LanguageMode language)
    {
        _doc = doc;
        Number = number;
        Guess = guess;
        Language = language;
        _eolTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _eolTimer.Tick += (_, _) => { _eolTimer.Stop(); RefreshEol(); Changed?.Invoke(); };
        RefreshEol();
        Hook(doc);
    }

    public Document Doc => _doc;
    public int Number { get; }
    public LanguageMode Language { get; set; }
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
    public bool IsLarge => TextBackend is null;
    public string? Path => _doc.FilePath;

    /// <summary>Changes worth asking about: modified, and not just a new document nobody typed into.</summary>
    public bool HasUnsavedWork => _doc.IsModified && !(Path is null && _doc.ByteLength == _doc.BomLength);
    public EolStats Eol => _eol;

    public string Title => Path is null ? Loc.Get(K.TabUntitled, ("number", Number)) : System.IO.Path.GetFileName(Path);

    /// <summary>True when the encoding was a guess the user should confirm (status bar marks it).</summary>
    public bool EncodingUncertain => Guess is { IsUncertain: true };

    /// <summary>Size badge for the tab of a large file, like "10 GiB".</summary>
    public string? SizeBadge => IsLarge ? FormatSize(_doc.ByteLength) : null;

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GiB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MiB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.#} KiB",
        _ => $"{bytes} B",
    };

    // ---- creation ----

    public static OpenDocument New(TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        var doc = Document.FromBytes(codec.Bom, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = codec });
        return new OpenDocument(doc, ++_untitled, null, LanguageModes.Plain);
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
    }

    private void Unhook(Document doc)
    {
        doc.Changed -= OnDocChanged;
        doc.Saved -= OnSaved;
        doc.ExternalChangeDetected -= OnExternalChange;
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
        if (!_eolTimer.IsEnabled) _eolTimer.Start();
        Changed?.Invoke();
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        if (Language.Id == LanguageModes.PlainId) Language = LanguageModes.ForPath(Path);
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Recomputes the line ending summary now instead of after the typing pause.</summary>
    public void FlushEol()
    {
        _eolTimer.Stop();
        RefreshEol();
        Changed?.Invoke();
    }

    private void RefreshEol()
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

    /// <summary>Replaces the document by a freshly opened one (reload). Views bind again through <see cref="Replaced"/>.</summary>
    public void Replace(Document next, EncodingGuess? guess = null)
    {
        var old = _doc;
        Unhook(old);
        _doc = next;
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
        Unhook(_doc);
        _doc.Dispose();
    }
}
