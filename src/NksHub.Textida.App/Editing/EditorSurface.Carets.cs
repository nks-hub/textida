using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using CoreDoc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// More than one caret on the AvaloniaEdit surface. The area's own caret and selection stay the primary one; the others are kept as anchors of the
/// document (<see cref="AnchoredSelections"/>), so they follow edits from any source, and drawn by two background renderers. While there are
/// several, keys, typed text and the clipboard act on all of them in one transaction (<see cref="MultiEdit"/>): one revision, one undo step, byte-exact.
/// </summary>
internal sealed partial class EditorSurface
{
    private int _blinkMs = EditorOptions.DefaultCaretBlinkMs;

    private AnchoredSelections? _others;
    private AnchoredSelections? _primaryKept;
    private List<Sel> _otherChars = [];
    private long _stamp, _otherStamp = -1;
    private CoreDoc? _hooked;
    private bool _wholeWord;
    private int[]? _sticky;
    private int? _addColumn;
    private DispatcherTimer? _blink;
    private bool _caretOn = true;
    private IBrush? _caretBrush;
    private Carets? _preAlt;
    private Point _altPress;
    private string _runKind = "";
    private long _runRevision = -1;
    private Carets? _runCarets;
    private int _runId;
    private SyntaxProbe? _probe;

    /// <summary>Raised when the number or the places of the carets changed through this class.</summary>
    public event Action? CaretsChanged;

    /// <summary>True while there is more than one caret.</summary>
    public bool IsMulti => _others is not null && OtherSels().Count > 0;

    public int CaretCount => IsMulti ? OtherSels().Count + 1 : 1;

    /// <summary>Characters selected over all carets.</summary>
    public int SelectedLength => IsMulti ? OtherSels().Sum(s => s.Length) + PrimarySel().Length : TextArea.Selection.Length;

    private void InitCarets()
    {
        TextArea.TextView.BackgroundRenderers.Add(new SelectionLayer(this));
        TextArea.TextView.BackgroundRenderers.Add(new CaretLayer(this));
        TextArea.AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel);
        TextArea.AddHandler(PointerReleasedEvent, OnPointerReleasedAfter, RoutingStrategies.Bubble, handledEventsToo: true);
        // the caret drawn here has no focus of its own to follow, and it starts to blink from "on" after every move
        TextArea.Caret.PositionChanged += (_, _) => { if (DrawsOwnCarets) RestartBlink(); };
        TextArea.GotFocus += (_, _) => { if (DrawsOwnCarets) RestartBlink(); };
        TextArea.LostFocus += (_, _) => { if (DrawsOwnCarets) InvalidateCarets(); };
        HookCoreDocument();
    }

    private void HookCoreDocument()
    {
        if (_hooked == Doc.Doc) return;
        if (_hooked is not null) _hooked.Changed -= OnCoreChanged;
        _hooked = Doc.Doc;
        _hooked.Changed += OnCoreChanged;
    }

    private void OnCoreChanged(object? sender, NksHub.Textida.Core.Documents.DocumentChangedEventArgs e)
    {
        long t = Core.TypingTrace.Begin();
        try { OnCoreChangedCore(e); }
        finally { Core.TypingTrace.End("carets.onCoreChanged", t); }
    }

    private void OnCoreChangedCore(NksHub.Textida.Core.Documents.DocumentChangedEventArgs e)
    {
        _stamp++;
        EndComposition();
        if (_others is null) return;
        // a reread replaces the whole text of the view and the area loses its selection; the primary one is put back from its anchors
        if (e.Change.Kind == ChangeKind.View && _primaryKept?.Current() is { } kept && Doc.TextBackend is { } backend
            && backend.TryToCharOffset(kept.PrimarySelection.Anchor, out int a) && backend.TryToCharOffset(kept.PrimarySelection.Caret, out int c))
            SetPrimaryOnly(new Sel(a, c));
        InvalidateCarets();
    }

    /// <summary>Releases what the surface holds on the document; called when its tab closes.</summary>
    public void Detach()
    {
        EndSnippet();
        Doc.LanguageChanged -= ApplyLogLevels;
        Doc.LogLevelsChanged -= ApplyLogLevels;
        Doc.LogHighlightChanged -= ApplyLogLevels;
        foreach (var margin in TextArea.LeftMargins.OfType<BookmarkMargin>().ToList())
        {
            margin.Release();
            TextArea.LeftMargins.Remove(margin);
        }
        _gitMargin.Release();
        TextArea.LeftMargins.Remove(_gitMargin);
        if (_hooked is not null) _hooked.Changed -= OnCoreChanged;
        _hooked = null;
        Doc.Changed -= OnSecretStateMaybeChanged;
        Doc.LanguageChanged -= OnColorLanguageChanged;
        _blink?.Stop();
        _glide?.Stop();
        _probe?.Dispose();
        _probe = null;
        ReleaseBrackets();
    }

    private void RebindCarets()
    {
        EndComposition();
        DropOthers();
        _probe?.Dispose();
        _probe = null;
        ReleaseBrackets();
        HookCoreDocument();
    }

    // ---- the selections as a list ----

    private Sel PrimarySel()
    {
        var selection = TextArea.Selection;
        int caret = CaretOffset;
        if (selection.IsEmpty) return Sel.At(caret);
        var seg = selection.SurroundingSegment;
        return new Sel(caret == seg.EndOffset ? seg.Offset : seg.EndOffset, caret);
    }

    private List<Sel> OtherSels()
    {
        if (_others is null) return [];
        if (_otherStamp == _stamp) return _otherChars;
        var list = new List<Sel>();
        if (_others.Current() is { } set && Doc.TextBackend is { } backend)
            foreach (var t in set.Items)
                if (backend.TryToCharOffset(t.Anchor, out int a) && backend.TryToCharOffset(t.Caret, out int c)) list.Add(new(a, c));
        _otherChars = list;
        _otherStamp = _stamp;
        if (list.Count == 0) { _others = null; AfterCaretsChanged(); }
        return list;
    }

    /// <summary>All carets in document order with the primary one marked.</summary>
    public Carets GetCarets()
    {
        var primary = PrimarySel();
        var others = OtherSels();
        if (others.Count == 0) return Carets.One(primary);
        var all = new List<Sel>(others.Count + 1);
        all.AddRange(others);
        // the others are in order: the primary one goes in at its place, or is merged with a neighbour it touches
        int at = 0, hi = others.Count;
        while (at < hi)
        {
            int mid = (at + hi) / 2;
            if (others[mid].Start < primary.Start) at = mid + 1; else hi = mid;
        }
        if ((at == 0 || others[at - 1].End < primary.Start) && (at == others.Count || primary.End < others[at].Start))
        {
            all.Insert(at, primary);
            return new Carets(all, at);
        }
        all.Add(primary);
        return Carets.Create(all, all.Count - 1);
    }

    private void SetPrimaryOnly(Sel s)
    {
        using (TextArea.AllowCaretOutsideSelection())
        {
            if (s.IsEmpty) TextArea.ClearSelection();
            else TextArea.Selection = Selection.Create(TextArea, s.Start, s.End);
            CaretOffset = s.Caret;
        }
    }

    /// <summary>Makes <paramref name="c"/> the carets of the surface; <paramref name="bytes"/> are their positions in bytes when the caller knows them.</summary>
    public void SetCarets(Carets c, IReadOnlyList<TextSelection>? bytes = null, bool reveal = true)
    {
        SetPrimaryOnly(c.PrimarySel);
        _sticky = null;
        _addColumn = null;
        if (c.Count == 1) DropOthers();
        else if (Doc.TextBackend is { } backend)
        {
            var all = bytes ?? BytesOf(backend, [.. c.Items]);
            var others = new List<Sel>(c.Count - 1);
            var otherBytes = new List<TextSelection>(c.Count - 1);
            for (int i = 0; i < c.Count; i++)
            {
                if (i == c.Primary) continue;
                others.Add(c.Items[i]);
                otherBytes.Add(all[i]);
            }
            var otherSet = SelectionSet.Create(otherBytes, 0);
            var primarySet = SelectionSet.Single(all[c.Primary]);
            // after typing at every caret the anchors have followed the text to where the new carets are
            if (_others is null || _primaryKept is null || !_others.Matches(otherSet) || !_primaryKept.Matches(primarySet))
            {
                ReleaseAnchors();
                _others = new AnchoredSelections(Doc.Doc, otherSet);
                _primaryKept = new AnchoredSelections(Doc.Doc, primarySet);
            }
            _otherChars = others;
            _otherStamp = _stamp;
            AfterCaretsChanged();
        }
        if (reveal) TextArea.Caret.BringCaretToView();
    }

    /// <summary>The carets as byte ranges of the document, in document order with the primary one marked; what the plugin API reports as selections.</summary>
    public SelectionSet? ByteSelections()
    {
        if (Doc.TextBackend is not { } backend) return null;
        var carets = GetCarets();
        return SelectionSet.Create(BytesOf(backend, [.. carets.Items]), carets.Primary);
    }

    private static List<TextSelection> BytesOf(AvaloniaEditBackend backend, List<Sel> sels)
    {
        var offsets = sels.SelectMany(s => new[] { s.Anchor, s.Caret }).Distinct().Order().ToArray();
        var bytes = backend.ToByteOffsets(offsets);
        var at = new Dictionary<int, long>(offsets.Length);
        for (int i = 0; i < offsets.Length; i++) at[offsets[i]] = bytes[i];
        return [.. sels.Select(s => new TextSelection(at[s.Anchor], at[s.Caret]))];
    }

    private void ReleaseAnchors()
    {
        _others?.Release();
        _primaryKept?.Release();
    }

    private void DropOthers()
    {
        if (_others is null) return;
        ReleaseAnchors();
        _others = null;
        _primaryKept = null;
        _otherChars = [];
        AfterCaretsChanged();
    }

    /// <summary>Back to one caret, the primary one.</summary>
    public void ClearCarets() => DropOthers();

    private void AfterCaretsChanged()
    {
        RefreshCaretDrawing();
        CaretsChanged?.Invoke();
    }

    /// <summary>
    /// The surface draws the carets itself when there are several, and also for one caret when the blink time of the setting is not the one of AvaloniaEdit, whose caret
    /// takes no other (see <see cref="CaretLayer"/>). Not in block mode, where the caret may stand beyond the end of a line.
    /// </summary>
    internal bool DrawsOwnCarets => _others is not null || (_blinkMs != EditorOptions.DefaultCaretBlinkMs && !BlockMode);

    /// <summary>The blink time of the setting (0: the caret stays on); changing it switches between the caret of AvaloniaEdit and the one drawn here.</summary>
    private void ApplyCaretBlink(int milliseconds)
    {
        int blink = Math.Clamp(milliseconds, 0, 2000);
        if (blink == _blinkMs) return;
        _blinkMs = blink;
        RefreshCaretDrawing();
    }

    /// <summary>Makes the caret visible and starts its blinking again from "on".</summary>
    private void RestartBlink()
    {
        _caretOn = true;
        if (_blink is { IsEnabled: true } running)
        {
            running.Stop();
            running.Start();
        }
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    /// <summary>Hides the caret of AvaloniaEdit while this class draws, and runs the blink timer when a caret is drawn here and is to blink.</summary>
    private void RefreshCaretDrawing()
    {
        _caretOn = true;
        UpdateNativeCaret();
        if (DrawsOwnCarets && _blinkMs > 0)
        {
            _blink ??= CreateBlink();
            _blink.Stop();
            _blink.Interval = TimeSpan.FromMilliseconds(_blinkMs);
            _blink.Start();
        }
        else _blink?.Stop();
        InvalidateCarets();
    }

    /// <summary>The time between two changes of the blinking caret drawn here, or null when no timer exists.</summary>
    internal TimeSpan? CaretBlinkInterval => _blink?.Interval;

    /// <summary>True while the blink timer of the caret drawn here runs.</summary>
    internal bool CaretBlinking => _blink?.IsEnabled == true;

    private DispatcherTimer CreateBlink()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            if (!DrawsOwnCarets) return;
            _caretOn = !_caretOn;
            TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        };
        return timer;
    }

    private void InvalidateCarets()
    {
        TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    /// <summary>The native caret is hidden while the carets are drawn by the renderer, so they all look and blink alike.</summary>
    private void UpdateNativeCaret() => TextArea.CaretBrush = DrawsOwnCarets ? Brushes.Transparent : _caretBrush;

    // ---- commands ----

    private int TabSize => Math.Max(1, Settings.TabSize);

    public void AddNextOccurrence() => ApplyAdded(c => MultiCaret.AddNextOccurrence(Document, c, ref _wholeWord));

    public void SkipOccurrence() => ApplyAdded(c => MultiCaret.SkipOccurrence(Document, c, ref _wholeWord));

    public void SelectAllOccurrences() => ApplyAdded(c => MultiCaret.AllOccurrences(Document, c, ref _wholeWord));

    private void ApplyAdded(Func<Carets, Carets?> step)
    {
        if (BlockMode) return;
        if (step(GetCarets()) is { } next) SetCarets(next);
    }

    /// <summary>Adds a caret on the line above (-1) or below (+1) at the column the carets have; repeated calls keep that column across short lines.</summary>
    public void AddCaretVertical(int direction)
    {
        if (BlockMode) return;
        var c = GetCarets();
        var edge = direction < 0 ? c.Items[0] : c.Items[^1];
        int column = _addColumn ?? MultiCaret.VisualColumn(Document, edge.Caret, TabSize);
        if (MultiCaret.AddVertical(Document, c, direction, TabSize, column) is not { } next) return;
        SetCarets(next);
        _addColumn = column;
    }

    private bool MoveCarets(CaretMove move, bool extend)
    {
        SetCarets(MultiCaret.Move(Document, GetCarets(), move, extend));
        return true;
    }

    private bool MoveVertically(int direction, bool extend)
    {
        var moved = MultiCaret.MoveVertical(Document, GetCarets(), direction, extend, TabSize, _sticky, out var columns);
        SetCarets(moved);
        _sticky = columns;
        return true;
    }

    // ---- editing ----

    private string? RunKey(string kind, Carets before)
    {
        bool continues = _runKind == kind && _runRevision == Doc.Doc.Revision && _runCarets is { } last && last.Primary == before.Primary && last.Items.SequenceEqual(before.Items);
        if (!continues) _runId++;
        return $"carets-{kind}#{_runId}";
    }

    private bool Run(List<Planned> plan, Carets before, string? kind)
    {
        if (Doc.TextBackend is not { } backend || IsReadOnly) return false;
        long t = Core.TypingTrace.Begin();
        var result = MultiEdit.Apply(Doc.Doc, backend, before, plan, kind is null ? null : RunKey(kind, before), TextArea.TextView);
        Core.TypingTrace.End("carets.multiEditApply", t);
        if (!result.Edit.Applied) return false;
        t = Core.TypingTrace.Begin();
        SetCarets(result.Carets, result.Bytes);
        Core.TypingTrace.End("carets.setCarets", t);
        (_runKind, _runRevision, _runCarets) = (kind ?? "", Doc.Doc.Revision, result.Carets);
        return true;
    }

    private SmartKeys? Smart()
    {
        if (!SmartKeys.HasRulesFor(Doc.Language.Id)) return null;
        if (_probe is null || _probe.Language != Doc.Language.Id)
        {
            _probe?.Dispose();
            _probe = SyntaxProbe.TryCreate(Doc.Doc, Doc.Language.Id);
        }
        return _probe is null ? null : new SmartKeys(Document, _probe, Doc.Language.Id, Settings.Smart, Settings.Indent.Unit);
    }

    private List<ITypingRule> TypingRules(SmartKeys? smart)
    {
        var rules = new List<ITypingRule>(2);
        if (smart is not null) rules.Add(smart);
        if (Settings.AutoClosePairs) rules.Add(Settings.PspadPairs ? new PspadPairsRule(Doc.Language.Id is "html" or "xml" or "php") : PairedCharactersRule.Instance);
        return rules;
    }

    /// <summary>
    /// Typed text (a character or a committed composition). With several carets it goes to all of them as one transaction; with one caret only when a
    /// smart rule has something to add, otherwise the area inserts it as usual (and keeps its own typing runs). False: not handled here.
    /// </summary>
    private bool TypeAtCarets(string text)
    {
        long t = Core.TypingTrace.Begin();
        try { return TypeAtCaretsCore(text); }
        finally { Core.TypingTrace.End("carets.typeAtCarets", t); }
    }

    private bool TypeAtCaretsCore(string text)
    {
        if (IsReadOnly || IsBlockSelection || text.Any(char.IsControl)) return false;
        var smart = Smart();
        var rules = TypingRules(smart);
        bool multi = IsMulti;
        if (!multi && rules.Count == 0) return false;
        var carets = GetCarets();
        MultiEdit.PartnerEdit? partner = smart is null ? null : e => smart.TryPlanRename(e, out var p) ? p : null;
        long tPlan = Core.TypingTrace.Begin();
        var plan = MultiEdit.PlanTyped(Document, carets, text, rules, partner);
        Core.TypingTrace.End("carets.planTyped", tPlan);
        if (!multi && plan.Count == 1 && plan[0].Edit == new ViewEdit(carets.PrimarySel.Start, carets.PrimarySel.Length, text)) return false; // nothing special: the area does it
        if (!multi && plan.Count == 1)
        {
            // a rule changed one place only: edit the text view directly, so the typing run of the area stays one undo step
            var edit = plan[0].Edit;
            Document.Replace(edit.Start, edit.Length, edit.Text);
            SetCarets(Carets.One(plan[0].Local!.Value));
            return true;
        }
        return Run(plan, carets, text.Length <= 16 ? "typing" : null);
    }

    private bool DeleteAtCarets(bool backwards, bool word)
    {
        if (IsReadOnly || IsBlockSelection) return false;
        var carets = GetCarets();
        int unindent = Settings.BackspaceUnindent ? TabSize : 0;
        bool pairs = Settings.AutoClosePairs && !Settings.PspadPairs;
        if (!IsMulti)
        {
            // one caret: only what the area does not do itself; its own runs of Backspace stay one undo step
            if (!backwards || word || unindent == 0 || !IndentBackspace.TryPlan(Document, carets.PrimarySel, unindent, out var edit, out var local)) return false;
            Document.Replace(edit.Start, edit.Length, edit.Text);
            SetCarets(Carets.One(local));
            return true;
        }
        return Run(MultiEdit.PlanDelete(Document, carets, backwards, word, pairs, unindent), carets, "deleting");
    }

    /// <summary>Enter at every caret: smart rules, comment continuation, the indentation of the line.</summary>
    public bool InsertLineBreaks(string newLine, CommentStyle? comments)
    {
        if (IsReadOnly) return false;
        var smart = Smart();
        var carets = GetCarets();
        MultiEdit.SmartLineBreak? rule = smart is null ? null : (Sel s, out ViewEdit edit, out Sel result) => smart.TryPlanLineBreak(s, newLine, out edit, out result);
        return Run(MultiEdit.PlanLineBreaks(Document, carets, newLine, comments, rule), carets, null);
    }

    /// <summary>Paste with several carets: one clipboard line per caret when the counts agree, otherwise the whole text at every caret. Text pasted inside a string is escaped.</summary>
    public bool PasteAtCarets(string text)
    {
        if (IsReadOnly) return false;
        var carets = GetCarets();
        var pieces = MultiCaret.SplitForCarets(text, carets.Count);
        var smart = Smart();
        if (smart is not null)
        {
            pieces = [.. Enumerable.Range(0, carets.Count).Select(i => smart.EscapeForPaste(carets.Items[i].Start, pieces?[i] ?? text))];
        }
        return Run(MultiEdit.PlanText(carets, text, pieces), carets, null);
    }

    /// <summary>The text for a paste at the primary caret: escaped when the caret is inside a string.</summary>
    public string EscapeForPaste(string text) => Smart() is { } smart ? smart.EscapeForPaste(PrimarySel().Start, text) : text;

    /// <summary>The texts of the selections, one per line, for the clipboard; null when nothing is selected.</summary>
    public string? SelectedTexts(string newLine) => MultiCaret.CopyText(Document, GetCarets(), newLine);

    /// <summary>Removes the selected text of every selection (cut).</summary>
    public bool DeleteSelected()
    {
        var carets = GetCarets();
        var plan = MultiEdit.PlanDelete(Document, carets, true, false, false).Where((_, i) => !carets.Items[i].IsEmpty).ToList();
        return plan.Count > 0 && Run(plan, carets, null);
    }

    /// <summary>One mark that F8 of the "pspad" profile sets; the next F8 selects from it to the caret.</summary>
    private int? _mark;

    /// <summary>Sets the mark at the caret, or, when a mark exists, selects from the mark to the caret and clears it.</summary>
    public void MarkOrSelectToMark()
    {
        var p = PrimarySel();
        if (_mark is not { } mark) { _mark = p.Caret; return; }
        _mark = null;
        int at = Math.Clamp(mark, 0, Document.TextLength);
        DropOthers();
        SetPrimaryOnly(new Sel(at, p.Caret));
    }

    public bool HasMark => _mark is not null;

    // ---- keys ----

    /// <summary>The caret and delete keys as commands of the registry, so a macro records and replays them; a move has a variant that extends the selection (Shift).</summary>
    public static readonly (string Id, KeyStep Step, bool Extend)[] StepCommands =
    [
        ("nks.edit.cursorLeft", KeyStep.Left, false), ("nks.edit.cursorLeftSelect", KeyStep.Left, true),
        ("nks.edit.cursorRight", KeyStep.Right, false), ("nks.edit.cursorRightSelect", KeyStep.Right, true),
        ("nks.edit.cursorWordLeft", KeyStep.WordLeft, false), ("nks.edit.cursorWordLeftSelect", KeyStep.WordLeft, true),
        ("nks.edit.cursorWordRight", KeyStep.WordRight, false), ("nks.edit.cursorWordRightSelect", KeyStep.WordRight, true),
        ("nks.edit.cursorUp", KeyStep.Up, false), ("nks.edit.cursorUpSelect", KeyStep.Up, true),
        ("nks.edit.cursorDown", KeyStep.Down, false), ("nks.edit.cursorDownSelect", KeyStep.Down, true),
        ("nks.edit.cursorLineStart", KeyStep.LineStart, false), ("nks.edit.cursorLineStartSelect", KeyStep.LineStart, true),
        ("nks.edit.cursorLineEnd", KeyStep.LineEnd, false), ("nks.edit.cursorLineEndSelect", KeyStep.LineEnd, true),
        ("nks.edit.cursorDocumentStart", KeyStep.DocumentStart, false), ("nks.edit.cursorDocumentStartSelect", KeyStep.DocumentStart, true),
        ("nks.edit.cursorDocumentEnd", KeyStep.DocumentEnd, false), ("nks.edit.cursorDocumentEndSelect", KeyStep.DocumentEnd, true),
        ("nks.edit.deleteLeft", KeyStep.DeleteLeft, false), ("nks.edit.deleteRight", KeyStep.DeleteRight, false),
        ("nks.edit.deleteWordLeft", KeyStep.DeleteWordLeft, false), ("nks.edit.deleteWordRight", KeyStep.DeleteWordRight, false),
    ];

    /// <summary>
    /// Asked with the command of a caret or delete key (<see cref="StepCommands"/>) before the key acts; true when that command ran instead.
    /// The window runs the command only while a macro is recorded, so the macro records the key; otherwise the key works as always.
    /// </summary>
    public Func<string, bool>? KeyCommand { get; set; }

    /// <summary>The command of a caret or delete key: the keys of the area and of <see cref="HandleCaretKey"/> on this platform.</summary>
    private static string? StepCommandOf(KeyEventArgs e)
    {
        var m = e.KeyModifiers;
        bool shift = m.HasFlag(KeyModifiers.Shift), mac = OperatingSystem.IsMacOS();
        bool ctrl = m.HasFlag(KeyModifiers.Control), alt = m.HasFlag(KeyModifiers.Alt), meta = m.HasFlag(KeyModifiers.Meta);
        bool plain = !ctrl && !alt && !meta;
        bool word = mac ? alt && !ctrl && !meta : ctrl && !alt && !meta;
        bool command = mac && meta && !alt && !ctrl;
        KeyStep? step = e.Key switch
        {
            Key.Left when plain => KeyStep.Left,
            Key.Right when plain => KeyStep.Right,
            Key.Left when word => KeyStep.WordLeft,
            Key.Right when word => KeyStep.WordRight,
            Key.Left when command => KeyStep.LineStart,
            Key.Right when command => KeyStep.LineEnd,
            Key.Up when plain => KeyStep.Up,
            Key.Down when plain => KeyStep.Down,
            Key.Up when command => KeyStep.DocumentStart,
            Key.Down when command => KeyStep.DocumentEnd,
            Key.Home when plain => KeyStep.LineStart,
            Key.End when plain => KeyStep.LineEnd,
            Key.Home when word && !mac => KeyStep.DocumentStart,
            Key.End when word && !mac => KeyStep.DocumentEnd,
            Key.Back when plain && !shift => KeyStep.DeleteLeft,
            Key.Delete when plain && !shift => KeyStep.DeleteRight,
            Key.Back when word && !shift => KeyStep.DeleteWordLeft,
            Key.Delete when word && !shift => KeyStep.DeleteWordRight,
            _ => null,
        };
        if (step is not { } s) return null;
        bool extend = shift && s < KeyStep.DeleteLeft;
        return Array.Find(StepCommands, c => c.Step == s && c.Extend == extend).Id;
    }

    /// <summary>
    /// What the key of <paramref name="step"/> does, as a command: with several carets the moves and deletions of this class, with one the command of the
    /// editor area that its key runs. False when it does not apply (block mode, a deletion in a read-only document).
    /// </summary>
    public bool Step(KeyStep step, bool extend)
    {
        if (BlockMode) return false;
        if (step >= KeyStep.DeleteLeft)
        {
            if (IsReadOnly) return false;
            bool backwards = step is KeyStep.DeleteLeft or KeyStep.DeleteWordLeft, word = step is KeyStep.DeleteWordLeft or KeyStep.DeleteWordRight;
            if (DeleteAtCarets(backwards, word)) return true;
            if (step == KeyStep.DeleteLeft && Settings.AutoClosePairs && !Settings.PspadPairs && !IsBlockSelection && PairedCharacters.TryBackspace(Document, GetSelections()[0], out var paired))
            {
                SetSelections([paired]);
                return true;
            }
            return RunAreaCommand(word ? (backwards ? EditingCommands.DeletePreviousWord : EditingCommands.DeleteNextWord) : backwards ? EditingCommands.Backspace : EditingCommands.Delete);
        }
        if (step is KeyStep.LineStart or KeyStep.LineEnd && Settings.EnhancedHomeEnd)
            return MoveCarets(step == KeyStep.LineStart ? CaretMove.LineStartEnhanced : CaretMove.LineEndEnhanced, extend);
        if (IsMulti)
        {
            if (step is KeyStep.Up or KeyStep.Down) return MoveVertically(step == KeyStep.Up ? -1 : 1, extend);
            return MoveCarets(step switch
            {
                KeyStep.Left => CaretMove.Left,
                KeyStep.Right => CaretMove.Right,
                KeyStep.WordLeft => CaretMove.WordLeft,
                KeyStep.WordRight => CaretMove.WordRight,
                KeyStep.LineStart => CaretMove.LineStart,
                KeyStep.LineEnd => CaretMove.LineEnd,
                KeyStep.DocumentStart => CaretMove.DocumentStart,
                _ => CaretMove.DocumentEnd,
            }, extend);
        }
        return RunAreaCommand(step switch
        {
            KeyStep.Left => extend ? EditingCommands.SelectLeftByCharacter : EditingCommands.MoveLeftByCharacter,
            KeyStep.Right => extend ? EditingCommands.SelectRightByCharacter : EditingCommands.MoveRightByCharacter,
            KeyStep.WordLeft => extend ? EditingCommands.SelectLeftByWord : EditingCommands.MoveLeftByWord,
            KeyStep.WordRight => extend ? EditingCommands.SelectRightByWord : EditingCommands.MoveRightByWord,
            KeyStep.Up => extend ? EditingCommands.SelectUpByLine : EditingCommands.MoveUpByLine,
            KeyStep.Down => extend ? EditingCommands.SelectDownByLine : EditingCommands.MoveDownByLine,
            KeyStep.LineStart => extend ? EditingCommands.SelectToLineStart : EditingCommands.MoveToLineStart,
            KeyStep.LineEnd => extend ? EditingCommands.SelectToLineEnd : EditingCommands.MoveToLineEnd,
            KeyStep.DocumentStart => extend ? EditingCommands.SelectToDocumentStart : EditingCommands.MoveToDocumentStart,
            _ => extend ? EditingCommands.SelectToDocumentEnd : EditingCommands.MoveToDocumentEnd,
        });
    }

    private bool RunAreaCommand(RoutedCommand command)
    {
        if (!command.CanExecute(null, TextArea)) return false;
        command.Execute(null, TextArea);
        return true;
    }

    private bool HandleCaretKey(KeyEventArgs e)
    {
        if (BlockMode) return false;
        var m = e.KeyModifiers;
        bool shift = m.HasFlag(KeyModifiers.Shift);
        bool mac = OperatingSystem.IsMacOS();
        bool ctrl = m.HasFlag(KeyModifiers.Control), alt = m.HasFlag(KeyModifiers.Alt), meta = m.HasFlag(KeyModifiers.Meta);
        bool plain = !ctrl && !alt && !meta;
        bool word = mac ? alt && !ctrl && !meta : ctrl && !alt && !meta;
        bool multi = IsMulti;

        switch (e.Key)
        {
            case Key.Home or Key.End when plain && Settings.EnhancedHomeEnd:
                return Handle(e, MoveCarets(e.Key == Key.Home ? CaretMove.LineStartEnhanced : CaretMove.LineEndEnhanced, shift));
            case Key.Home or Key.End when multi && (plain || (!mac && ctrl && !alt)):
                return Handle(e, MoveCarets(plain ? (e.Key == Key.Home ? CaretMove.LineStart : CaretMove.LineEnd) : (e.Key == Key.Home ? CaretMove.DocumentStart : CaretMove.DocumentEnd), shift));
            case Key.Left or Key.Right when multi && plain:
                return Handle(e, MoveCarets(e.Key == Key.Left ? CaretMove.Left : CaretMove.Right, shift));
            case Key.Left or Key.Right when multi && word:
                return Handle(e, MoveCarets(e.Key == Key.Left ? CaretMove.WordLeft : CaretMove.WordRight, shift));
            case Key.Left or Key.Right when multi && mac && meta && !alt && !ctrl:
                return Handle(e, MoveCarets(e.Key == Key.Left ? CaretMove.LineStart : CaretMove.LineEnd, shift));
            case Key.Up or Key.Down when multi && plain:
                return Handle(e, MoveVertically(e.Key == Key.Up ? -1 : 1, shift));
            case Key.Up or Key.Down when multi && mac && meta && !alt && !ctrl:
                return Handle(e, MoveCarets(e.Key == Key.Up ? CaretMove.DocumentStart : CaretMove.DocumentEnd, shift));
            case Key.Back or Key.Delete when !IsReadOnly && (plain || word) && !shift:
                return Handle(e, DeleteAtCarets(e.Key == Key.Back, word && !plain));
            case Key.Escape when multi && m == KeyModifiers.None:
                ClearCarets();
                e.Handled = true;
                return true;
            case Key.PageUp or Key.PageDown when multi:
                ClearCarets(); // the page keys move one caret only
                return false;
        }
        return false;
    }

    private static bool Handle(KeyEventArgs e, bool handled)
    {
        if (handled) e.Handled = true;
        return handled;
    }

    // ---- mouse ----

    private int? OffsetAt(PointerEventArgs e)
    {
        var view = TextArea.TextView;
        return view.GetPosition(e.GetPosition(view) + view.ScrollOffset) is { } pos ? Document.GetOffset(pos.Location) : null;
    }

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TextArea).Properties.IsLeftButtonPressed || BlockMode) return;
        _preAlt = null;
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (alt && !Settings.AltDragBlock)
        {
            // decided on release: a click adds a caret, a drag makes one caret per row
            _preAlt = GetCarets();
            _altPress = e.GetPosition(TextArea);
            return;
        }
        if (e.ClickCount == 2 && !IsReadOnly || e.ClickCount == 2)
        {
            if (Settings.Smart.SelectVariable && Smart() is { } smart && OffsetAt(e) is { } at && smart.TrySelectVariable(at, out int start, out int end))
            {
                DropOthers();
                SetPrimaryOnly(new Sel(start, end));
                e.Handled = true;
                return;
            }
        }
        if (IsMulti) DropOthers();
    }

    private void OnPointerReleasedAfter(object? sender, PointerReleasedEventArgs e)
    {
        if (_preAlt is not { } before) return;
        _preAlt = null;
        var point = e.GetPosition(TextArea);
        bool dragged = Math.Abs(point.X - _altPress.X) > 3 || Math.Abs(point.Y - _altPress.Y) > 3;
        if (dragged && TextArea.Selection is RectangleSelection rect)
        {
            var rows = rect.Segments.Select(s => new Sel(s.StartOffset, s.EndOffset)).ToList();
            if (rows.Count > 0) SetCarets(MultiCaret.FromRows(rows), reveal: false);
            return;
        }
        if (OffsetAt(e) is { } at) SetCarets(MultiCaret.AddOrRemoveAt(before, at), reveal: false);
    }

    // ---- drawing ----

    private IEnumerable<Sel> VisibleOthers(TextView view)
    {
        var lines = view.VisualLines;
        if (lines.Count == 0) yield break;
        int from = lines[0].FirstDocumentLine.Offset, to = lines[^1].LastDocumentLine.EndOffset;
        foreach (var s in OtherSels())
            if (s.End >= from && s.Start <= to) yield return s;
    }

    private sealed class SelectionLayer(EditorSurface owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            long trace = Core.TypingTrace.Begin();
            try { DrawCore(textView, drawingContext); }
            finally { Core.TypingTrace.End("carets.drawSelections", trace); }
        }

        private void DrawCore(TextView textView, DrawingContext drawingContext)
        {
            if (owner._others is null || owner.TextArea.SelectionBrush is not { } brush || !textView.VisualLinesValid) return;
            foreach (var s in owner.VisibleOthers(textView))
            {
                if (s.IsEmpty) continue;
                var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = owner.TextArea.SelectionCornerRadius };
                builder.AddSegment(textView, new TextSegment { StartOffset = s.Start, EndOffset = s.End });
                if (builder.CreateGeometry() is { } geometry) drawingContext.DrawGeometry(brush, owner.TextArea.SelectionBorder, geometry);
            }
        }
    }

    private sealed class CaretLayer(EditorSurface owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Caret;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            long trace = Core.TypingTrace.Begin();
            try { DrawCore(textView, drawingContext); }
            finally { Core.TypingTrace.End("carets.drawCarets", trace); }
        }

        private void DrawCore(TextView textView, DrawingContext drawingContext)
        {
            if (!owner.DrawsOwnCarets || !owner._caretOn || owner._caretBrush is not { } brush || !textView.VisualLinesValid) return;
            if (owner._others is null) // one caret: drawn only while the editor has the keyboard, as the caret of AvaloniaEdit is
            {
                if (owner.TextArea.IsKeyboardFocusWithin) DrawCaret(textView, drawingContext, brush, owner.CaretOffset, 1);
                return;
            }
            DrawCaret(textView, drawingContext, brush, owner.PrimarySel().Caret, 1);
            using (drawingContext.PushOpacity(0.7))
                foreach (var s in owner.VisibleOthers(textView)) DrawCaret(textView, drawingContext, brush, s.Caret, 0.7);
        }

        private void DrawCaret(TextView view, DrawingContext dc, IBrush brush, int offset, double opacity)
        {
            var location = owner.Document.GetLocation(offset);
            if (view.GetVisualLine(location.Line) is null) return;
            var position = new TextViewPosition(location);
            var top = view.GetVisualPosition(position, VisualYPosition.LineTop) - view.ScrollOffset;
            var bottom = view.GetVisualPosition(position, VisualYPosition.LineBottom) - view.ScrollOffset;
            if (bottom.Y < 0 || top.Y > view.Bounds.Height) return;
            dc.FillRectangle(brush, new Rect(Math.Floor(top.X), top.Y, 1.5, bottom.Y - top.Y));
        }
    }
}

/// <summary>A caret move or a deletion of the keyboard (<see cref="EditorSurface.StepCommands"/>); the deletions come last.</summary>
internal enum KeyStep { Left, Right, WordLeft, WordRight, Up, Down, LineStart, LineEnd, DocumentStart, DocumentEnd, DeleteLeft, DeleteRight, DeleteWordLeft, DeleteWordRight }
