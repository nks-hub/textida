using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Agents.Hooks;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.App.Shell;

/// <summary>What the hooks editor needs from the window that opened it: the text of the file as the tab holds it now, its revision, and the way a changed text reaches the document.</summary>
/// <param name="Propose">Sends the new text of the file to the change review; true when the document took (some of) it.</param>
internal sealed record HooksHost(string? Path, string Title, HookProduct Product, HookLimits Limits, Func<string> ReadText, Func<long> Revision, Func<string, Task<bool>> Propose);

/// <summary>
/// Editor of the hooks of an agent (P8b.3.1): the tree event, matcher group, handler of the file, a form for one hook, the findings of the schema check, and a dry run that shows what the hook would
/// receive and what its exit codes would do. It works on a copy of the text of the tab. Nothing in it starts a command: the dry run is read from the public documentation of the product, and the only
/// way out is "Propose changes", which hands the new text to the change review, where the user applies it as an undoable edit of the tab. The file is saved by the normal save of the tab.
/// </summary>
internal sealed class HooksEditorWindow : Window
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private sealed record NodeTag(string Event, string? Matcher, HookRef? Hook);

    private readonly HooksHost _host;
    private HookProduct _product;
    private string _text = "";
    private long _revision;
    private HookDocument? _document;
    private NodeTag? _selected;
    private bool _loading;

    private readonly ComboBox _productBox = new() { MinWidth = 160 };
    private readonly TreeView _tree = new() { Background = Brushes.Transparent };
    private readonly ComboBox _eventBox = new() { MinWidth = 260 }, _typeBox = new() { MinWidth = 160 }, _failureBox = new() { MinWidth = 160 };
    private readonly TextBox _matcher = new() { FontFamily = Mono }, _main = new() { FontFamily = Mono }, _server = new() { FontFamily = Mono }, _tool = new() { FontFamily = Mono },
        _timeout = new() { FontFamily = Mono }, _statusMessage = new(), _condition = new() { FontFamily = Mono }, _tested = new() { FontFamily = Mono };
    private readonly TextBox _dry = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = Mono, TextWrapping = TextWrapping.NoWrap, MinHeight = 150 };
    private readonly ListBox _findings = new() { Background = Brushes.Transparent, MinHeight = 80, MaxHeight = 150 };
    private readonly TextBlock _fileLabel = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private readonly TextBlock _productLabel = new(), _treeLabel = new(), _eventLabel = new(), _matcherLabel = new(), _typeLabel = new(), _mainLabel = new(), _serverLabel = new(), _toolLabel = new(),
        _timeoutLabel = new(), _statusLabel = new(), _conditionLabel = new(), _failureLabel = new(), _testedLabel = new(), _dryLabel = new(), _findingsLabel = new();
    private readonly TextBlock _matcherHint = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _new = new() { MinWidth = 110 }, _apply = new() { MinWidth = 130, IsDefault = true }, _delete = new() { MinWidth = 110 }, _dryRun = new() { MinWidth = 130 },
        _propose = new() { MinWidth = 150 }, _close = new() { MinWidth = 84, IsCancel = true };

    public HooksEditorWindow(HooksHost host)
    {
        _host = host;
        _product = host.Product;
        HelpContext.OnF1(this, "tools.hooks");
        Classes.Add("shell");
        Width = 1120;
        Height = 780;
        MinWidth = 780;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var label in new[] { _productLabel, _treeLabel, _eventLabel, _matcherLabel, _typeLabel, _mainLabel, _serverLabel, _toolLabel, _timeoutLabel, _statusLabel, _conditionLabel, _failureLabel, _testedLabel, _dryLabel, _findingsLabel })
            label.FontWeight = FontWeight.SemiBold;

        var form = new StackPanel { Spacing = 4 };
        foreach (var (label, box) in new (TextBlock, Control)[]
        {
            (_eventLabel, _eventBox), (_matcherLabel, _matcher), (_matcherHint, new Border()), (_typeLabel, _typeBox), (_mainLabel, _main), (_serverLabel, _server), (_toolLabel, _tool),
            (_timeoutLabel, _timeout), (_statusLabel, _statusMessage), (_conditionLabel, _condition), (_failureLabel, _failureBox), (_testedLabel, _tested),
        })
        {
            form.Children.Add(label);
            if (box is not Border) form.Children.Add(box);
        }
        form.Children.Add(_dryLabel);
        form.Children.Add(_dry);
        var formButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0), Children = { _new, _apply, _delete, _dryRun } };
        form.Children.Add(formButtons);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 4 };
        void Place(Control c, int row) { Grid.SetRow(c, row); left.Children.Add(c); }
        Place(new StackPanel { Spacing = 4, Children = { _productLabel, _productBox } }, 0);
        Place(_treeLabel, 1);
        Place(new Border { Child = _tree, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 2);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), ColumnSpacing = 16, Margin = new Thickness(18, 14) };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(left);
        body.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _propose, _close } };
        var footer = new StackPanel { Margin = new Thickness(18, 0, 18, 12), Spacing = 6, Children = { _fileLabel, _findingsLabel, new Border { Child = _findings, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, _note, buttons } };
        var root = new ReadingDockPanel { Children = { footer, body } };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = root;

        _productBox.ItemsSource = new[] { ProductName(HookProduct.ClaudeCode), ProductName(HookProduct.Codex) };
        _productBox.SelectedIndex = _product == HookProduct.ClaudeCode ? 0 : 1;
        _productBox.SelectionChanged += (_, _) => { if (!_loading) ChooseProduct(_productBox.SelectedIndex == 0 ? HookProduct.ClaudeCode : HookProduct.Codex); };
        _tree.SelectionChanged += (_, _) => { if (!_loading) TreeSelected(); };
        _typeBox.SelectionChanged += (_, _) => { if (!_loading) ShowFieldsOfType(); };
        _eventBox.SelectionChanged += (_, _) => { if (!_loading) ShowMatcherHint(); };
        _new.Click += (_, _) => NewNow();
        _apply.Click += (_, _) => ApplyNow();
        _delete.Click += (_, _) => DeleteNow();
        _dryRun.Click += (_, _) => DryRunNow();
        _propose.Click += (_, _) => _ = ProposeNow();
        _close.Click += (_, _) => Close();
        // subscribed only while open, so a window that is never shown or is closed does not stay reachable from Loc
        Opened += (_, _) => { Loc.Changed += OnLanguageChanged; ApplyTexts(); _eventBox.Focus(); };
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        Reload(host.ReadText(), host.Revision());
    }

    // ---- state for tests and the host ----

    /// <summary>The text with the changes made here so far; the tab has it only after the change was proposed and applied.</summary>
    internal string WorkingText => _text;
    internal HookProduct Product => _product;
    internal string NoteText => _note.Text ?? "";
    internal string DryRunText => _dry.Text ?? "";
    internal IReadOnlyList<string> Findings => [.. _findings.Items.OfType<TextBlock>().Select(t => t.Text ?? "")];
    internal bool CanPropose => _propose.IsEnabled;

    /// <summary>The handlers of the tree as "event, matcher: summary", in order.</summary>
    internal IReadOnlyList<string> Handlers => _document is null ? [] : [.. _document.Events.SelectMany(e => e.Groups.SelectMany(g => g.Handlers.Select(h => $"{e.Name}, {g.Matcher}: {h.Summary}")))];

    internal string FieldEvent { get => _eventBox.SelectedItem as string ?? ""; set => _eventBox.SelectedItem = value; }
    internal string FieldMatcher { get => _matcher.Text ?? ""; set => _matcher.Text = value; }
    internal string FieldType { get => _typeBox.SelectedItem as string ?? ""; set => _typeBox.SelectedItem = value; }
    internal string FieldMain { get => _main.Text ?? ""; set => _main.Text = value; }
    internal string FieldTimeout { get => _timeout.Text ?? ""; set => _timeout.Text = value; }
    internal string FieldTested { get => _tested.Text ?? ""; set => _tested.Text = value; }
    internal TreeView Tree => _tree;

    private static string ProductName(HookProduct product) => product == HookProduct.ClaudeCode ? "Claude Code" : "Codex";

    private HookCatalog Catalog => HookCatalog.For(_product);

    private void SetNote(string text, bool error = false)
    {
        _note.Text = text;
        _note.Foreground = error ? ThemeColors.Danger : null;
    }

    // ---- loading ----

    private void Reload(string text, long revision)
    {
        _text = text;
        _revision = revision;
        Refresh();
        NewNow();
    }

    private void Refresh()
    {
        _loading = true;
        try
        {
            _eventBox.ItemsSource = Catalog.Events.Select(e => e.Name).ToList();
            _typeBox.ItemsSource = Catalog.HandlerTypes.Select(t => t.Name).ToList();
            _failureBox.ItemsSource = new[] { Loc.Get(K.HooksFailureUnset), "continue", "block" };
        }
        finally { _loading = false; }
        HookDocument.TryParse(_text, _product, _host.Limits, out _document, out _);
        RefreshTree();
        RefreshFindings();
        _propose.IsEnabled = _text != _host.ReadText() && !_findings.Items.OfType<TextBlock>().Any(t => t.Tag is HookSeverity.Error);
        _fileLabel.Text = _host.Path is { } path ? Loc.Get(K.HooksFile, ("path", path)) : Loc.Get(K.HooksFileUnsaved, ("title", _host.Title));
    }

    private void RefreshTree()
    {
        _loading = true;
        _tree.Items.Clear();
        if (_document is { } doc)
        {
            foreach (var ev in doc.Events)
            {
                var eventItem = new TreeViewItem { Header = ev.Name, Tag = new NodeTag(ev.Name, null, null), IsExpanded = true };
                foreach (var group in ev.Groups)
                {
                    var groupItem = new TreeViewItem { Header = group.Matcher.Length > 0 ? group.Matcher : Loc.Get(K.HooksTreeNoMatcher), Tag = new NodeTag(ev.Name, group.Matcher, null), IsExpanded = true };
                    foreach (var handler in group.Handlers)
                        groupItem.Items.Add(new TreeViewItem { Header = handler.Summary, Tag = new NodeTag(ev.Name, group.Matcher, new HookRef(ev.Name, group.Index, handler.Index)) });
                    eventItem.Items.Add(groupItem);
                }
                _tree.Items.Add(eventItem);
            }
            if (doc.Events.Count == 0) _tree.Items.Add(new TreeViewItem { Header = Loc.Get(K.HooksTreeEmpty), IsEnabled = false });
        }
        _loading = false;
    }

    private void RefreshFindings()
    {
        _findings.Items.Clear();
        var issues = HookValidator.Validate(_text, _product, _host.Limits);
        foreach (var issue in issues)
        {
            var (line, column) = LineColumn(_text, issue.Start);
            string text = Loc.Get(issue.Severity == HookSeverity.Error ? K.HooksFindingError : K.HooksFindingWarning, ("text", Describe(issue, line, column))) + (issue.Code == "syntax" ? "" : $" ({line}:{column})");
            _findings.Items.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Tag = issue.Severity });
        }
        if (issues.Count == 0) _findings.Items.Add(new TextBlock { Text = Loc.Get(K.HooksFindingsNone), TextWrapping = TextWrapping.Wrap });
    }

    private static (int Line, int Column) LineColumn(string text, int offset)
    {
        int line = 1, column = 1;
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n') { line++; column = 1; }
            else if (text[i] != '\r') column++;
        }
        return (line, column);
    }

    /// <summary>The finding in the language of the user: the checks of this editor have their own texts, the schema keywords share those of the schema features.</summary>
    internal static string Describe(HookIssue issue, int line, int column)
    {
        string Arg(int i) => i < issue.Args.Length ? issue.Args[i] : "";
        return issue.Code switch
        {
            "syntax" => SchemaTexts.For(Loc.Language).Syntax(line, column, issue.Detail),
            "hook.matcherIgnored" => Loc.Get(K.HooksIssueMatcherIgnored, ("event", Arg(0))),
            "hook.matcherPattern" => Loc.Get(K.HooksIssueMatcherPattern),
            "hook.emptyGroup" => Loc.Get(K.HooksIssueEmptyGroup),
            "hook.fieldNotForType" => Loc.Get(K.HooksIssueFieldNotForType, ("field", Arg(0)), ("type", Arg(1))),
            "hook.ifNeverRuns" => Loc.Get(K.HooksIssueIfNeverRuns, ("event", Arg(0))),
            "hook.shellIgnored" => Loc.Get(K.HooksIssueShellIgnored),
            "hook.skipped" => Loc.Get(K.HooksIssueSkipped, ("type", Arg(0))),
            "hook.timeoutLimit" => Loc.Get(K.HooksIssueTimeoutLimit, ("event", Arg(0))),
            "propertyNames" => Loc.Get(K.HooksIssueUnknownEvent, ("event", Arg(0))),
            "additionalProperties" => Loc.Get(K.HooksIssueUnknownField, ("field", Arg(0))),
            _ => SchemaTexts.For(Loc.Language).Issue(new SchemaIssue(issue.Code, issue.Detail, issue.Start, issue.End, issue.Pointer, issue.Args)),
        };
    }

    // ---- the form ----

    private void TreeSelected()
    {
        _selected = (_tree.SelectedItem as TreeViewItem)?.Tag as NodeTag;
        if (_selected is null) return;
        if (_selected.Hook is { } hook && _document?.Find(hook) is { } found) LoadForm(found.Event, found.Group, found.Handler);
        else LoadEmptyForm(_selected.Event, _selected.Matcher ?? "");
        _delete.IsEnabled = _selected.Hook is not null;
    }

    private void LoadEmptyForm(string eventName, string matcher)
    {
        _loading = true;
        _eventBox.SelectedItem = eventName;
        _matcher.Text = matcher;
        _typeBox.SelectedItem = "command";
        foreach (var box in new[] { _main, _server, _tool, _timeout, _statusMessage, _condition }) box.Text = "";
        _failureBox.SelectedIndex = 0;
        _loading = false;
        ShowFieldsOfType();
    }

    private void LoadForm(HookEventNode ev, HookGroupNode group, HookHandlerNode handler)
    {
        _loading = true;
        _eventBox.SelectedItem = ev.Name;
        _matcher.Text = group.Matcher;
        _typeBox.SelectedItem = handler.Type;
        _main.Text = handler.Text(MainField(handler.Type)) ?? "";
        _server.Text = handler.Text("server") ?? "";
        _tool.Text = handler.Text("tool") ?? "";
        _timeout.Text = handler.Raw("timeout", _text) ?? "";
        _statusMessage.Text = handler.Text("statusMessage") ?? "";
        _condition.Text = handler.Text("if") ?? "";
        _failureBox.SelectedIndex = handler.Text("onFailure") switch { "continue" => 1, "block" => 2, _ => 0 };
        _loading = false;
        ShowFieldsOfType();
    }

    private static string MainField(string type) => type switch { "http" => "url", "prompt" or "agent" => "prompt", "mcp_tool" => "", _ => "command" };

    private void NewNow()
    {
        _tree.SelectedItem = null;
        _selected = null;
        _delete.IsEnabled = false;
        LoadEmptyForm(Catalog.Events[0].Name, "");
        SetNote("");
    }

    private void ChooseProduct(HookProduct product)
    {
        if (product == _product) return;
        _product = product;
        Refresh();
        NewNow();
    }

    /// <summary>Shows the fields the handler type has and the label of its main field.</summary>
    private void ShowFieldsOfType()
    {
        string type = FieldType;
        bool claude = _product == HookProduct.ClaudeCode;
        string main = MainField(type);
        _mainLabel.Text = Loc.Get(main switch { "url" => K.HooksFieldUrl, "prompt" => K.HooksFieldPrompt, _ => K.HooksFieldCommand });
        AutomationProperties.SetName(_main, _mainLabel.Text);
        foreach (var c in new Control[] { _mainLabel, _main }) c.IsVisible = main.Length > 0;
        foreach (var c in new Control[] { _serverLabel, _server, _toolLabel, _tool }) c.IsVisible = type == "mcp_tool";
        foreach (var c in new Control[] { _conditionLabel, _condition }) c.IsVisible = claude;
        foreach (var c in new Control[] { _failureLabel, _failureBox }) c.IsVisible = claude && type is "command" or "http";
        ShowMatcherHint();
    }

    private void ShowMatcherHint()
    {
        var ev = Catalog.Find(FieldEvent);
        _matcherHint.Text = ev is null ? "" : ev.MatcherField is null
            ? Loc.Get(K.HooksMatcherNone)
            : Loc.Get(K.HooksMatcherHint, ("field", ev.MatcherField), ("examples", string.Join(", ", ev.MatcherValues.Take(4))));
    }

    /// <summary>The form as a draft; null with the note set when a field cannot be used.</summary>
    private HookDraft? Draft()
    {
        var draft = new HookDraft { Event = FieldEvent, Matcher = FieldMatcher.Trim() };
        string type = FieldType;
        var fields = draft.Fields;
        fields["type"] = HookValue.String(type);
        void Set(string name, string value, bool applies) => fields[name] = applies && value.Trim().Length > 0 ? HookValue.String(value.Trim()) : null;
        Set("command", _main.Text ?? "", type == "command");
        Set("url", _main.Text ?? "", type == "http");
        Set("prompt", _main.Text ?? "", type is "prompt" or "agent");
        Set("server", _server.Text ?? "", type == "mcp_tool");
        Set("tool", _tool.Text ?? "", type == "mcp_tool");
        Set("statusMessage", _statusMessage.Text ?? "", true);
        string timeout = FieldTimeout.Trim();
        if (timeout.Length == 0) fields["timeout"] = null;
        else if (double.TryParse(timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 && !double.IsInfinity(seconds)) fields["timeout"] = HookValue.Number(seconds.ToString(CultureInfo.InvariantCulture));
        else
        {
            SetNote(Loc.Get(K.HooksNoteTimeout), error: true);
            return null;
        }
        if (_product == HookProduct.ClaudeCode)
        {
            Set("if", _condition.Text ?? "", true);
            fields["onFailure"] = type is "command" or "http" && _failureBox.SelectedIndex > 0 ? HookValue.String(_failureBox.SelectedIndex == 1 ? "continue" : "block") : null;
        }
        return draft;
    }

    // ---- actions ----

    /// <summary>Puts the hook of the form into the list: adds it, or changes the selected one. The file changes only after the change is proposed.</summary>
    internal bool ApplyNow()
    {
        if (Draft() is not { } draft) return false;
        var existing = _selected?.Hook;
        var result = HookEdits.Upsert(_text, _product, existing, draft, _host.Limits);
        if (!result.Ok)
        {
            SetNote(EditError(result.Error), error: true);
            return false;
        }
        _text = result.Text;
        Refresh();
        SelectApplied(draft, existing);
        SetNote(Loc.Get(K.HooksNoteApplied));
        return true;
    }

    /// <summary>Selects the hook that was just put in the list again, so the next change goes to it.</summary>
    private void SelectApplied(HookDraft draft, HookRef? existing)
    {
        if (_document is null) return;
        var ev = _document.Events.FirstOrDefault(e => e.Name == draft.Event);
        var group = ev?.Groups.FirstOrDefault(g => g.Matcher == draft.Matcher);
        if (ev is null || group is null || group.Handlers.Count == 0) return;
        var handler = existing is not null && existing.Event == draft.Event && group.Handlers.Any(h => h.Index == existing.Handler) && group.Index == existing.Group ? group.Handlers.First(h => h.Index == existing.Handler) : group.Handlers[^1];
        var target = new HookRef(ev.Name, group.Index, handler.Index);
        foreach (var item in AllItems(_tree.Items.OfType<TreeViewItem>()))
            if (item.Tag is NodeTag { Hook: { } hook } && hook == target) { _tree.SelectedItem = item; return; }
    }

    private static IEnumerable<TreeViewItem> AllItems(IEnumerable<TreeViewItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in AllItems(item.Items.OfType<TreeViewItem>())) yield return child;
        }
    }

    internal bool DeleteNow()
    {
        if (_selected?.Hook is not { } hook) return false;
        var result = HookEdits.Remove(_text, _product, hook, _host.Limits);
        if (!result.Ok)
        {
            SetNote(EditError(result.Error), error: true);
            return false;
        }
        _text = result.Text;
        Refresh();
        NewNow();
        SetNote(Loc.Get(K.HooksNoteDeleted));
        return true;
    }

    private static string EditError(HookEditError error) => Loc.Get(error switch
    {
        HookEditError.Syntax => K.HooksErrSyntax,
        HookEditError.NoEvent => K.HooksErrNoEvent,
        HookEditError.NotObject => K.HooksErrNotObject,
        HookEditError.HooksNotObject => K.HooksErrHooksNotObject,
        HookEditError.EventNotArray => K.HooksErrEventNotArray,
        HookEditError.HandlersNotArray => K.HooksErrHandlersNotArray,
        _ => K.HooksErrNoSuchHook,
    });

    /// <summary>Works out what the hook of the form would receive and what its exit codes would do. Nothing is started.</summary>
    internal string DryRunNow()
    {
        var traits = new HookHandlerTraits(
            OnFailure: _product == HookProduct.ClaudeCode && FieldType is "command" or "http" ? _failureBox.SelectedIndex switch { 1 => "continue", 2 => "block", _ => null } : null);
        if (_selected?.Hook is { } hook && _document?.Find(hook) is { } found && found.Handler.Node.Get("async") is { Kind: DataKind.Boolean, Bool: true })
            traits = traits with { Async = true, AsyncRewake = found.Handler.Node.Get("asyncRewake") is { Kind: DataKind.Boolean, Bool: true } };
        var run = HookDryRunner.Run(Catalog, FieldEvent, FieldMatcher.Trim(), _tested.Text, traits, _host.Limits.RegexTimeout);
        _dry.Text = Report(Catalog, run);
        return _dry.Text;
    }

    internal static string EffectText(HookExitEffect effect) => Loc.Get(effect switch
    {
        HookExitEffect.Success => K.HooksEffectSuccess,
        HookExitEffect.Blocks => K.HooksEffectBlocks,
        HookExitEffect.KeepsWorking => K.HooksEffectKeepsWorking,
        HookExitEffect.StderrToModel => K.HooksEffectStderrToModel,
        HookExitEffect.StderrToUser => K.HooksEffectStderrToUser,
        HookExitEffect.Ignored => K.HooksEffectIgnored,
        HookExitEffect.NotHonored => K.HooksEffectNotHonored,
        HookExitEffect.DebugLog => K.HooksEffectDebugLog,
        HookExitEffect.DisplayOnly => K.HooksEffectDisplayOnly,
        HookExitEffect.NonBlocking => K.HooksEffectNonBlocking,
        HookExitEffect.Denies => K.HooksEffectDenies,
        HookExitEffect.Background => K.HooksEffectBackground,
        _ => K.HooksEffectUndocumented,
    });

    /// <summary>The text of the dry run: the event, whether the matcher fires, the input on stdin, the result of each exit code and where all of it comes from.</summary>
    internal static string Report(HookCatalog catalog, HookDryRun run)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.Get(K.HooksDryNothing));
        if (run.Event is not { } ev)
        {
            sb.AppendLine(Loc.Get(K.HooksDryUnknown, ("event", run.EventName), ("product", ProductName(catalog.Product))));
            return sb.ToString();
        }
        sb.AppendLine(Loc.Get(K.HooksDryEvent, ("event", ev.Name)));
        if (ev.Since is { } since) sb.AppendLine(Loc.Get(K.HooksDrySince, ("product", ProductName(catalog.Product)), ("version", since)));
        if (ev.MatcherField is not { } field) sb.AppendLine(Loc.Get(K.HooksDryNoMatcher));
        else
        {
            sb.AppendLine(run.MatchKind switch
            {
                HookMatchKind.All => Loc.Get(K.HooksDryMatcherAll),
                HookMatchKind.Exact => Loc.Get(K.HooksDryMatcherExact, ("matcher", run.Matcher)),
                _ => Loc.Get(K.HooksDryMatcherRegex, ("matcher", run.Matcher)),
            });
            sb.AppendLine(run.Matches switch
            {
                true => Loc.Get(K.HooksDryRuns, ("field", field), ("value", run.TestedValue)),
                false => Loc.Get(K.HooksDryRunsNot, ("field", field), ("value", run.TestedValue)),
                null => Loc.Get(K.HooksDryMatcherBad),
            });
        }
        sb.AppendLine().AppendLine(Loc.Get(K.HooksDryStdin)).AppendLine(run.StdinJson).AppendLine();
        sb.AppendLine(Loc.Get(K.HooksDryExits));
        foreach (var o in run.Outcomes)
            sb.Append("  ").AppendLine(Loc.Get(o.ExitCode == 1 ? K.HooksDryExitOther : K.HooksDryExit, ("code", o.ExitCode), ("effect", EffectText(o.Effect))));
        if (run.Stdout == HookStdout.ContextText) sb.AppendLine(Loc.Get(K.HooksDryStdoutContext));
        else if (run.Stdout == HookStdout.JsonRequired) sb.AppendLine(Loc.Get(K.HooksDryStdoutJson));
        sb.AppendLine().AppendLine(Loc.Get(K.HooksDrySource, ("product", ProductName(catalog.Product)), ("source", catalog.Source), ("date", catalog.Checked)));
        return sb.ToString();
    }

    /// <summary>Hands the new text to the change review of the tab. Refused when the tab changed meanwhile, or when the list has errors.</summary>
    internal async Task<bool> ProposeNow()
    {
        if (_text == _host.ReadText())
        {
            SetNote(Loc.Get(K.HooksNoteUnchanged));
            return false;
        }
        if (_host.Revision() != _revision)
        {
            SetNote(Loc.Get(K.HooksNoteStale), error: true);
            return false;
        }
        if (HookValidator.Validate(_text, _product, _host.Limits).Any(i => i.Severity == HookSeverity.Error))
        {
            SetNote(Loc.Get(K.HooksNoteErrors), error: true);
            return false;
        }
        _propose.IsEnabled = false;
        bool taken = await _host.Propose(_text);
        if (taken) Reload(_host.ReadText(), _host.Revision());
        else Refresh();
        SetNote(Loc.Get(taken ? K.HooksNoteProposed : K.HooksNoteNotTaken));
        return taken;
    }

    // ---- language ----

    private void OnLanguageChanged()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.HooksTitle);
        _productLabel.Text = Loc.Get(K.HooksProduct);
        _treeLabel.Text = Loc.Get(K.HooksTree);
        _eventLabel.Text = Loc.Get(K.HooksFieldEvent);
        _matcherLabel.Text = Loc.Get(K.HooksFieldMatcher);
        _typeLabel.Text = Loc.Get(K.HooksFieldType);
        _serverLabel.Text = Loc.Get(K.HooksFieldServer);
        _toolLabel.Text = Loc.Get(K.HooksFieldTool);
        _timeoutLabel.Text = Loc.Get(K.HooksFieldTimeout);
        _statusLabel.Text = Loc.Get(K.HooksFieldStatus);
        _conditionLabel.Text = Loc.Get(K.HooksFieldIf);
        _failureLabel.Text = Loc.Get(K.HooksFieldOnFailure);
        _testedLabel.Text = Loc.Get(K.HooksFieldTested);
        _dryLabel.Text = Loc.Get(K.HooksDryLabel);
        _findingsLabel.Text = Loc.Get(K.HooksFindings);
        _new.Content = Loc.Get(K.HooksNew);
        _apply.Content = Loc.Get(K.HooksApply);
        _delete.Content = Loc.Get(K.HooksDelete);
        _dryRun.Content = Loc.Get(K.HooksDryRun);
        _propose.Content = Loc.Get(K.HooksPropose);
        _close.Content = Loc.Get(K.PanelOutputClose);
        AutomationProperties.SetName(_productBox, _productLabel.Text);
        AutomationProperties.SetName(_tree, _treeLabel.Text);
        AutomationProperties.SetName(_eventBox, _eventLabel.Text);
        AutomationProperties.SetName(_matcher, _matcherLabel.Text);
        AutomationProperties.SetName(_typeBox, _typeLabel.Text);
        AutomationProperties.SetName(_server, _serverLabel.Text);
        AutomationProperties.SetName(_tool, _toolLabel.Text);
        AutomationProperties.SetName(_timeout, _timeoutLabel.Text);
        AutomationProperties.SetName(_statusMessage, _statusLabel.Text);
        AutomationProperties.SetName(_condition, _conditionLabel.Text);
        AutomationProperties.SetName(_failureBox, _failureLabel.Text);
        AutomationProperties.SetName(_tested, _testedLabel.Text);
        AutomationProperties.SetName(_dry, _dryLabel.Text);
        AutomationProperties.SetName(_findings, _findingsLabel.Text);
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        var failure = _failureBox.SelectedIndex;
        _loading = true;
        _failureBox.ItemsSource = new[] { Loc.Get(K.HooksFailureUnset), "continue", "block" };
        _failureBox.SelectedIndex = Math.Max(0, failure);
        _loading = false;
        ShowFieldsOfType();
        RefreshTree();
        RefreshFindings();
    }
}
