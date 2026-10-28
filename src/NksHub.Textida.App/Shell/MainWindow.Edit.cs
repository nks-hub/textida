using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Input;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using WbKeyModifiers = NksHub.Textida.Workbench.Keys.KeyModifiers;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    internal EditorSurface? ActiveEditor => Active is { TableShown: false } tab ? tab.Surface : null;

    // ---- keys ----

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        // while an input method composes, Esc, Enter and the arrows confirm or cancel the composition; shortcuts with Ctrl, Alt or ⌘ still run
        if ((ActiveEditor is { IsComposing: true } || Active?.Paged is { IsComposing: true }) && (e.KeyModifiers & ~Avalonia.Input.KeyModifiers.Shift) == Avalonia.Input.KeyModifiers.None) return;
        if (e.Key == Key.Escape)
        {
            if (!IsPaletteOpen && (CancelLogSearch() || CancelExport())) { e.Handled = true; return; }
            if (IsPaletteOpen) { ClosePalette(); e.Handled = true; return; }
            if (CloseLeftPanel()) { e.Handled = true; return; }
            if (_find.IsVisible) { HideFind(); e.Handled = true; return; }
        }
        if (!KeyMapping.TryChord(e.Key, e.KeyModifiers, out var chord)) return;
        // AltGr arrives as Ctrl+Alt on Windows and types a character: that is text, not a command
        if (chord.Modifiers.HasFlag(WbKeyModifiers.Ctrl) && chord.Modifiers.HasFlag(WbKeyModifiers.Alt) && !string.IsNullOrEmpty(e.KeySymbol)) return;
        var pending = _app.Workbench.Dispatcher.HandleAsync(chord, new ShellContext(this, forMenu: false));
        if (!pending.IsCompletedSuccessfully) { e.Handled = true; return; }
        e.Handled = pending.Result.Kind is KeyDispatchKind.Executed or KeyDispatchKind.Disabled or KeyDispatchKind.Pending or KeyDispatchKind.Cancelled;
    }

    // ---- menus ----

    private List<MenuGroup> BuildMenuModel()
    {
        var wb = _app.Workbench;
        var groups = MenuBuilder.Build(wb, new ShellContext(this, forMenu: true), CheckState, id => _ = wb.Commands.ExecuteAsync(id, new ShellContext(this, forMenu: true)));
        if (_look.NativeMenu)
        {
            if (_nativeMenu is null) NativeMenu.SetMenu(this, _nativeMenu = new NativeMenu());
            MenuRenderer.FillNative(_nativeMenu, groups);
        }
        else
        {
            var menu = MenuRenderer.ToMenu(groups);
            AutomationProperties.SetName(menu, Loc.Get(K.A11yMainMenu));
            _menuHost.Content = menu;
        }
        return groups;
    }

    private (bool On, bool Radio)? CheckState(string commandId) => commandId switch
    {
        "nks.settings.profile.default" => (_app.Workbench.Profile == BehaviourProfiles.Default, true),
        "nks.settings.profile.pspad" => (_app.Workbench.Profile == BehaviourProfiles.Pspad, true),
        "nks.settings.theme.system" => (_app.Settings.Theme == "system", true),
        "nks.settings.theme.light" => (_app.Settings.Theme == "light", true),
        "nks.settings.theme.dark" => (_app.Settings.Theme == "dark", true),
        _ when commandId.StartsWith(AppCommands.LanguageCommandPrefix, StringComparison.Ordinal) => (Loc.Language == commandId[AppCommands.LanguageCommandPrefix.Length..], true),
        "nks.view.toggleWordWrap" => (Active?.Surface?.WordWrap ?? Options.WordWrap, false),
        "nks.view.toggleHiddenCharacters" => (HiddenCharactersShown, false),
        "nks.view.toggleLineNumbers" => (Options.LineNumbers, false),
        "nks.view.toggleCurrentLine" => (Options.CurrentLine, false),
        "nks.view.toggleRightMargin" => (Options.RightMargin > 0, false),
        "nks.view.toggleLinks" => (Options.Links, false),
        "nks.view.toggleMinimap" => (Options.Minimap, false),
        "nks.view.toggleSecrets" => (Active?.SecretsRevealed ?? false, false),
        "nks.view.syncScroll" => (SyncScrollOn, false),
        "nks.compare.ignoreWhitespace" => (CompareIgnore.HasFlag(Core.Compare.DiffIgnore.Whitespace), false),
        "nks.compare.ignoreCase" => (CompareIgnore.HasFlag(Core.Compare.DiffIgnore.Case), false),
        "nks.view.toggleDocumentList" => (DocumentsPanelOpen, false),
        "nks.view.toggleFilePanel" => (FilePanelOpen, false),
        "nks.view.toggleOutput" => (OutputPanelOpen, false),
        "nks.view.fullScreen" => (IsFullScreen, false),
        _ => null,
    };

    // ---- undo and redo ----

    public void Undo() => UndoRedo(redo: false);
    public void Redo() => UndoRedo(redo: true);

    private static int ToTextOffset(Document doc, TextDocument text, long byteOffset) =>
        doc.TryGetPosition(byteOffset, out var p) ? text.GetOffset((int)p.Line + 1, (int)p.Column + 1) : -1;

    /// <summary>Undo or redo through the document, so the typed run, a line command and a conversion are each one step; then the caret goes where the change happened.</summary>
    private void UndoRedo(bool redo)
    {
        if (ActiveTable is { } table)
        {
            table.UndoRedo(redo);
            return;
        }
        if (Active?.Hex is { } hex)
        {
            hex.UndoRedo(redo);
            return;
        }
        if (Active?.Paged is { } paged)
        {
            paged.UndoRedo(redo);
            return;
        }
        if (Active is not { Surface: { } surface } tab || (tab.Doc.IsVirtual && tab.Doc.ReadOnly)) return;
        surface.EndSnippet(); // the step undone may be the snippet itself, and then Tab must expand its abbreviation again
        var doc = tab.Doc.Doc;
        var changes = new List<ChangeSet>();
        void Capture(object? s, NksHub.Textida.Core.Documents.DocumentChangedEventArgs e) => changes.Add(e.Change);
        doc.Changed += Capture;
        UndoResult? result;
        try { result = redo ? doc.Redo() : doc.Undo(); }
        finally { doc.Changed -= Capture; }
        if (result is null) return;

        var text = tab.Doc.TextBackend!.TextDocument;
        if (result.Selections.Count > 0)
        {
            var sels = result.Selections.Select(r => (A: ToTextOffset(doc, text, r.Start), B: ToTextOffset(doc, text, r.End))).Where(s => s.A >= 0 && s.B >= 0).Select(s => new Sel(s.A, s.B)).ToList();
            if (sels.Count > 0) { surface.SetSelections(sels); return; }
        }
        if (changes.Count > 0 && changes[^1].Edits.Count > 0)
        {
            var edit = changes[^1].Edits[0];
            int offset = ToTextOffset(doc, text, edit.Start + edit.Inserted);
            if (offset >= 0) surface.SetSelections([Sel.At(offset)]);
        }
    }

    // ---- editing commands ----

    /// <summary>Runs a text command over the selections as one undo step and puts the selections it returns in place.</summary>
    internal CommandOutcome RunEdit(Func<EditorSurface, IReadOnlyList<Sel>, IReadOnlyList<Sel>> command)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var before = surface.GetSelections();
        IReadOnlyList<Sel> after;
        using (surface.Doc.Doc.BeginUndoGroup(ToBytes(surface, before), () => ToBytes(surface, surface.GetSelections())))
        {
            surface.ClearCarets(); // every edit of the command would move all the anchored carets, a cost that grows with the square of their number; `after` puts them back
            after = command(surface, before);
            surface.SetSelections(after);
        }
        return CommandOutcome.Handled;
    }

    /// <summary>Selections as byte ranges of the document, what undo and redo give back to restore them.</summary>
    private static IReadOnlyList<ByteRange> ToBytes(EditorSurface surface, IReadOnlyList<Sel> sels)
    {
        var doc = surface.Doc.Doc;
        var text = surface.Document;
        long ByteOf(int offset)
        {
            var loc = text.GetLocation(Math.Clamp(offset, 0, text.TextLength));
            return doc.TryGetOffset(new LinePosition(loc.Line - 1, loc.Column - 1), out long b) ? b : -1;
        }
        var list = new List<ByteRange>();
        foreach (var s in sels)
        {
            long a = ByteOf(s.Start), z = ByteOf(s.End);
            if (a >= 0 && z >= a) list.Add(new ByteRange(a, z));
        }
        return list;
    }

    internal CommandOutcome Tab(bool outdent)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface || surface.IsBlockSelection) return CommandOutcome.NotHandled;
        var d = surface.Document;
        var sel = surface.GetSelections();
        bool multiLine = sel.Any(s => !s.IsEmpty && d.GetLineByOffset(s.Start).LineNumber != d.GetLineByOffset(s.End).LineNumber);
        if (!outdent && !multiLine) return CommandOutcome.NotHandled; // falls back to nks.edit.insertIndent
        var indent = surface.Settings.Indent;
        return RunEdit((s, sels) => outdent ? LineOperations.Outdent(s.Document, sels, indent) : LineOperations.Indent(s.Document, sels, indent));
    }

    internal CommandOutcome SelectLine()
    {
        if (ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
        var d = surface.Document;
        if (surface.IsMulti)
        {
            // every caret takes its whole line(s), including the line ending
            var lines = surface.GetSelections().Select(s => new Sel(d.GetLineByOffset(s.Start).Offset, d.GetLineByOffset(s.End) is var l ? l.EndOffset + l.DelimiterLength : 0)).ToList();
            surface.SetCarets(Carets.Create(lines, lines.Count - 1));
            return CommandOutcome.Handled;
        }
        var sel = surface.GetSelections()[0];
        var first = d.GetLineByOffset(sel.Start);
        var last = d.GetLineByOffset(sel.End);
        if (!sel.IsEmpty && sel.End == last.Offset && last != first) last = last.PreviousLine;
        int start = first.Offset, end = last.EndOffset + last.DelimiterLength;
        if (sel.Start == start && sel.End == end && last.NextLine is { } next) end = next.EndOffset + next.DelimiterLength;
        surface.SetSelections([new Sel(start, end)]);
        return CommandOutcome.Handled;
    }

    internal CommandOutcome ToggleBlockSelect()
    {
        if (ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
        surface.ToggleBlockMode();
        return CommandOutcome.Handled;
    }

    /// <summary>Enter: continues a comment, otherwise a line break in the line ending of the document with the indentation of the current line.</summary>
    internal CommandOutcome Newline()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface || surface.IsBlockSelection) return CommandOutcome.NotHandled;
        var od = surface.Doc;
        string newLine = od.NewLine(surface.Settings.ConfiguredEol);
        surface.InsertLineBreaks(newLine, surface.Settings.ContinueComments ? od.Language.Comments : null);
        return CommandOutcome.Handled;
    }

    internal Task<CommandOutcome> CopyAsync() => CopyOrCutAsync(cut: false);

    internal Task<CommandOutcome> CutAsync() => CopyOrCutAsync(cut: true);

    private Task<CommandOutcome> CopyOrCutAsync(bool cut) =>
        ActiveTable is { } table ? Task.FromResult(TableCopy(table, cut)) : ActiveHex is not null ? Task.FromResult(HexCopy(cut)) : PagedCopy(cut) is { } paged ? Task.FromResult(paged) : CopyOrCutTextAsync(cut);

    private CommandOutcome? PagedCopy(bool cut)
    {
        if (Active?.Paged is not { } paged) return null;
        _ = paged.CopyAsync(cut);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Copy and cut: with several carets the selected texts go to the clipboard one per line. One selection gets the line endings of the platform, as the
    /// editor control gives them. The task ends when the clipboard has the text, so the next step of a macro can paste it.
    /// </summary>
    private async Task<CommandOutcome> CopyOrCutTextAsync(bool cut)
    {
        if (ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
        if (surface.CopyHidden(cut)) return CommandOutcome.Handled;
        string? text = surface.IsMulti ? surface.SelectedTexts(surface.Doc.NewLine(surface.Settings.ConfiguredEol))
            : surface.HasSelection ? TextUtilities.NormalizeNewLines(surface.SelectedText, Environment.NewLine) : null;
        if (text is null || Clipboard is not { } clipboard) return CommandOutcome.Handled;
        RememberCopy(text);
        var written = clipboard.SetTextAsync(text);
        if (cut && !surface.IsReadOnly)
        {
            if (surface.IsMulti) surface.DeleteSelected();
            else
            {
                surface.TextArea.Selection.ReplaceSelectionWithText("");
                surface.TextArea.Caret.BringCaretToView();
            }
        }
        await written;
        return CommandOutcome.Handled;
    }

    internal async Task<CommandOutcome> PasteAsync()
    {
        if (ActiveTable is { } table) return await TablePasteAsync(table);
        if (ActiveHex is not null) return HexPaste();
        if (Active?.Paged is { } paged)
        {
            await paged.PasteAsync();
            return CommandOutcome.Handled;
        }
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text)) return CommandOutcome.Handled;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) PasteText(surface, text);
        else await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => PasteText(surface, text));
        return CommandOutcome.Handled;
    }

    /// <summary>Paste: line endings follow the target document, and a block selection takes one clipboard line per row.</summary>
    internal void PasteText(EditorSurface surface, string text)
    {
        string newLine = surface.Doc.NewLine(surface.Settings.ConfiguredEol);
        text = TextUtilities.NormalizeNewLines(text, newLine);
        if (surface.IsMulti && !surface.IsBlockSelection)
        {
            surface.PasteAtCarets(text);
            return;
        }
        if (!surface.IsBlockSelection) text = surface.EscapeForPaste(text);
        var doc = surface.Doc.Doc;
        using (doc.BeginUndoGroup())
        {
            if (surface.IsBlockSelection)
            {
                surface.SetSelections(ColumnEdit.Paste(surface.Document, surface.GetSelections(), text, newLine));
                surface.TextArea.ClearSelection();
                surface.ToggleBlockModeOff();
            }
            else
            {
                var sel = surface.GetSelections()[0];
                surface.Document.Replace(sel.Start, sel.Length, text);
                surface.SetSelections([Sel.At(sel.Start + text.Length)]);
            }
        }
    }

    internal async Task GoToLineAsync()
    {
        if (Active is not { } tab) return;
        if (tab.Hex is { } hex)
        {
            await GoToOffsetAsync(hex);
            return;
        }
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.GoToLineTitle), Loc.Get(K.GoToLineLabel), "");
        if (answer is null) return;
        GoTo(answer);
    }

    // ---- view ----

    internal void Zoom(int delta)
    {
        if (Active?.Custom is ImageView image)
        {
            image.ZoomBy(delta);
            return;
        }
        var settings = _app.Workbench.Settings;
        double size = settings.GetNumber("editor.fontSize");
        _app.Workbench.Set("editor.fontSize", System.Text.Json.Nodes.JsonValue.Create((int)Math.Clamp(Math.Round(size) + delta, 6, MaxFontSize(Math.Round(size)))));
    }

    /// <summary>The largest font size that still leaves a line in the active view; never below the current size, so a small view does not force the size down.</summary>
    private double MaxFontSize(double current)
    {
        double height = Active?.Root.Bounds.Height ?? 0;
        return Math.Max(6, height > 0 ? Math.Max(current, Math.Floor(height / 1.6)) : 1000);
    }

    internal void ZoomReset()
    {
        if (Active?.Custom is ImageView image) image.Fit();
        else _app.Workbench.Set("editor.fontSize", (System.Text.Json.Nodes.JsonNode?)null);
    }

    /// <summary>Switches to the opposite of what the menu shows; when a giant line holds the wrap back, the setting stays on and the status bar says why.</summary>
    internal void ToggleWordWrap()
    {
        var surface = Active?.Surface;
        bool wanted = !(surface?.WordWrap ?? Options.WordWrap);
        if (wanted != Options.WordWrap) _app.Workbench.Set("editor.wordWrap", wanted ? "on" : "off");
        else surface?.RefreshWrap();
        if (wanted && surface is { WrapSuppressed: true }) SetStatusMessage(Loc.Get(K.ViewWrapSuppressed, ("limit", EditorSurface.WrapLineLimit)));
    }

    internal void CycleTab(int step)
    {
        var tabs = _activeGroup.Tabs;
        if (tabs.Count == 0) return;
        int at = _activeGroup.Active is { } a ? tabs.ToList().IndexOf(a) : 0;
        SelectTab(tabs[(at + step + tabs.Count) % tabs.Count]);
    }
}
