using System.Text;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
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

        // The registry goes on without the UI thread after an await, so a Tab that no plugin handled does what it does without plugins
        // (the command itself, then its fallback) right here on the UI thread. If the user left the tab meanwhile, the key is dropped.
        async ValueTask<CommandOutcome> OfferTab(MainWindow w, DocTab tab, IWhenContext context)
        {
            if (await app.Plugins.OfferTabAsync(tab)) return CommandOutcome.Handled;
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!ReferenceEquals(w.Active, tab)) return CommandOutcome.Handled;
                var outcome = w.Tab(outdent: false);
                if (outcome == CommandOutcome.Handled || !registry.TryGet("nks.edit.tab", out var self) || self.FallbackId is not { } fallback) return outcome;
                return await registry.ExecuteAsync(fallback, context);
            });
        }

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
        On("nks.edit.paste", w => { _ = w.PasteAsync(); return w.ActiveEditor is null && w.ActiveHex is null && w.Active?.Paged is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        On("nks.edit.selectAll", w =>
        {
            if (w.ActiveTable is { } table) { table.SelectAll(); return CommandOutcome.Handled; }
            if (w.ActiveHex is { } hex) { hex.SelectAll(); return CommandOutcome.Handled; }
            if (w.Active?.Paged is { } paged) { paged.SelectAll(); return CommandOutcome.Handled; }
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
        On("nks.edit.clipboardHistory", w => w.ShowClipboardHistory());
        On("nks.edit.pasteKeepCaret", w => { _ = w.PasteKeepCaretAsync(); return w.ActiveEditor is { IsReadOnly: false } ? CommandOutcome.Handled : CommandOutcome.NotHandled; });
        On("nks.edit.insertIntoLines", w => w.InsertIntoLines(registry.IsRepeating));
        On("nks.edit.sortLines", w => w.SortLines(registry.IsRepeating));
        On("nks.search.selectLinesByRange", w => w.SelectLinesByRange(registry.IsRepeating));
        On("nks.edit.reformatParagraph", w => w.Reformat());
        On("nks.edit.justifyLeft", w => w.Justify(Justification.Left));
        On("nks.edit.justifyRight", w => w.Justify(Justification.Right));
        On("nks.edit.justifyCenter", w => w.Justify(Justification.Center));
        On("nks.edit.justifyBlock", w => w.Justify(Justification.Block));
        On("nks.edit.insertDateTime", w => w.InsertDateTime());
        On("nks.edit.insertGuid", w => w.InsertGuid());
        On("nks.edit.insertClip", w => w.ShowClips());
        On("nks.edit.complete", w => w.ShowCompletion());
        On("nks.edit.clipFromSelection", w => w.SaveSelectionAsClip());
        On("nks.clips.editVariables", w => w.EditUserVariables());
        On("nks.file.newFromTemplate", w => w.ShowTemplates());
        On("nks.edit.convert", w => w.ShowConversions(registry.IsRepeating));
        On("nks.edit.runRecipe", w => w.ShowRecipes(registry.IsRepeating));
        On("nks.edit.copyAiContext", w => w.ShowAiContext());
        registry.SetHandler("nks.edit.repeatLast", (context, token) => registry.RepeatLastAsync(context, token));
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
        registry.SetHandler("nks.edit.tab", (context, _) =>
        {
            if (app.ActiveWindow is not { } w) return done(CommandOutcome.NotHandled);
            // plugins that registered for Tab answer first; without one the key is handled at once, as it always was
            if (w.Active is { } tab && (app.Plugins.Documents.HasTabProviders || PluginServices.EmmetSyntax(tab.Doc.Language.Id) is not null)) return OfferTab(w, tab, context);
            return done(w.Tab(outdent: false));
        });
        On("nks.edit.newline", w => w.Newline());
        On("nks.edit.toggleComment", w => w.ToggleComment());
        On("nks.edit.expandSelection", w => w.ExpandSelection());
        Fire("nks.edit.formatJson", (w, t) => app.Plugins.RunJsonAsync(w, t, preview: false));
        Fire("nks.edit.previewFormatJson", (w, t) => app.Plugins.RunJsonAsync(w, t, preview: true));
        Fire("nks.edit.formatSql", (w, t) => app.Plugins.RunSqlAsync(w, t, preview: false));
        Fire("nks.edit.previewFormatSql", (w, t) => app.Plugins.RunSqlAsync(w, t, preview: true));
        Fire("nks.edit.htmlEncode", (w, t) => app.Plugins.RunHtmlEntitiesAsync(w, t, encode: true));
        Fire("nks.edit.htmlDecode", (w, t) => app.Plugins.RunHtmlEntitiesAsync(w, t, encode: false));
        Fire("nks.edit.htmlTidy", (w, t) => app.Plugins.RunTidyAsync(w, t));

        // tools
        Fire("nks.tools.statistics", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.stats"));
        Fire("nks.tools.hash", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.hash"));
        Fire("nks.tools.calculateBlock", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.calculate"));
        Fire("nks.tools.evaluateExpression", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.expression"));
        Fire("nks.tools.convertBase", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.bases"));
        On("nks.tools.pickColor", w =>
        {
            if (w.ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
            if (!surface.PickColorAtCaret()) w.SetStatusMessage(Loc.Get(K.ColorNoneAtCaret));
            return CommandOutcome.Handled;
        });
        Fire("nks.tools.colorToHex", (w, t) => app.Plugins.RunColorAsync(w, t, "hex"));
        Fire("nks.tools.colorToRgb", (w, t) => app.Plugins.RunColorAsync(w, t, "rgb"));
        Fire("nks.tools.colorToHsl", (w, t) => app.Plugins.RunColorAsync(w, t, "hsl"));
        Fire("nks.tools.colorToOklch", (w, t) => app.Plugins.RunColorAsync(w, t, "oklch"));
        Fire("nks.tools.copyKeyPath", (w, t) => app.Plugins.CopyKeyPathAsync(w, t));
        Fire("nks.tools.showKeys", (w, t) => app.Plugins.IndexKeysAsync(w, t, reveal: true));
        Do("nks.tools.goToKey", w => w.ShowQuickOpen("@"));
        Do("nks.tools.goToSymbol", w => w.ShowQuickOpen("@"));
        Fire("nks.tools.showOutline", (w, t) => app.Plugins.IndexSymbolsAsync(w, t, reveal: true));
        Fire("nks.tools.compareKeys", (w, t) => w.CompareKeysAsync(t));
        Fire("nks.tools.emmetPreview", (w, t) => app.Plugins.RunEmmetAsync(w, t, "emmet.preview"));
        Fire("nks.tools.emmetWrap", (w, t) => app.Plugins.RunEmmetAsync(w, t, "emmet.wrap"));
        Fire("nks.tools.sqlDialect", (w, _) => w.ChooseSqlDialectAsync());

        On("nks.tools.run", w => w.ShowToolMenu());
        On("nks.tools.stop", w => w.StopTool());
        Do("nks.tools.logPattern", w => w.ShowLogPatternTester());
        On("nks.log.nextError", w => w.LogStep(forward: true));
        On("nks.log.previousError", w => w.LogStep(forward: false));
        On("nks.log.levels", w => w.ChooseLogLevels());

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
        Do("nks.view.toggleHiddenCharacters", w => w.ToggleHiddenCharacters());
        Do("nks.view.toggleLineNumbers", w => w.ToggleLineNumbers());
        Do("nks.view.toggleCurrentLine", w => w.ToggleCurrentLine());
        Do("nks.view.toggleRightMargin", w => w.ToggleRightMargin());
        Do("nks.view.toggleLinks", w => w.ToggleLinks());
        Do("nks.view.toggleMinimap", w => w.ToggleMinimap());
        Surface("nks.view.toggleSecrets", s => s.ToggleSecrets());
        Do("nks.view.nextTab", w => w.CycleTab(1));
        Do("nks.view.previousTab", w => w.CycleTab(-1));
        Do("nks.view.toggleDocumentList", w => w.ToggleDocuments());
        On("nks.view.hex", w => w.ToggleHex());
        Do("nks.file.openAsHex", w => _ = w.OpenAsHexDialogAsync());
        On("nks.view.table", w => w.ToggleTable());
        On("nks.table.editRegion", w => { if (w.Active is not { Custom: null }) return CommandOutcome.NotHandled; _ = w.EditSelectionAsTableAsync(); return CommandOutcome.Handled; });
        On("nks.table.dialect", w => { w.ShowTableDialectMenu(); return w.ActiveTable is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        On("nks.table.insertRow", w => w.TableCommand(t => t.InsertRow()));
        On("nks.table.deleteRow", w => w.TableCommand(t => t.DeleteRows()));
        On("nks.table.insertColumn", w => { if (w.ActiveTable is null) return CommandOutcome.NotHandled; _ = w.InsertTableColumnAsync(); return CommandOutcome.Handled; });
        On("nks.table.deleteColumn", w => w.TableCommand(t => t.DeleteColumns()));
        On("nks.table.filter", w => { if (w.ActiveTable is null) return CommandOutcome.NotHandled; _ = w.FilterTableAsync(); return CommandOutcome.Handled; });
        On("nks.table.clearFilter", w => w.TableCommand(t => t.ApplyFilter("", t.Column)));
        On("nks.hex.toggleInsert", w => w.HexCommand(h => h.ToggleInsert()));
        On("nks.hex.inspector", w => w.ToggleInspector());
        On("nks.hex.charset", w => { w.ShowHexCharsetMenu(); return w.ActiveHex is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        On("nks.hex.layout", w => { w.ShowHexLayoutMenu(); return w.ActiveHex is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        foreach (int size in HexView.Sizes) On("nks.hex.bytesPerRow" + size, w => w.HexCommand(h => h.SetBytesPerRow(size)));
        On("nks.hex.offsetFormat", w => w.HexCommand(h => h.ToggleOffsetFormat()));
        On("nks.hex.inspectChanges", w => { if (w.Active is not { Hex: not null } tab) return CommandOutcome.NotHandled; _ = w.ShowHexChangesAsync(tab); return CommandOutcome.Handled; });
        On("nks.hex.copyAs", w => w.HexToolMenu(w.HexCopyAsItems));
        On("nks.hex.pasteAs", w => w.HexToolMenu(w.HexPasteAsItems));
        On("nks.hex.exportSelection", w => w.HexToolMenu(w.HexExportItems));
        On("nks.hex.fill", w => w.HexToolMenu(w.HexFillItems));
        On("nks.hex.operate", w => w.HexToolMenu(w.HexOperateItems));
        On("nks.hex.checksum", w => w.HexChecksumMenu());
        On("nks.hex.bookmarks.panel", w => w.ToggleHexBookmarkPanel());
        On("nks.hex.bookmarks.edit", w => { if (w.ActiveHex is not { } hex) return CommandOutcome.NotHandled; _ = w.EditHexBookmarkAsync(hex); return CommandOutcome.Handled; });
        On("nks.hex.bookmarks.export", w => { if (w.ActiveHex is not { } hex) return CommandOutcome.NotHandled; _ = w.ExportHexBookmarksAsync(hex); return CommandOutcome.Handled; });
        On("nks.hex.bookmarks.import", w => { if (w.ActiveHex is not { } hex) return CommandOutcome.NotHandled; _ = w.ImportHexBookmarksAsync(hex); return CommandOutcome.Handled; });
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
        Fire("nks.format.applyEditorConfig", (w, t) => w.ApplyEditorConfigFormatAsync(t));
        Do("nks.file.convertEncodingBatch", w => _ = w.ConvertFilesInFolderAsync());
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
        Fire("nks.help.languageHelp", (w, _) => w.ShowLanguageHelpAsync());
    }

    /// <summary>Gives the commands that data plugins name in their clips, templates, converters and recipes a handler that runs that item.</summary>
    public static void BindClips(App app)
    {
        foreach (var (command, pluginId, action) in app.Clips.CommandTargets)
        {
            var id = action.Id;
            var kind = action.Kind;
            app.Workbench.Commands.SetHandler(command, (_, _) => ValueTask.FromResult(app.ActiveWindow is { } w ? w.RunDataCommand(pluginId, id, kind) : CommandOutcome.NotHandled));
        }
    }
}
