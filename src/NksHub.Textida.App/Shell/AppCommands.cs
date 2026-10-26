using System.Text;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Attaches the handlers of the built-in commands to the registry. Menus, palette and keys reach the editor only through these;
/// a command without a handler here is not offered anywhere (the document search of P2.4 is the example today).
/// </summary>
internal static class AppCommands
{
    /// <summary>Prefix of the commands that switch the UI language; the language tag follows.</summary>
    public const string LanguageCommandPrefix = "nks.settings.language.";

    public static void Register(App app)
    {
        var registry = app.Workbench.Commands;
        var done = new Func<CommandOutcome, ValueTask<CommandOutcome>>(o => ValueTask.FromResult(o));

        void On(string id, Func<MainWindow, CommandOutcome> run) =>
            registry.SetHandler(id, (_, _) => done(app.ActiveWindow is { } w ? run(w) : CommandOutcome.NotHandled));

        void Do(string id, Action<MainWindow> run) => On(id, w => { run(w); return CommandOutcome.Handled; });

        void Fire(string id, Func<MainWindow, DocTab, Task> run) =>
            On(id, w => { if (w.Active is not { } tab) return CommandOutcome.NotHandled; _ = run(w, tab); return CommandOutcome.Handled; });

        void Surface(string id, Action<EditorSurface> run) =>
            On(id, w => { if (w.ActiveEditor is not { } s) return CommandOutcome.NotHandled; run(s); return CommandOutcome.Handled; });

        void Edit(string id, Func<EditorSurface, IReadOnlyList<Sel>, IReadOnlyList<Sel>> run) =>
            On(id, w => w.RunEdit(run));

        // file
        Do("nks.file.new", w => w.NewDocument());
        Do("nks.file.open", w => _ = w.OpenWithDialogAsync());
        Fire("nks.file.save", (w, t) => w.SaveAsync(t));
        Fire("nks.file.saveAs", (w, t) => w.SaveAsAsync(t));
        Do("nks.file.saveAll", w => _ = w.SaveAllAsync());
        Fire("nks.file.close", (w, t) => w.CloseTabAsync(t));
        Fire("nks.file.reload", (w, t) => w.ReloadAsync(t));
        Do("nks.file.compareWithDisk", w => { if (w.Active is { } t) w.CompareWithDisk(t); });
        Do("nks.app.openSettings", _ => app.OpenSettings());
        Do("nks.app.quit", _ => app.Exit());

        // edit
        Do("nks.edit.undo", w => w.Undo());
        Do("nks.edit.redo", w => w.Redo());
        On("nks.edit.cut", w => w.CutAsync());
        On("nks.edit.copy", w => w.CopyAsync());
        On("nks.edit.paste", w => { _ = w.PasteAsync(); return w.ActiveEditor is null && w.ActiveHex is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        On("nks.edit.selectAll", w =>
        {
            if (w.ActiveHex is { } hex) { hex.SelectAll(); return CommandOutcome.Handled; }
            if (w.ActiveEditor is not { } s) return CommandOutcome.NotHandled;
            s.SelectAll();
            return CommandOutcome.Handled;
        });
        On("nks.edit.selectLine", w => w.SelectLine());
        Surface("nks.edit.addNextOccurrence", s => s.AddNextOccurrence());
        Surface("nks.edit.skipOccurrence", s => s.SkipOccurrence());
        Surface("nks.edit.selectAllOccurrences", s => s.SelectAllOccurrences());
        Surface("nks.edit.addCaretAbove", s => s.AddCaretVertical(-1));
        Surface("nks.edit.addCaretBelow", s => s.AddCaretVertical(1));
        Surface("nks.edit.clearCarets", s => s.ClearCarets());
        Surface("nks.edit.selectToMark", s => s.MarkOrSelectToMark());
        On("nks.edit.toggleBlockSelect", w => w.ToggleBlockSelect());
        Edit("nks.edit.deleteLine", (s, sels) => LineOperations.DeleteLines(s.Document, sels));
        Edit("nks.edit.duplicateLine", (s, sels) => LineOperations.DuplicateLines(s.Document, sels));
        Edit("nks.edit.moveLineUp", (s, sels) => LineOperations.MoveLines(s.Document, sels, -1));
        Edit("nks.edit.moveLineDown", (s, sels) => LineOperations.MoveLines(s.Document, sels, 1));
        Edit("nks.edit.joinLines", (s, sels) => LineOperations.JoinLines(s.Document, sels));
        Edit("nks.edit.upperCase", (s, sels) => LineOperations.ChangeCase(s.Document, sels, upper: true));
        Edit("nks.edit.lowerCase", (s, sels) => LineOperations.ChangeCase(s.Document, sels, upper: false));
        Edit("nks.edit.indent", (s, sels) => LineOperations.Indent(s.Document, sels, s.Settings.Indent));
        Edit("nks.edit.outdent", (s, sels) => LineOperations.Outdent(s.Document, sels, s.Settings.Indent));
        Edit("nks.edit.insertIndent", (s, sels) => LineOperations.InsertIndent(s.Document, sels, s.Settings.Indent));
        On("nks.edit.tab", w => w.Tab(outdent: false));
        On("nks.edit.newline", w => w.Newline());

        // search
        On("nks.search.find", w => w.ShowFind(replace: false));
        On("nks.search.replace", w => w.ShowFind(replace: true));
        Do("nks.search.findNext", w => _ = w.FindStepAsync(forward: true));
        Do("nks.search.findPrevious", w => _ = w.FindStepAsync(forward: false));
        On("nks.search.findWord", w => w.FindWord());
        On("nks.search.findInFiles", w => w.ShowFindInFiles(replace: false));
        On("nks.search.replaceInFiles", w => w.ShowFindInFiles(replace: true));
        Do("nks.search.regexTester", w => w.ShowRegexTester());
        On("nks.search.gotoBracket", w => w.GoToBracket());
        Do("nks.search.gotoLine", w => _ = w.GoToLineAsync());

        // view
        Do("nks.view.commandPalette", w => w.ShowPalette());
        Do("nks.view.toggleWordWrap", w => w.ToggleWordWrap());
        Do("nks.view.zoomIn", w => w.Zoom(1));
        Do("nks.view.zoomOut", w => w.Zoom(-1));
        Do("nks.view.zoomReset", w => w.ZoomReset());
        Do("nks.view.nextTab", w => w.CycleTab(1));
        Do("nks.view.previousTab", w => w.CycleTab(-1));
        Do("nks.view.toggleDocumentList", w => w.ToggleDocuments());
        On("nks.view.hex", w => w.ToggleHex());
        Do("nks.file.openAsHex", w => _ = w.OpenAsHexDialogAsync());
        On("nks.hex.toggleInsert", w => w.HexCommand(h => h.ToggleInsert()));
        On("nks.hex.inspector", w => w.ToggleInspector());
        On("nks.hex.charset", w => { w.ShowHexCharsetMenu(); return w.ActiveHex is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        On("nks.hex.layout", w => { w.ShowHexLayoutMenu(); return w.ActiveHex is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        foreach (int size in HexView.Sizes) On("nks.hex.bytesPerRow" + size, w => w.HexCommand(h => h.SetBytesPerRow(size)));
        On("nks.hex.offsetFormat", w => w.HexCommand(h => h.ToggleOffsetFormat()));
        On("nks.hex.inspectChanges", w => { if (w.Active is not { Hex: not null } tab) return CommandOutcome.NotHandled; _ = w.ShowHexChangesAsync(tab); return CommandOutcome.Handled; });
        Do("nks.view.toggleOutput", w => w.ToggleOutput());
        Do("nks.view.fullScreen", w => w.ToggleFullScreen());

        // format
        Do("nks.format.reread", w => _ = w.ShowEncodingMenu(onlyConvert: false));
        Do("nks.format.convert", w => _ = w.ShowEncodingMenu(onlyConvert: true));
        Fire("nks.format.eolCrLf", (w, t) => w.UnifyEolAsync(t, EolStyle.CrLf));
        Fire("nks.format.eolLf", (w, t) => w.UnifyEolAsync(t, EolStyle.Lf));
        Fire("nks.format.eolCr", (w, t) => w.UnifyEolAsync(t, EolStyle.Cr));
        Fire("nks.format.normalizeNfc", (w, t) => w.NormalizeAsync(t, NormalizationForm.FormC));
        Fire("nks.format.normalizeNfd", (w, t) => w.NormalizeAsync(t, NormalizationForm.FormD));
        Do("nks.format.language", w => _ = w.ShowLanguageMenu());

        // window
        Do("nks.window.new", w => app.NewWindow(w));
        Do("nks.window.splitView", w => w.SplitView());
        Do("nks.window.moveToOtherGroup", w => w.MoveToOtherGroup());

        NavigationCommands.Register(app);

        // settings and help
        Do("nks.settings.profile.default", _ => app.Workbench.Set("editor.profile", BehaviourProfiles.Default));
        Do("nks.settings.profile.pspad", _ => app.Workbench.Set("editor.profile", BehaviourProfiles.Pspad));
        Do("nks.settings.theme.system", _ => app.SetTheme("system"));
        Do("nks.settings.theme.light", _ => app.SetTheme("light"));
        Do("nks.settings.theme.dark", _ => app.SetTheme("dark"));
        foreach (var tag in Loc.Languages) Do(LanguageCommandPrefix + tag, _ => app.SetLanguage(tag));
        Do("nks.help.about", w => _ = app.Dialogs.ShowAboutAsync(w));
        Do("nks.help.contents", w => { var topic = w.HelpContextTopic(); w.ClosePalette(); app.OpenHelp(topic); });
    }
}
