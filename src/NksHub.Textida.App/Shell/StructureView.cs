using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureView : DockPanel, IDisposable
{
    internal const int PageSize = 64;
    internal const int MaximumItems = 256;
    private readonly Func<StructureSource?> source;
    private readonly Func<string, Task> copy;
    private readonly Func<string, string, string, Task<string?>> prompt;
    private readonly StructureTree tree = new() { Name = "StructureTree" };
    private readonly TextBox filter = new() { Name = "StructureFilter", MaxLength = 16384, MinWidth = 100 };
    private readonly TextBox namespaces = new() { Name = "StructureNamespaces", MaxLength = 16384, AcceptsReturn = true, MaxHeight = 72 };
    private readonly ComboBox scope = new() { Name = "StructureSearchScope", SelectedIndex = 0, MinWidth = 80 };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock syntax = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 };
    private readonly Expander typeCounts = new();
    private readonly Dictionary<StructureKind, TextBlock> countLabels = [];
    private readonly Button refresh = new(), cancel = new(), up = new(), jump = new(), copyPath = new(), copySource = new(), edit = new();
    private readonly Button find = new(), clear = new(), previous = new(), next = new(), issue = new(), nextIssue = new();
    private readonly Dictionary<StructureTreeNode, Row> rows = [];
    private Task work = Task.CompletedTask;
    private Session? session;
    private Request? filterRequest, caretRequest;
    private long generation, clock;
    private bool disposed, changing, showingResults, selecting;
    private long treeVersion;
    private long resultPage;
    private readonly Dictionary<long, long> resultCursors = [];
    private int issuePosition;
    private string statusKey = K.StructureUnsupported;
    private (string Name, object Value)[] statusArguments = [];

    private sealed class Session(StructureSource source, long generation)
    {
        public StructureSource Source = source;
        public long Generation { get; } = generation;
        public CancellationTokenSource Cancellation { get; } = new();
        public StructureAnalysis? Analysis;
        public bool Parsing = true;
        public CancellationToken Token => Cancellation.Token;
    }

    private sealed class Request(CancellationToken parent) : IDisposable
    {
        private readonly CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        private readonly object gate = new();
        private bool disposed;
        public CancellationToken Token => source.Token;
        public void Cancel() { lock (gate) { if (!disposed) source.Cancel(); } }
        public void Dispose() { lock (gate) { if (!disposed) { disposed = true; source.Dispose(); } } }
    }

    public StructureView(Func<StructureSource?> source, Func<string, Task> copy,
        Func<string, string, string, Task<string?>> prompt)
    {
        this.source = source;
        this.copy = copy;
        this.prompt = prompt;
        BuildPresentation();
        refresh.Click += (_, _) => { if (!disposed) Invalidate(source()); };
        cancel.Click += (_, _) => CancelWork();
        up.Click += (_, _) => ShowParent();
        jump.Click += (_, _) => Jump(openSource: true);
        copyPath.Click += async (_, _) => await CopyAsync(path: true);
        copySource.Click += async (_, _) => await CopyAsync(path: false);
        edit.Click += async (_, _) => await EditAsync();
        find.Click += (_, _) => StartResults(0);
        clear.Click += (_, _) => ShowDocument();
        previous.Click += (_, _) => StartResults(Math.Max(0, resultPage - 1));
        next.Click += (_, _) => StartResults(resultPage + 1);
        filter.TextChanged += (_, _) =>
        {
            if (changing) return;
            if (string.IsNullOrEmpty(filter.Text)) ShowDocument();
            else StartResults(0, debounce: true);
        };
        namespaces.TextChanged += (_, _) => { if (!changing && scope.SelectedIndex == 3 && !string.IsNullOrEmpty(filter.Text)) StartResults(0, debounce: true); };
        scope.SelectionChanged += (_, _) => { if (!changing && !string.IsNullOrEmpty(filter.Text)) StartResults(0, debounce: true); };
        filter.KeyDown += (_, e) => { if (e.Key == Key.Enter) { StartResults(0); e.Handled = true; } };
        tree.SelectionChanged += (_, _) => { UpdateButtons(); if (!selecting) Jump(); };
        tree.DoubleTapped += (_, e) => { if (!FromButton(e.Source)) Jump(); };
        tree.NodeActivated += (_, _) => Jump();
        issue.Click += (_, _) => JumpIssue();
        nextIssue.Click += (_, _) => { issuePosition++; ShowIssues(); };
        ApplyTexts();
    }

    protected override Type StyleKeyOverride => typeof(DockPanel);
    internal Task Parsing { get; private set; } = Task.CompletedTask;
    internal Task Working => work;
    internal StructureTree Tree => tree;
    internal TextBox Filter => filter;
    internal ComboBox SearchScope => scope;
    internal Button FindButton => find;
    internal Button NextButton => next;
    internal Button PreviousButton => previous;
    internal Button CopyPathButton => copyPath;
    internal Button CopySourceButton => copySource;
    internal Button EditButton => edit;
    internal Button JumpButton => jump;
    internal Button IssueButton => issue;
    internal Expander TypeCounts => typeCounts;
    internal int CachedItems => CountItems() + freeRows.Count;
    internal string Message => status.Text ?? "";

    private static WrapPanel Bar(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.Margin = new Thickness(0, 0, 4, 2); panel.Children.Add(control); }
        return panel;
    }

    private static bool FromButton(object? value) => value is Visual visual
        && (visual is Button || visual.GetVisualAncestors().Any(ancestor => ancestor is Button));

    public void ApplyTexts()
    {
        (Button Button, string Key)[] buttons =
        [
            (refresh, K.StructureRefresh), (cancel, K.StructureCancel), (up, K.StructureUp), (jump, K.StructureJump), (copyPath, K.StructureCopyPath),
            (copySource, K.StructureCopySource), (edit, K.StructureEdit), (find, K.StructureFind), (clear, K.StructureClear),
            (previous, K.StructurePrevious), (next, K.StructureNext), (nextIssue, K.StructureNextIssue),
        ];
        foreach (var (button, key) in buttons)
        {
            if (button.Tag is not string) button.Content = Loc.Get(key);
            ToolTip.SetTip(button, Loc.Get(key));
            AutomationProperties.SetName(button, Loc.Get(key));
        }
        changing = true;
        int selected = Math.Max(0, scope.SelectedIndex);
        scope.Items.Clear();
        foreach (string key in new[] { K.StructureScopeName, K.StructureScopeValue, K.StructureScopePath, session?.Source.Format == StructureFormat.Xml ? K.StructureScopeQueryXml : K.StructureScopeQueryJson })
            scope.Items.Add(Loc.Get(key));
        scope.SelectedIndex = selected;
        changing = false;
        filter.PlaceholderText = Loc.Get(K.StructureFilter);
        namespaces.PlaceholderText = Loc.Get(K.StructureNamespaces);
        AutomationProperties.SetName(filter, Loc.Get(K.StructureFilter));
        AutomationProperties.SetName(namespaces, Loc.Get(K.StructureNamespaces));
        AutomationProperties.SetName(scope, Loc.Get(K.StructureFind));
        AutomationProperties.SetName(tree, Loc.Get(K.StructureTree));
        namespacePanel.IsVisible = session?.Source.Format == StructureFormat.Xml;
        syntax.Text = Loc.Get(session?.Source.Format switch
        {
            StructureFormat.Xml => K.StructureSyntaxXml,
            StructureFormat.Yaml or StructureFormat.Neon => K.StructureSyntaxConfig,
            _ => K.StructureSyntaxJson,
        });
        ShowTypeCounts();
        foreach (var row in rows.Values) Label(row);
        status.Text = Loc.Get(statusKey, statusArguments);
        ShowIssues();
        UpdateButtons();
        ApplyPresentationTexts();
    }

    public bool FocusTree()
    {
        if (tree.SelectedNode is { } node) tree.SelectAndReveal(node);
        return tree.IsKeyboardFocusWithin || tree.List.Focus() || filter.Focus();
    }

    public bool FocusContent() => Presentation switch
    {
        StructureViewPresentation.Graph => graph.Focus(),
        StructureViewPresentation.Table => table.Focus(),
        _ => FocusTree(),
    };

    public void Sync()
    {
        if (disposed) return;
        var current = source();
        if (current == null) { if (session != null) Invalidate(null); return; }
        if (session == null || !SameSource(current, session.Source)) { Invalidate(current); return; }
        bool moved = current.Offset != session.Source.Offset;
        session.Source = current;
        UpdateButtons();
        if (moved)
        {
            if (sourceJump is { } jump && ReferenceEquals(jump.Owner, session) && jump.Offset == current.Offset
                && session.Analysis is { IsValid: true }
                && (CurrentRow(jump.Row, jump.RequestId, jump.NodeId) || ReferenceEquals(tableSelection, jump.Row)))
            {
                caretRequest?.Cancel();
                if (!ReferenceEquals(tableSelection, jump.Row)) Mark(jump.Row, revealParents: false);
            }
            else MarkCaret(session);
        }
    }

    private static bool SameSource(StructureSource a, StructureSource b) => ReferenceEquals(a.Key, b.Key)
        && a.DocumentId == b.DocumentId && a.Revision == b.Revision && ReferenceEquals(a.Codec, b.Codec)
        && a.BomLength == b.BomLength && a.Format == b.Format;

    private bool Current(Session value) => !disposed && value.Generation == generation && ReferenceEquals(session, value)
        && !value.Token.IsCancellationRequested && source() is { } latest && SameSource(latest, value.Source);

    private void Invalidate(StructureSource? current)
    {
        tableRequest?.Cancel(); tableOwner = null; tableContainer = null; tableSelection = null;
        tableResults = null; tableIsResultPage = false; tablePages = new();
        table.SetPage(new([], [], 0, 0, false, false));
        cancellationCapture = null;
        cancellationCleanups?.Clear();
        var old = session;
        old?.Cancellation.Cancel();
        filterRequest?.Cancel(); caretRequest?.Cancel();
        session = null;
        generation++;
        ResetRows(retainPool: false);
        showingResults = false; resultCursors.Clear(); issuePosition = 0;
        if (old != null) Queue(async () => { old.Analysis?.Dispose(); old.Analysis = null; old.Cancellation.Dispose(); await Task.CompletedTask; });
        if (current == null || disposed) { SetStatus(K.StructureUnsupported); ApplyTexts(); return; }
        var nextSession = new Session(current, generation);
        session = nextSession;
        SetStatus(K.StructureParsing);
        ApplyTexts();
        Parsing = Queue(async () =>
        {
            try
            {
                await Task.Delay(300, nextSession.Token).ConfigureAwait(false);
                DocumentSnapshot? snapshot = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!Current(nextSession) || source() is not { } latest || !SameSource(latest, nextSession.Source)) return null;
                    var captured = latest.Snapshot();
                    return captured.DocumentId == latest.DocumentId && captured.Revision == latest.Revision && ReferenceEquals(captured.Codec, latest.Codec) ? captured : null;
                });
                if (snapshot == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { if (Current(nextSession)) { nextSession.Parsing = false; SetStatus(K.StructureStale); UpdateButtons(); } });
                    return;
                }
                long lastProgress = 0;
                var analysis = StructureAnalysis.Parse(snapshot, current.BomLength, current.Format, nextSession.Token, bytes =>
                {
                    long now = Environment.TickCount64;
                    if (now - lastProgress < 150) return;
                    lastProgress = now;
                    PostProgress(nextSession, bytes, snapshot.ByteLength);
                });
                bool published = false;
                try
                {
                    RowData root = ReadRow(analysis.Index, analysis.Index.ReadNode(0), nextSession.Token);
                    var page = analysis.IsValid ? ReadPage(analysis.Index, root.Node, -1, nextSession.Token) : new ChildPage([], -1, true);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!Current(nextSession)) return;
                        nextSession.Analysis = analysis; nextSession.Parsing = false; published = true;
                        InstallRoot(root, page);
                        ShowStats(nextSession); ShowIssues(); UpdateButtons(); MarkCaret(nextSession);
                        if (Presentation == StructureViewPresentation.Table) LoadTable();
                        if (analysis.IsValid && !string.IsNullOrEmpty(filter.Text)) StartResults(0);
                    });
                }
                finally { if (!published) analysis.Dispose(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { await Dispatcher.UIThread.InvokeAsync(() => { if (Current(nextSession)) { nextSession.Parsing = false; SetStatus(K.StructureFailed, ("message", error.Message)); UpdateButtons(); } }); }
        });
    }

    // Keep the UI closure here so skipped 150 ms updates do not allocate.
    private void PostProgress(Session owner, long bytes, long total)
    {
        Dispatcher.UIThread.Post(() => { if (Current(owner) && owner.Parsing) SetStatus(K.StructureProgress, ("bytes", bytes), ("total", total)); });
    }

    private Task Queue(Func<Task> action)
    {
        Task previousWork = work;
        work = Task.Run(async () =>
        {
            try { await previousWork.ConfigureAwait(false); } catch { }
            await action().ConfigureAwait(false);
        });
        if (observeCancellation && cancellationCapture is { } capture && ReferenceEquals(session, capture.Owner))
            capture.Extend(CancellationTail(capture.Owner));
        return work;
    }

    private Task Run<T>(Session owner, Func<CancellationToken, T> action, Action<T> publish, CancellationToken? request = null, int delay = 0)
    {
        CancellationToken ct = request ?? owner.Token;
        return Queue(async () =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (delay > 0) await Task.Delay(delay, ct).ConfigureAwait(false);
                T result = action(ct);
                ct.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() => { if (Current(owner) && !ct.IsCancellationRequested) publish(result); });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { await Dispatcher.UIThread.InvokeAsync(() => { if (Current(owner) && !ct.IsCancellationRequested) SetStatus(K.StructureFailed, ("message", error.Message)); }); }
        });
    }

    private void SetStatus(string key, params (string Name, object Value)[] arguments)
    {
        statusKey = key; statusArguments = arguments;
        status.Text = Loc.Get(key, arguments);
    }

    private void ShowStats(Session owner)
    {
        if (owner.Analysis is not { } analysis) return;
        var index = analysis.Index;
        ShowTypeCounts();
        if (!analysis.IsValid) { SetStatus(K.StructureInvalid, ("errors", index.IssueCount)); return; }
        SetStatus(K.StructureStats, ("nodes", index.Count), ("depth", index.MaximumDepth),
            ("bytes", index.Source.ByteLength), ("duplicates", analysis.DuplicateKeys));
    }

    private void ShowTypeCounts()
    {
        typeCounts.Header = Loc.Get(K.StructureTypeCounts);
        foreach (var (kind, label) in countLabels)
            label.Text = Loc.Get(KindKey(kind)) + ": " + (session?.Analysis?.Index.CountOf(kind) ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    private void Programmatic(Action action)
    {
        bool previousSelection = selecting;
        selecting = true;
        try { using (tree.DeferRefresh()) action(); }
        finally { selecting = previousSelection; }
    }

    public void Suspend() { if (!disposed) Invalidate(null); }
    public void Dispose() { if (disposed) return; disposed = true; graph.Dispose(); Invalidate(null); }
}
