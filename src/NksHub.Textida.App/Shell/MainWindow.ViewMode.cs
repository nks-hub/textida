using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The view modes of a document (P3.6.11, P9.3.4) and the one switcher bar that changes them: a picture file is shown as a picture, as text or as hex; a Markdown file as reading, editing or source. The mode
/// belongs to the document, so all its tabs change together, and the window remembers it for the file until it closes. Changing it builds the views again over the same document, as the hex switch does,
/// and carries the caret and the place on the page over.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<string, ViewMode> _rememberedModes = new(StringComparer.FromComparison(PathComparison));

    // ---- what a document offers ----

    /// <summary>The first bytes of a file, enough to tell a picture; empty when the file cannot be read.</summary>
    private static byte[] ReadHead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[64];
            return head[..stream.Read(head, 0, head.Length)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>
    /// How a file is shown when it is opened from disk: what the window remembers for it, else a picture by <c>files.imageOpen</c>, a Markdown file by <c>markdown.defaultMode</c>, anything else as text.
    /// <paramref name="image"/> tells that the file starts like a picture.
    /// </summary>
    private ViewMode FirstMode(string path, out bool image)
    {
        image = RasterImage.Detect(ReadHead(path)) is not null;
        var settings = _app.Workbench.Settings;
        ViewMode wanted;
        if (image) wanted = settings.GetString("files.imageOpen") switch { "text" => ViewMode.Text, "hex" => ViewMode.Hex, _ => ViewMode.Image };
        else if (LanguageModes.ForPath(path).Id == "markdown") wanted = settings.GetString("markdown.defaultMode") switch { "edit" => ViewMode.Edit, "source" => ViewMode.Text, _ => ViewMode.Read };
        else return ViewMode.Text;
        return _rememberedModes.TryGetValue(path, out var kept) ? kept : wanted;
    }

    private static bool OffersMarkdownModes(OpenDocument od) => od.Language.Id == "markdown" && !od.IsResults && !od.IsVirtual && !od.IsLarge;

    /// <summary>The modes the switcher bar of a document offers, in order; none for a document without modes.</summary>
    internal static IReadOnlyList<ViewMode> ModesOf(OpenDocument od) =>
        od.IsResults || od.IsVirtual ? [] : od.IsImageFile ? [ViewMode.Image, ViewMode.Text, ViewMode.Hex] : OffersMarkdownModes(od) ? [ViewMode.Read, ViewMode.Edit, ViewMode.Text] : [];

    /// <summary>The page is drawn only for text that is within <see cref="PluginLimitsPlaceholders.PreviewLiveMaxChars"/>.</summary>
    private static bool PreviewFits(OpenDocument od) =>
        (od.TextBackend is { } text ? text.TextDocument.TextLength : od.Doc.ByteLength) <= PluginLimitsPlaceholders.PreviewLiveMaxChars;

    internal static bool CanShow(OpenDocument od, ViewMode mode) => mode switch
    {
        ViewMode.Image => od.IsImageFile,
        ViewMode.Read or ViewMode.Edit => OffersMarkdownModes(od) && PreviewFits(od),
        _ => true,
    };

    /// <summary>The name of a mode for the bar of the document, the status and the screen reader.</summary>
    internal static string ModeName(OpenDocument od, ViewMode mode) => Loc.Get(mode switch
    {
        ViewMode.Read => K.ViewModeRead,
        ViewMode.Edit => K.ViewModeEdit,
        ViewMode.Hex => K.ViewModeHex,
        ViewMode.Image => K.ViewModeImage,
        _ => od.IsImageFile ? K.ViewModeText : K.ViewModeSource,
    });

    private static string ButtonLabel(ViewMode mode) => Loc.Get(mode switch { ViewMode.Image => K.ViewModeShowImage, ViewMode.Hex => K.ViewModeShowHex, _ => K.ViewModeShowText });

    // ---- the bar ----

    private ViewModeBar? BuildModeBar(DocTab tab)
    {
        var od = tab.Doc;
        if (tab.Custom is not (null or ImageView or MarkdownPreview) || ModesOf(od) is not { Count: > 0 } modes) return null;
        bool picture = od.IsImageFile;
        var items = modes.Select(m => (m, picture ? ButtonLabel(m) : ModeName(od, m))).ToList();
        return new ViewModeBar(Loc.Get(K.ViewModeBar, ("mode", ModeName(od, od.Mode))), items, od.Mode, mode => _ = ChooseModeAsync(tab, mode, fromBar: true), picture ? "view.image" : "view.markdownModes");
    }

    /// <summary>The status line of a picture (size in pixels, format, size of the file, zoom) and of the page of a Markdown file; the mode stands at the right.</summary>
    private void ShowViewerStatus(DocTab tab)
    {
        string text = tab.Custom is ImageView image ? image.StatusText : Loc.Get(K.StatusReady);
        _statusLeft.Text = _message.Length == 0 ? text : tab.Custom is ImageView ? text + " · " + _message : _message;
        _statusRight.Children.Add(new TextBlock { Text = ModeName(tab.Doc, tab.Doc.Mode), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
    }

    private void WireImage(DocTab tab, ImageView image)
    {
        image.StateChanged += () => { if (tab == Active) UpdateStatus(); };
        image.Announced += text => { if (tab == Active) Announce(text); };
    }

    private void RefreshModeBar(DocTab tab) => tab.ShowModeBar(BuildModeBar(tab));

    private void RefreshModeBars(OpenDocument od)
    {
        foreach (var view in od.Views) RefreshModeBar(view);
    }

    /// <summary>The language of a document changed: a file that is Markdown now gets the bar, one that no longer is loses it and its page.</summary>
    private void OnLanguageChangedForModes(OpenDocument od)
    {
        if (od.Mode is ViewMode.Read or ViewMode.Edit && !OffersMarkdownModes(od)) SetViewMode(od, ViewMode.Text);
        else RefreshModeBars(od);
    }

    /// <summary>The texts of the bars, the pictures and the pages follow the interface language.</summary>
    private void ApplyModeTexts()
    {
        foreach (var od in _docs) RefreshModeBars(od);
        foreach (var tab in Tabs)
        {
            (tab.Custom as ImageView)?.Retranslate();
            (tab.Custom as MarkdownPreview ?? tab.SidePreview)?.Retranslate();
        }
    }

    /// <summary>What the pages of the Markdown previews are drawn with: the font of code, links that ask first, the pictures of this window, the folder of the document.</summary>
    internal PreviewRenderer.Ctx PreviewContext(OpenDocument doc, List<PreviewRenderer.Anchor>? anchors)
    {
        string? folder = doc.Doc.FilePath is { Length: > 0 } path && Path.IsPathRooted(path) ? Path.GetDirectoryName(path) : null;
        return new(new FontFamily(_app.Workbench.Settings.GetString("editor.fontFamily")), uri => _ = OpenPreviewLinkAsync(uri), PreviewPictures, folder, null, anchors);
    }

    /// <summary>A host was allowed for pictures: the pages in the tabs are drawn again.</summary>
    private void RefreshTabPreviews()
    {
        foreach (var tab in Tabs) (tab.Custom as MarkdownPreview ?? tab.SidePreview)?.Refresh(force: true);
    }

    // ---- changing the mode ----

    /// <summary>Changes the mode of a document from the bar, a command or the file the user opened. Hex asks about text the encoding cannot hold first, like the hex switch does.</summary>
    internal async Task<bool> ChooseModeAsync(DocTab tab, ViewMode mode, bool fromBar = false)
    {
        var od = tab.Doc;
        if (od.Mode == mode) return true;
        if (!CanShow(od, mode))
        {
            SetStatusMessage(Loc.Get(K.ViewModeUnavailable, ("limit", PluginLimitsPlaceholders.PreviewLiveMaxChars)));
            return false;
        }
        if (mode == ViewMode.Hex && !await ConfirmTypedLossAsync(tab, Loc.Get(K.LossHexShow))) return false;
        bool changed = SetViewMode(od, mode);
        // the user who pressed a button of the bar stays on the bar, which was built again with the tab
        if (changed && fromBar) Dispatcher.UIThread.Post(() => { if (Active?.Doc == od) Active.ModeBar?.FocusCurrent(); }, DispatcherPriority.Background);
        return changed;
    }

    /// <summary>The command <c>nks.view.cycleMode</c>: the next mode of the bar, after the last the first.</summary>
    internal CommandOutcome CycleMode()
    {
        if (Active is not { } tab || tab.Custom is not (null or ImageView or MarkdownPreview)) return CommandOutcome.NotHandled;
        var modes = ModesOf(tab.Doc).Where(m => CanShow(tab.Doc, m)).ToList();
        if (modes.Count == 0) return CommandOutcome.NotHandled;
        _ = ChooseModeAsync(tab, modes[(modes.IndexOf(tab.Doc.Mode) + 1) % modes.Count]);
        return CommandOutcome.Handled;
    }

    /// <summary>The command that picks one mode of the bar; not handled in a tab whose bar does not offer it.</summary>
    internal CommandOutcome PickMode(ViewMode mode)
    {
        if (Active is not { } tab || tab.Custom is not (null or ImageView or MarkdownPreview) || !ModesOf(tab.Doc).Contains(mode)) return CommandOutcome.NotHandled;
        _ = ChooseModeAsync(tab, mode);
        return CommandOutcome.Handled;
    }

    private static double? TopLineOf(DocTab tab) => tab.Surface is { } surface ? TopLine(surface) : tab.Custom is MarkdownPreview page ? page.TopLine : null;

    private static void ApplyTopLine(DocTab tab, double line)
    {
        if (tab.Surface is { } surface) Dispatcher.UIThread.Post(() => MarkdownPreview.ScrollEditorTo(surface, line), DispatcherPriority.Background);
        else if (tab.Custom is MarkdownPreview page) page.ScrollToLine(line);
    }

    /// <summary>
    /// Shows a document in another mode in every view of it. Nothing is read again and nothing is written: identity, revision, undo and the unsaved state stay, and the caret, the selection and the place
    /// on the page move to the same bytes and lines. Returns false when the mode cannot be shown or the document cannot change (a save is running).
    /// </summary>
    internal bool SetViewMode(OpenDocument od, ViewMode mode, long? offset = null)
    {
        if (od.IsResults || !_docs.Contains(od) || od.Views.Count == 0) return false;
        if (od.Mode == mode)
        {
            if (offset is { } o && od.Views.FirstOrDefault(v => v.Hex is not null) is { } existing) { SelectTab(existing); ApplyOffsets(existing, o, o); }
            return true;
        }
        if (!CanShow(od, mode)) return false;
        if (od.Doc.IsSaving)
        {
            SetStatusMessage(Loc.Get(K.HexBusy));
            return false;
        }
        long t = Core.TypingTrace.Begin();
        var views = od.Views.ToList();
        var from = Active is { } a && a.Doc == od ? a : views[0];
        var (anchor, caret) = CaptureOffsets(from);
        double? top = TopLineOf(from);
        var slots = views.Select(v => (Group: v.Group!, Index: v.Group!.Tabs.ToList().IndexOf(v), Active: v == Active, Source: v == from)).ToList();
        foreach (var v in views)
        {
            v.Group!.Remove(v);
            od.Views.Remove(v);
            v.Close();
            v.ShowBar(null);
        }
        Core.TypingTrace.End("hex.switch.close", t);
        t = Core.TypingTrace.Begin();
        Exception? failure = null;
        try
        {
            od.ChooseMode(mode);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            failure = e;
        }
        Core.TypingTrace.End("hex.switch.enter", t);
        t = Core.TypingTrace.Begin();
        DocTab? activeTab = null, source = null;
        foreach (var slot in slots)
        {
            var fresh = AddView(od, slot.Group, slot.Index);
            if (slot.Active) activeTab = fresh;
            if (slot.Source) source = fresh;
        }
        Core.TypingTrace.End("hex.switch.addViews", t);
        t = Core.TypingTrace.Begin();
        if (failure is null && source is not null)
        {
            ApplyOffsets(source, offset ?? anchor, offset ?? caret);
            if (top is { } line) ApplyTopLine(source, line);
        }
        if (activeTab is not null) SelectTab(activeTab);
        Core.TypingTrace.End("hex.switch.offsets", t);
        t = Core.TypingTrace.Begin();
        od.NotifyChanged();
        Core.TypingTrace.End("hex.switch.notify", t);
        t = Core.TypingTrace.Begin();
        OnDocReplacedRefresh(od);
        Core.TypingTrace.End("hex.switch.refresh", t);
        if (failure is not null) SetStatusMessage(Loc.Get(K.HexSwitchFailed, ("reason", failure.Message)));
        else
        {
            if (od.Path is { } path && (od.IsImageFile || od.Language.Id == "markdown")) _rememberedModes[path] = od.Mode;
            Announce(Loc.Get(K.ViewModeBar, ("mode", ModeName(od, od.Mode))));
        }
        if (activeTab is not null)
        {
            FocusView(activeTab);
            var focusTab = activeTab; // the view has no size and no focus until the first layout
            Dispatcher.UIThread.Post(() => { if (Active == focusTab) FocusView(focusTab); }, DispatcherPriority.Background);
        }
        return failure is null;
    }
}
