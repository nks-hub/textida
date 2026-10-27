using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The hex view of the window (P3.5): switching a document between text and hex in all its views, the status line of a hex tab, go to offset,
/// search for bytes, the save of the byte editor and the offer to open a binary file in hex.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Button _hexModeItem = StatusItem();
    private readonly Button _hexCharsetItem = StatusItem();
    private readonly Button _hexRowItem = StatusItem();
    private readonly TextBlock _hexSize = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _hexItemsWired;
    private CancellationTokenSource? _hexFindCts;
    private BytePattern? _hexPattern;
    private string _hexQuery = "";

    internal HexView? ActiveHex => Active?.Hex;

    /// <summary>A window that was just activated has no focus or has it on the menu bar; the keyboard then goes to the document.</summary>
    private void FocusViewIfLost()
    {
        if (Active is not { } tab || IsPaletteOpen) return;
        if (FocusManager?.GetFocusedElement() is null or Avalonia.Controls.Menu or Avalonia.Controls.MenuItem) FocusView(tab);
    }

    /// <summary>Gives the keyboard to the view of the active tab.</summary>
    internal void FocusActiveView()
    {
        if (Active is { } tab) FocusView(tab);
    }

    /// <summary>Settings of the Binary-offer bar: bytes searched for a NUL, like the rule of the file search.</summary>
    private const int BinarySampleBytes = 8000;

    /// <summary>The accessible names of the hex views follow the UI language.</summary>
    private void ApplyHexTexts()
    {
        foreach (var tab in Tabs)
            if (tab.Hex is { } hex) AutomationProperties.SetName(hex, Loc.Get(K.A11yHexEditor));
    }

    // ---- wiring of a hex tab ----

    private void WireHex(DocTab tab, HexView hex)
    {
        hex.ViewChanged += () => { if (tab == Active) UpdateStatus(); };
        hex.Refused += SetStatusMessage;
        hex.Choose = async (title, message, labels) => await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title, message,
            [.. labels.Select((label, i) => new DialogButton(label, IsDefault: i == 0, IsCancel: i == labels.Count - 1))]));
        AutomationProperties.SetName(hex, Loc.Get(K.A11yHexEditor));
    }

    // ---- switching between text and hex ----

    /// <summary>Position of a view as byte offsets: where the selection starts and where the caret is.</summary>
    private static (long Anchor, long Caret) CaptureOffsets(DocTab tab)
    {
        if (tab.Surface is { } surface && tab.Doc.TextBackend is { } text)
        {
            var sel = surface.GetSelections()[0];
            return (text.ToByteOffset(sel.Anchor), text.ToByteOffset(sel.Caret));
        }
        if (tab.Paged is { } paged) return (paged.CaretOffset, paged.CaretOffset);
        if (tab.Hex is { } hex) return (hex.Editor.Anchor, hex.Editor.Caret);
        return (0, 0);
    }

    /// <summary>Puts a view on byte offsets; in the text view a byte inside a character goes to the start of that character.</summary>
    private static void ApplyOffsets(DocTab tab, long anchor, long caret)
    {
        if (tab.Hex is { } hex)
        {
            hex.Select(anchor, caret);
            hex.CenterIfOutside(caret);
        }
        else if (tab.Surface is { } surface && tab.Doc.TextBackend is { } text)
        {
            int Char(long offset)
            {
                for (int back = 0; back < 4 && offset - back >= 0; back++)
                    if (text.TryToCharOffset(offset - back, out int c)) return c;
                return 0;
            }
            surface.SetSelections([new Sel(Char(anchor), Char(caret))]);
        }
        else if (tab.Paged is { } paged) paged.GoToOffset(caret);
    }

    /// <summary>
    /// Shows a document in hex or as text again, in every view of it. Nothing is read again and nothing is written: identity, revision, undo and the
    /// unsaved state stay, and the caret and the selection move to the same bytes. Returns false when the document cannot change (a save is running).
    /// </summary>
    internal bool SetHex(OpenDocument od, bool hex, long? offset = null)
    {
        if (od.IsResults || !_docs.Contains(od) || od.Views.Count == 0) return false;
        if (od.IsHex == hex)
        {
            if (offset is { } o && od.Views.FirstOrDefault(v => v.Hex is not null) is { } existing) { SelectTab(existing); ApplyOffsets(existing, o, o); }
            return true;
        }
        if (od.Doc.IsSaving)
        {
            SetStatusMessage(Loc.Get(K.HexBusy));
            return false;
        }
        var views = od.Views.ToList();
        var from = Active is { } a && a.Doc == od ? a : views[0];
        var (anchor, caret) = CaptureOffsets(from);
        var slots = views.Select(v => (Group: v.Group!, Index: v.Group!.Tabs.ToList().IndexOf(v), Active: v == Active, Source: v == from)).ToList();
        foreach (var v in views)
        {
            v.Group!.Remove(v);
            od.Views.Remove(v);
            v.Close();
            v.ShowBar(null);
        }
        Exception? failure = null;
        try
        {
            if (hex) od.EnterHex(); else od.LeaveHex();
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            failure = e;
        }
        DocTab? activeTab = null, source = null;
        foreach (var slot in slots)
        {
            var fresh = AddView(od, slot.Group, slot.Index);
            if (slot.Active) activeTab = fresh;
            if (slot.Source) source = fresh;
        }
        if (failure is null && source is not null) ApplyOffsets(source, offset ?? anchor, offset ?? caret);
        if (activeTab is not null) SelectTab(activeTab);
        od.NotifyChanged();
        OnDocReplacedRefresh(od);
        if (failure is not null) SetStatusMessage(Loc.Get(K.HexSwitchFailed, ("reason", failure.Message)));
        if (activeTab is not null)
        {
            FocusView(activeTab);
            var focusTab = activeTab; // the view has no size and no focus until the first layout
            Dispatcher.UIThread.Post(() => { if (Active == focusTab) FocusView(focusTab); }, DispatcherPriority.Background);
        }
        return failure is null;
    }

    /// <summary>The views of a document were rebuilt; settings, autosave and the status follow the new kind of view.</summary>
    private void OnDocReplacedRefresh(OpenDocument od)
    {
        od.Autosave?.Dispose();
        od.Autosave = null;
        ConfigureAutosave(od);
        ApplyLanguageOptions();
        UpdateStatus();
    }

    /// <summary>The command <c>nks.view.hex</c>: the document of the active tab switches between the text view and hex.</summary>
    internal CommandOutcome ToggleHex()
    {
        if (Active is not { Custom: null } tab) return CommandOutcome.NotHandled;
        _ = SwitchHexAsync(tab);
        return CommandOutcome.Handled;
    }

    /// <summary>Switches a tab between text and hex; text the encoding cannot hold opens the loss inspector first, because hex shows the bytes that would be written.</summary>
    internal async Task<bool> SwitchHexAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (!od.IsHex && !await ConfirmTypedLossAsync(tab, Loc.Get(K.LossHexShow))) return false;
        return SetHex(od, !od.IsHex);
    }

    /// <summary>Opens a file in hex, optionally at a byte offset.</summary>
    internal DocTab OpenHex(string path, long? offset)
    {
        var tab = OpenFile(path, null);
        return OpenHex(tab, offset);
    }

    internal DocTab OpenHex(DocTab tab, long? offset)
    {
        tab.ShowBar(null);
        SetHex(tab.Doc, true, offset);
        return tab.Doc.Views.FirstOrDefault(v => v.Hex is not null) ?? tab;
    }

    internal async Task OpenAsHexDialogAsync()
    {
        foreach (var path in await _app.Dialogs.PickFilesAsync(this))
        {
            try { OpenHex(path, null); }
            catch (IOException e) { SetStatusMessage(e.Message); }
        }
    }

    // ---- binary files ----

    internal static bool LooksBinary(Document doc)
    {
        if (doc.Codec.Kind is CodecKind.Utf16 or CodecKind.Utf32) return false;
        var head = new byte[(int)Math.Min(doc.ByteLength, BinarySampleBytes)];
        int n = doc.Read(0, head);
        return head.AsSpan(0, n).Contains((byte)0);
    }

    /// <summary>
    /// A file with a NUL in its first 8 000 bytes; <c>files.binaryAsHex</c> decides: "ask" (default) opens it as text with a bar above it that offers hex,
    /// "always" opens it in hex, "never" leaves it as text. Returns the tab that shows the document afterwards.
    /// </summary>
    private DocTab OfferHex(DocTab tab)
    {
        if (tab.Doc.IsHex || tab.Doc.IsResults || tab.Surface is null && tab.Paged is null || !LooksBinary(tab.Doc.Doc)) return tab;
        var od = tab.Doc;
        string mode = _app.Workbench.Settings.GetString("files.binaryAsHex");
        if (mode == "never") return tab;
        if (mode == "always" && SetHex(od, true)) return od.Views.FirstOrDefault(v => v.Hex is not null) ?? tab;
        tab.ShowBar(new InfoBar(Loc.Get(K.HexBarBinary),
            [(Loc.Get(K.HexBarOpen), () => SetHex(od, true)), (Loc.Get(K.HexBarKeep), () => tab.ShowBar(null))], warning: false, onClose: () => tab.ShowBar(null)));
        return tab;
    }

    // ---- status line ----

    private void WireHexItems()
    {
        if (_hexItemsWired) return;
        _hexItemsWired = true;
        _hexModeItem.Click += (_, _) => ActiveHex?.ToggleInsert();
        _hexCharsetItem.Click += (_, _) => ShowHexCharsetMenu();
        _hexRowItem.Click += (_, _) => ShowHexLayoutMenu();
    }

    private void ShowHexStatus(DocTab tab)
    {
        WireHexItems();
        var hex = tab.Hex!;
        var editor = hex.Editor;
        var doc = tab.Doc.Doc;
        string position = hex.OffsetText(editor.Caret) + $" ({editor.Caret.ToString(CultureInfo.InvariantCulture)})";
        if (editor.HasSelection) position += " · " + Loc.Get(K.HexStatusSelection, ("bytes", editor.Selection.Length));
        else if (editor.Caret < doc.ByteLength)
        {
            var info = hex.Describe(editor.Caret);
            position += $" · {info.Value:X2} = {info.Value}";
        }
        string message = _message.Length > 0 ? _message : doc.IsSaving ? Loc.Get(K.HexSaving) : doc.IsModified ? "" : Loc.Get(K.StatusReady);
        _statusLeft.Text = message.Length > 0 ? position + " · " + message : position;

        _hexModeItem.Content = Loc.Get(hex.Insert ? K.HexModeInsert : K.HexModeOverwrite);
        _hexCharsetItem.Content = hex.Charset.Name;
        _hexRowItem.Content = Loc.Get(K.HexPerRow, ("n", hex.BytesPerRow));
        _hexSize.Text = OpenDocument.FormatSize(doc.ByteLength);
        ToolTip.SetTip(_hexModeItem, Loc.Get(K.HexModeTip));
        ToolTip.SetTip(_hexCharsetItem, Loc.Get(K.HexCharsetTip));
        ToolTip.SetTip(_hexRowItem, Loc.Get(K.HexLayoutTip));
        AutomationProperties.SetName(_hexModeItem, Loc.Get(K.HexModeA11y, ("value", (string)_hexModeItem.Content)));
        AutomationProperties.SetName(_hexCharsetItem, Loc.Get(K.HexCharsetA11y, ("value", hex.Charset.Name)));
        AutomationProperties.SetName(_hexRowItem, Loc.Get(K.HexLayoutA11y, ("value", hex.BytesPerRow)));
        AutomationProperties.SetName(_hexSize, Loc.Get(K.HexSizeA11y, ("value", _hexSize.Text)));
        Control[] items = [_hexModeItem, _hexCharsetItem, _hexRowItem, _hexSize];
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0) _statusRight.Children.Add(StatusDot());
            _statusRight.Children.Add(items[i]);
        }
    }

    /// <summary>Status bar texts of a hex tab, for tests.</summary>
    internal string HexStatusRightText => string.Join(" · ", new[] { _hexModeItem.Content, _hexCharsetItem.Content, _hexRowItem.Content, _hexSize.Text }.Select(o => o?.ToString() ?? ""));

    internal MenuFlyout? ShowHexCharsetMenu()
    {
        if (ActiveHex is not { } hex) return null;
        var flyout = new MenuFlyout();
        foreach (var set in HexCharset.All)
        {
            var chosen = set;
            var item = new MenuItem { Header = set.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = hex.Charset == set, GroupName = "hexcharset" };
            item.Click += (_, _) => hex.SetCharset(chosen);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(_hexCharsetItem);
        return flyout;
    }

    internal MenuFlyout? ShowHexLayoutMenu()
    {
        if (ActiveHex is not { } hex) return null;
        var flyout = new MenuFlyout();
        foreach (int size in HexView.Sizes)
        {
            int chosen = size;
            var item = new MenuItem { Header = Loc.Get(K.HexPerRow, ("n", size)), ToggleType = MenuItemToggleType.Radio, IsChecked = hex.BytesPerRow == size, GroupName = "hexrow" };
            item.Click += (_, _) => hex.SetBytesPerRow(chosen);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        foreach (int group in new[] { 1, 2, 4, 8 })
        {
            int chosen = group;
            var item = new MenuItem { Header = Loc.Get(K.HexGroup, ("n", group)), ToggleType = MenuItemToggleType.Radio, IsChecked = hex.Group == group, GroupName = "hexgroup", IsEnabled = hex.BytesPerRow % group == 0 };
            item.Click += (_, _) => hex.SetGroup(chosen);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var offsets = new MenuItem { Header = Loc.Get(K.HexOffsetDecimal), ToggleType = MenuItemToggleType.CheckBox, IsChecked = hex.DecimalOffsets };
        offsets.Click += (_, _) => hex.ToggleOffsetFormat();
        flyout.Items.Add(offsets);
        flyout.ShowAt(_hexRowItem);
        return flyout;
    }

    // ---- commands of the hex tab ----

    internal CommandOutcome HexCommand(Action<HexView> run)
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        run(hex);
        return CommandOutcome.Handled;
    }

    internal CommandOutcome ToggleInspector()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        var panel = hex.Inspector;
        if (!panel.IsVisible) panel.FocusList();
        else if (panel.IsKeyboardFocusWithin) { panel.IsVisible = false; hex.Focus(); }
        else panel.FocusList();
        UpdateStatus();
        return CommandOutcome.Handled;
    }

    internal CommandOutcome HexCopy(bool cut)
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        _ = hex.CopyAsync(cut);
        return CommandOutcome.Handled;
    }

    internal CommandOutcome HexPaste()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        _ = hex.PasteAsync();
        return CommandOutcome.Handled;
    }

    // ---- go to offset ----

    /// <summary>The target of a go-to expression, or the sentence that says why there is none; the preview and the jump share it.</summary>
    internal static (long? Target, string? Problem) ResolveOffset(HexView hex, string expression)
    {
        var doc = hex.Editor.Document;
        Span<byte> four = stackalloc byte[4];
        uint? value = doc.Read(hex.Caret, four) == 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(four) : null;
        long selection = hex.Editor.HasSelection ? hex.Editor.Selection.Start : hex.Caret;
        try
        {
            long target = OffsetExpression.Evaluate(expression, new OffsetContext(doc.ByteLength, hex.Caret, selection, value));
            if (target < 0 || target > doc.ByteLength || (target == doc.ByteLength && !hex.Insert && doc.ByteLength > 0))
                return (null, Loc.Get(K.HexGotoOutside, ("size", doc.ByteLength)));
            return (target, null);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentOutOfRangeException or DivideByZeroException)
        {
            return (null, Loc.Get(K.HexGotoInvalid, ("reason", e.Message)));
        }
    }

    /// <summary>What Ctrl+G would do with the expression typed so far: the offset it reaches and the byte there, or why it does not.</summary>
    internal static string PreviewOffset(HexView hex, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "";
        var (target, problem) = ResolveOffset(hex, expression);
        if (target is not { } at) return problem!;
        string offset = hex.OffsetText(at), decimalText = at.ToString(CultureInfo.InvariantCulture);
        if (at >= hex.Editor.Document.ByteLength) return Loc.Get(K.HexGotoPreviewEnd, ("offset", offset), ("decimal", decimalText));
        return Loc.Get(K.HexGotoPreview, ("offset", offset), ("decimal", decimalText), ("value", $"{hex.Describe(at).Value:X2}"));
    }

    internal async Task GoToOffsetAsync(HexView hex)
    {
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexGotoTitle), Loc.Get(K.HexGotoLabel), "", text => PreviewOffset(hex, text));
        if (answer is null) return;
        var (target, problem) = ResolveOffset(hex, answer);
        if (target is not { } at)
        {
            SetStatusMessage(problem!);
            return;
        }
        RecordJump();
        hex.MoveTo(at);
        hex.CenterIfOutside(at);
        hex.Focus();
    }

    // ---- search for bytes ----

    private static readonly Regex NumberQuery = new(@"^\S+\s+[iuf](8|16|32|64)(\s+(le|be))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A hex pattern (<c>50 4B ?? 04</c>), text in quotes in the charset of the character column, or a number with a type (<c>0x1F40 i32</c>).</summary>
    internal static BytePattern ParseHexQuery(string query, HexCharset charset)
    {
        string q = query.Trim();
        if (q.Length >= 2 && q[0] == '"' && q[^1] == '"') return BytePattern.FromText(q[1..^1], charset.Codec);
        if (NumberQuery.IsMatch(q)) return BytePattern.ParseNumber(q);
        return BytePattern.ParseHex(q);
    }

    internal async Task FindBytesAsync(HexView hex)
    {
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexFindTitle), Loc.Get(K.HexFindLabel), _hexQuery);
        if (string.IsNullOrWhiteSpace(answer)) return;
        try { _hexPattern = ParseHexQuery(answer, hex.Charset); }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            SetStatusMessage(Loc.Get(K.HexFindInvalid, ("reason", e.Message)));
            return;
        }
        _hexQuery = answer;
        await HexFindStepAsync(hex, forward: true, fromCaret: true);
    }

    /// <summary>The next or previous match of the last query, wrapping around at the ends; the matches are then counted in the background.</summary>
    internal async Task HexFindStepAsync(HexView hex, bool forward, bool fromCaret = false)
    {
        if (_hexPattern is not { } pattern)
        {
            await FindBytesAsync(hex);
            return;
        }
        _hexFindCts?.Cancel();
        var cts = _hexFindCts = new CancellationTokenSource();
        var doc = hex.Editor.Document;
        var snapshot = doc.GetSnapshot();
        long from = hex.Editor.HasSelection && !fromCaret ? (forward ? hex.Editor.Selection.Start + 1 : hex.Editor.Selection.Start) : (forward ? hex.Caret + (fromCaret ? 0 : 1) : hex.Caret);
        SetStatusMessage(Loc.Get(K.HexFindRunning));
        try
        {
            var found = await Task.Run(() => FindFrom(snapshot, pattern, from, forward, cts.Token), CancellationToken.None);
            if (cts.IsCancellationRequested) return;
            if (found is null)
            {
                SetStatusMessage(Loc.Get(K.HexFindNone));
                return;
            }
            RecordJump();
            hex.Select(found.Value.Start, found.Value.End);
            hex.CenterIfOutside(found.Value.Start);
            SetStatusMessage("");
            var total = await Task.Run(() => ByteSearch.Find(snapshot, pattern, new ByteSearchOptions { MaxResults = 1_000_000 }, cts.Token), CancellationToken.None);
            if (cts.IsCancellationRequested) return;
            int index = 0;
            for (int i = 0; i < total.Matches.Count; i++) if (total.Matches[i].Start == found.Value.Start) { index = i + 1; break; }
            SetStatusMessage(total.LimitReached
                ? Loc.Get(K.HexFindLimit, ("n", total.Matches.Count))
                : Loc.Get(K.HexFindCount, ("index", index), ("total", total.Matches.Count)));
        }
        catch (OperationCanceledException) { }
        catch (IOException)
        {
            // the file changed under the snapshot: drop the result and let the document's own check tell the user
            hex.FileChangedBehind();
            SetStatusMessage(Loc.Get(K.HexFindStale));
        }
    }

    private static ByteRange? FindFrom(DocumentSnapshot snapshot, BytePattern pattern, long from, bool forward, CancellationToken ct)
    {
        long length = snapshot.ByteLength;
        from = Math.Clamp(from, 0, length);
        if (forward)
        {
            var tail = ByteSearch.Find(snapshot, pattern, new ByteSearchOptions { MaxResults = 1, Range = new ByteRange(from, length) }, ct);
            if (tail.Matches.Count > 0) return tail.Matches[0];
            var head = ByteSearch.Find(snapshot, pattern, new ByteSearchOptions { MaxResults = 1, Range = new ByteRange(0, Math.Min(length, from + pattern.Length - 1)) }, ct);
            return head.Matches.Count > 0 ? head.Matches[0] : null;
        }
        // backwards: the last match that ends at or before the caret, else the last one in the file
        ByteRange? last = null;
        var before = ByteSearch.Find(snapshot, pattern, new ByteSearchOptions { MaxResults = int.MaxValue, Range = new ByteRange(0, Math.Min(length, from + pattern.Length - 1)) }, ct);
        foreach (var m in before.Matches) if (m.Start < from) last = m;
        if (last is not null) return last;
        var all = ByteSearch.Find(snapshot, pattern, new ByteSearchOptions { MaxResults = int.MaxValue }, ct);
        return all.Matches.Count > 0 ? all.Matches[^1] : null;
    }

    // ---- save ----

    /// <summary>
    /// Saves a hex tab: the bytes are written on a worker from a snapshot, in place when only same-length changes happened and as a new file otherwise.
    /// The window stays usable meanwhile and Esc in the view cancels before the data is committed.
    /// </summary>
    private async Task<bool> SaveHexAsync(DocTab tab, bool overwrite, string? path = null)
    {
        var od = tab.Doc;
        var doc = od.Doc;
        if (doc.IsSaving) return false;
        string? target = path ?? od.Path;
        try
        {
            if (target is not null && File.Exists(target) && new FileInfo(target).Length <= LargeFileBytes)
            {
                var backup = _app.Backups.BackupBeforeSave(target);
                if (backup.Status == BackupStatus.Failed && _app.Backups.Policy.RequireBackup)
                {
                    SetStatusMessage(Loc.Get(K.StatusBackupFailed, ("reason", backup.Problem ?? "")));
                    return false;
                }
            }
            var plan = doc.PlanSave();
            _message = Loc.Get(K.HexSaving);
            UpdateStatus();
            var result = await doc.SaveAsync(path, overwrite);
            _message = "";
            if (result.Saved)
            {
                od.NotifyChanged();
                tab.ShowBar(null);
                SetStatusMessage(path is null && plan.Mode == SaveMode.InPlace ? Loc.Get(K.HexSavedInPlace, ("bytes", plan.ChangedBytes)) : Loc.Get(K.StatusSaved));
                _app.SessionChanged();
                return true;
            }
            UpdateStatus();
            if (result.Conflict is { } change) return await ResolveSaveConflictAsync(tab, change);
            return false;
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(Loc.Get(K.HexSaveCancelled));
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.StatusSaveFailed, ("reason", e.Message)));
            return false;
        }
    }

    // ---- what a save would write ----

    /// <summary>The hex part of the byte inspector: the changed ranges with the old and new bytes and the way the next save writes them.</summary>
    internal async Task ShowHexChangesAsync(DocTab tab)
    {
        var doc = tab.Doc.Doc;
        var plan = doc.PlanSave();
        var report = doc.InspectUnsaved();
        long original = doc.Fingerprint?.Size ?? doc.ByteLength;
        string message = plan.Mode == SaveMode.InPlace
            ? Loc.Get(K.HexChangesInPlace, ("bytes", plan.ChangedBytes), ("ranges", plan.Ranges.Count))
            : Loc.Get(K.HexChangesStream, ("old", original), ("new", doc.ByteLength));
        if (!doc.IsModified) message = Loc.Get(K.HexChangesNone);
        var request = new ChoiceRequest(Loc.Get(K.HexChangesTitle), message,
            [new DialogButton(Loc.Get(K.DialogSave), IsDefault: true), new DialogButton(Loc.Get(K.HexChangesClose), IsCancel: true)], InspectionDetails(doc, report));
        if (await _app.Dialogs.ChooseAsync(this, request) == 0 && doc.IsModified) await SaveAsync(tab);
    }
}
