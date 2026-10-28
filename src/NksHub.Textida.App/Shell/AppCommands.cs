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
        On("nks.file.history", w => w.ShowHistory());
        On("nks.remote.open", w => w.OpenRemote());
        On("nks.remote.connections", w => w.ShowConnections());
        On("nks.remote.upload", w => w.UploadRemote());
        On("nks.remote.cancel", w => w.CancelRemoteTransfer());
        On("nks.remote.check", w => w.CheckRemote());
        On("nks.remote.unsent", w => w.ShowUnsent());
        On("nks.remote.pairCache", w => w.PairCache());
        On("nks.source.open", w => w.OpenFromSource());
        On("nks.remote.proxy", w => w.SetNetworkProxy());
        On("nks.remote.exportConnections", w => w.ExportConnections());
        On("nks.remote.importConnections", w => w.ImportConnections());
        On("nks.file.historyMark", w => w.MarkRevision());
        On("nks.file.historySearch", w => w.SearchHistory());
        Do("nks.app.openSettings", _ => app.OpenSettings());
        On("nks.settings.backup", w => w.BackupConfiguration());
        On("nks.settings.restore", w => w.RestoreConfiguration());
        Do("nks.app.quit", _ => app.Exit());

        // edit
        Do("nks.edit.undo", w => w.Undo());
        Do("nks.edit.redo", w => w.Redo());
        // the clipboard commands of the text editor end when the clipboard is written or read and the text is in, so a macro step waits for them
        void OnAsync(string id, Func<MainWindow, Task<CommandOutcome>> run) =>
            registry.SetHandler(id, (_, _) => app.ActiveWindow is { } w ? new ValueTask<CommandOutcome>(run(w)) : done(CommandOutcome.NotHandled));
        OnAsync("nks.edit.cut", w => w.CutAsync());
        OnAsync("nks.edit.copy", w => w.CopyAsync());
        registry.SetHandler("nks.edit.paste", (_, _) =>
        {
            if (app.ActiveWindow is not { } w) return done(CommandOutcome.NotHandled);
            bool text = w.ActiveTable is null && w.ActiveHex is null && w.Active?.Paged is null && w.ActiveEditor is not null;
            var pasting = w.PasteAsync();
            if (text) return new ValueTask<CommandOutcome>(pasting);
            return done(w.ActiveEditor is null && w.ActiveHex is null && w.Active?.Paged is null ? CommandOutcome.NotHandled : CommandOutcome.Handled);
        });
        foreach (var (id, step, extend) in EditorSurface.StepCommands)
            On(id, w => w.ActiveEditor is { } s && s.Step(step, extend) ? CommandOutcome.Handled : CommandOutcome.NotHandled);
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
        On("nks.edit.pasteAsHtml", w => { _ = w.PasteAsHtmlAsync(); return w.ActiveEditor is { IsReadOnly: false } ? CommandOutcome.Handled : CommandOutcome.NotHandled; });
        On("nks.edit.insertFromFile", w => w.InsertFromFile());
        On("nks.edit.insertIntoLines", w => w.InsertIntoLines(registry.IsRepeating));
        On("nks.edit.sortLines", w => w.SortLines(registry.IsRepeating));
        On("nks.search.selectLinesByRange", w => w.SelectLinesByRange(registry.IsRepeating));
        On("nks.edit.reformatParagraph", w => w.Reformat());
        On("nks.edit.reformatShortLines", w => w.Reformat(shortLines: true));
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
        foreach (var conversion in Core.Conversions.SpecialConversions.All) On(conversion.CommandId, w => w.ConvertSpecial(conversion));
        On("nks.edit.runRecipe", w => w.ShowRecipes(registry.IsRepeating));
        On("nks.edit.runRecipeFiles", w => w.ShowRecipeFiles());
        On("nks.edit.copyAiContext", w => w.ShowAiContext());
        On("nks.edit.fragmentJson", w => w.EditStringAs("json"));
        On("nks.edit.fragmentSql", w => w.EditStringAs("sql"));
        On("nks.edit.fragmentHtml", w => w.EditStringAs("html"));
        On("nks.edit.fragmentWriteBack", w => w.WriteBackFragment());
        On("nks.tools.mcpServer", w => w.ToggleMcpServer());
        On("nks.tools.mcpShareTab", w => w.ShareTabWithMcp());
        On("nks.tools.mcpShareSelection", w => w.ShareSelectionWithMcp());
        On("nks.tools.mcpTools", w => w.ShowMcpTools());
        On("nks.tools.ideBridge", w => w.ToggleIdeBridge());
        On("nks.tools.ideDisconnect", w => w.DisconnectIdeAgent());
        On("nks.tools.mcpServers", w => w.ShowAgentMcpServers());
        On("nks.agent.start", w => w.StartAgent());
        On("nks.agent.panel", w => w.ToggleAgentPanelCommand());
        On("nks.agent.shareTab", w => w.ShareTabWithAgent());
        On("nks.agent.cancel", w => w.CancelAgentTurn());
        On("nks.agent.stop", w => w.StopAgent());
        On("nks.agent.hooks", w => w.ShowHooksEditor());
        On("nks.agent.transcript", w => w.ShowTranscript());
        On("nks.agent.tokens", w => w.ShowTokenEstimate());
        On("nks.tools.shellMenuAdd", w => w.AddShellMenu());
        On("nks.tools.shellAssociate", w => w.ShowShellAssociations());
        On("nks.tools.shellRemove", w => w.RemoveShellIntegration());
        On("nks.macro.record", w => w.RecordMacro());
        On("nks.macro.stop", w => w.StopMacro());
        On("nks.macro.playLast", w => w.PlayLastMacro());
        On("nks.macro.save", w => w.SaveMacro());
        On("nks.macro.run", w => w.ShowMacros());
        On("nks.macro.rename", w => w.ShowMacros(MacroAction.Rename));
        On("nks.macro.delete", w => w.ShowMacros(MacroAction.Delete));
        registry.RecordingRefused += id =>
            app.ActiveWindow?.SetStatusMessage(Loc.Get(K.MacroRefused, ("title", registry.TryGet(id, out var refused) ? Loc.Get(refused.TitleKey) : id)));
        registry.SetHandler("nks.edit.repeatLast",(context, token) => registry.RepeatLastAsync(context, token));
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
            // inside a snippet session Tab walks the stops; no plugin (Emmet) gets it, whatever way this command was started
            if (w.ActiveEditor is { InSnippet: true } session && session.NextSnippetStop()) return done(CommandOutcome.Handled);
            // plugins that registered for Tab answer first; without one the key is handled at once, as it always was
            if (w.Active is { } tab && (app.Plugins.Documents.HasTabProviders || PluginServices.EmmetOnTab(tab))) return OfferTab(w, tab, context);
            return done(w.Tab(outdent: false));
        });
        On("nks.edit.newline", w => w.Newline());
        On("nks.edit.toggleComment", w => w.ToggleComment());
        On("nks.edit.expandSelection", w => w.ExpandSelection());
        Fire("nks.edit.formatJson", (w, t) => app.Plugins.RunJsonAsync(w, t, preview: false));
        Fire("nks.edit.previewFormatJson", (w, t) => app.Plugins.RunJsonAsync(w, t, preview: true));
        Fire("nks.edit.minifyJson", (w, t) => app.Plugins.RunJsonAsync(w, t, "json.minify"));
        Fire("nks.edit.sortJsonKeys", (w, t) => app.Plugins.RunJsonAsync(w, t, "json.sortKeys"));
        Fire("nks.edit.fixJsonCommas", (w, t) => app.Plugins.RunJsonAsync(w, t, "json.fixCommas"));
        Fire("nks.edit.formatSql", (w, t) => app.Plugins.RunSqlAsync(w, t, preview: false));
        Fire("nks.edit.previewFormatSql", (w, t) => app.Plugins.RunSqlAsync(w, t, preview: true));
        Fire("nks.edit.htmlEncode", (w, t) => app.Plugins.RunHtmlEntitiesAsync(w, t, encode: true));
        Fire("nks.edit.htmlDecode", (w, t) => app.Plugins.RunHtmlEntitiesAsync(w, t, encode: false));
        Fire("nks.edit.htmlEncodeAll", (w, t) => app.Plugins.RunHtmlEntitiesAsync(w, t, encode: true, nonAscii: true));
        On("nks.edit.htmlCloseTag", w => w.HtmlCloseTag());
        On("nks.edit.htmlMatchTag", w => w.HtmlMatchTag());
        On("nks.edit.htmlSelectElement", w => w.HtmlSelectElement());
        On("nks.edit.htmlRemoveTag", w => w.HtmlRemoveTag());
        On("nks.edit.htmlStripTags", w => w.HtmlStripTags());
        Do("nks.edit.htmlWrapTag", w => _ = w.HtmlWrapTagAsync());
        Fire("nks.edit.htmlTidy", (w, t) => app.Plugins.RunTidyAsync(w, t));

        // tools
        Fire("nks.tools.statistics", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.stats"));
        Fire("nks.tools.hash", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.hash"));
        Fire("nks.tools.hashCopy", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.hash", ",\"copy\":true"));
        Fire("nks.tools.hashCompare", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.hash", ",\"compare\":true"));
        Fire("nks.tools.insertExpression", (w, t) => app.Plugins.RunTextToolAsync(w, t, "text.expression", ",\"insert\":true"));
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
        On("nks.tools.markdownPreview", w => w.ShowLivePreview(outline: false));
        On("nks.tools.markdownOutline", w => w.ShowLivePreview(outline: true));
        On("nks.tools.markdownFormatTable", w => w.FormatMarkdownTable());
        On("nks.tools.markdownUpdateToc", w => w.UpdateMarkdownToc());
        On("nks.tools.markdownRepairTable", w => w.RepairMarkdownTable());
        On("nks.snippet.next", w => w.ActiveEditor is { } s && s.NextSnippetStop() ? CommandOutcome.Handled : CommandOutcome.NotHandled);
        On("nks.snippet.previous", w => w.ActiveEditor is { } s && s.PreviousSnippetStop() ? CommandOutcome.Handled : CommandOutcome.NotHandled);
        Surface("nks.snippet.end", s => s.EndSnippet());
        On("nks.markdown.nextCell", w => w.MoveTableCell(forward: true));
        On("nks.markdown.previousCell", w => w.MoveTableCell(forward: false));
        On("nks.markdown.newRow", w => w.AddTableRow());
        Fire("nks.tools.showOutline", (w, t) => app.Plugins.IndexSymbolsAsync(w, t, reveal: true));
        Fire("nks.tools.compareKeys", (w, t) => w.CompareKeysAsync(t));
        Fire("nks.tools.emmetPreview", (w, t) => app.Plugins.RunEmmetAsync(w, t, "emmet.preview"));
        Fire("nks.tools.emmetWrap", (w, t) => app.Plugins.RunEmmetAsync(w, t, "emmet.wrap"));
        Fire("nks.git.status", (w, t) => app.Plugins.RunGitAsync(w, t, "git.status"));
        Fire("nks.git.diffIndex", (w, t) => app.Plugins.RunGitAsync(w, t, "git.diffIndex"));
        Fire("nks.git.diffHead", (w, t) => app.Plugins.RunGitAsync(w, t, "git.diffHead"));
        Fire("nks.git.stageHunk", (w, t) => app.Plugins.RunGitAsync(w, t, "git.stageHunk"));
        Fire("nks.git.unstageHunk", (w, t) => app.Plugins.RunGitAsync(w, t, "git.unstageHunk"));
        Fire("nks.git.conflictOurs", (w, t) => app.Plugins.RunGitAsync(w, t, "git.conflictOurs"));
        Fire("nks.git.conflictTheirs", (w, t) => app.Plugins.RunGitAsync(w, t, "git.conflictTheirs"));
        Fire("nks.git.conflictBoth", (w, t) => app.Plugins.RunGitAsync(w, t, "git.conflictBoth"));
        Fire("nks.git.markResolved", (w, t) => app.Plugins.RunGitAsync(w, t, "git.markResolved"));
        Fire("nks.git.conflictDelete", (w, t) => app.Plugins.RunGitAsync(w, t, "git.conflictDelete"));
        Fire("nks.lsp.start", (w, t) => app.Plugins.RunLspAsync(w, t, "lsp.start"));
        Fire("nks.lsp.goToDefinition", (w, t) => app.Plugins.RunLspAsync(w, t, "lsp.definition"));
        Fire("nks.lsp.findReferences", (w, t) => app.Plugins.RunLspAsync(w, t, "lsp.references"));
        Fire("nks.lsp.rename", (w, t) => app.Plugins.RunLspAsync(w, t, "lsp.rename"));
        Fire("nks.lsp.restart", (w, t) => app.Plugins.RunLspAsync(w, t, "lsp.restart"));
        Fire("nks.lsp.stop", (w, t) => app.Plugins.RunLspAsync(w, t, "lsp.stop"));
        Fire("nks.tools.sqlDialect", (w, _) => w.ChooseSqlDialectAsync());

        On("nks.tools.run", w => w.ShowToolMenu());
        On("nks.tools.stop", w => w.StopTool());
        Do("nks.tools.logPattern", w => w.ShowLogPatternTester());
        Do("nks.tools.edit", w => w.ShowToolEditor());
        On("nks.file.saveActions", w => w.ShowSaveActions());
        On("nks.file.configFiles", w => w.ShowConfigFiles());
        On("nks.tools.makeTargets", w => w.ShowMakeTargets());
        On("nks.tools.makeRepeat", w => w.RepeatMakeTarget());
        On("nks.packages.lookup", w => w.PackageLookup(refresh: false));
        On("nks.packages.refresh", w => w.PackageLookup(refresh: true));
        On("nks.packages.revoke", w => w.PackageRevoke());
        On("nks.schemas.allow", w => w.SchemaAllowDownload());
        On("nks.packages.script", w => w.ShowPackageScripts());
        On("nks.plugins.manage", w => w.ShowPluginManager());
        On("nks.tools.lint", w => w.RunLinter());
        On("nks.log.nextError", w => w.LogStep(forward: true));
        On("nks.log.previousError", w => w.LogStep(forward: false));
        On("nks.file.exportHtml", w => w.ExportHighlighted(MainWindow.ExportForm.Html));
        On("nks.file.exportRtf", w => w.ExportHighlighted(MainWindow.ExportForm.Rtf));
        On("nks.edit.copyFormatted", w => w.CopyHighlighted(htmlSource: false));
        On("nks.edit.copyHtmlSource", w => w.CopyHighlighted(htmlSource: true));
        On("nks.log.levels", w => w.ChooseLogLevels());
        On("nks.log.follow", w => w.ToggleLogFollow());
        On("nks.log.fold", w => w.FoldLog());
        On("nks.log.unfold", w => w.UnfoldLog());
        On("nks.log.openFrame", w => w.OpenStackFrame());
        On("nks.log.showRaw", w => w.ToggleLogRaw());
        On("nks.log.saveView", w => w.SaveLogView());
        On("nks.log.views", w => w.ShowLogViews());
        On("nks.log.nextMatch", w => w.LogViewStep(forward: true));
        On("nks.log.previousMatch", w => w.LogViewStep(forward: false));

        // search
        On("nks.search.find", w => w.ShowFind(replace: false));
        On("nks.search.replace", w => w.ShowFind(replace: true));
        registry.SetHandler("nks.search.findNext", (_, _) => app.ActiveWindow is { } w ? w.FindStepCommandAsync(forward: true) : done(CommandOutcome.NotHandled));
        registry.SetHandler("nks.search.findPrevious", (_, _) => app.ActiveWindow is { } w ? w.FindStepCommandAsync(forward: false) : done(CommandOutcome.NotHandled));
        registry.SetHandler("nks.search.replaceNext", (_, _) => app.ActiveWindow is { } w ? w.ReplaceStepCommandAsync(all: false) : done(CommandOutcome.NotHandled));
        registry.SetHandler("nks.search.replaceAll", (_, _) => app.ActiveWindow is { } w ? w.ReplaceStepCommandAsync(all: true) : done(CommandOutcome.NotHandled));
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
        On("nks.view.toggleSecrets", w => w.Active?.ToggleSecrets() == true ? CommandOutcome.Handled : CommandOutcome.NotHandled);
        Do("nks.view.nextTab", w => w.CycleTab(1));
        Do("nks.view.previousTab", w => w.CycleTab(-1));
        Do("nks.view.toggleDocumentList", w => w.ToggleDocuments());
        On("nks.view.hex", w => w.ToggleHex());
        Do("nks.file.openAsHex", w => _ = w.OpenAsHexDialogAsync());
        On("nks.compare.clipboard", w => { _ = w.CompareWithClipboardAsync(); return CommandOutcome.Handled; });
        On("nks.compare.file", w => { _ = w.CompareWithFileAsync(); return CommandOutcome.Handled; });
        Do("nks.compare.texts", w => w.OpenTextCompare());
        On("nks.review.open", w => w.OpenReview());
        On("nks.review.next", w => w.ReviewStep(next: true));
        On("nks.review.previous", w => w.ReviewStep(next: false));
        On("nks.compare.next", w => w.CompareStep(next: true));
        On("nks.compare.previous", w => w.CompareStep(next: false));
        On("nks.compare.copyToRight", w => w.CompareCopy(Core.Compare.DiffSide.B));
        On("nks.compare.copyToLeft", w => w.CompareCopy(Core.Compare.DiffSide.A));
        On("nks.compare.action1", w => w.CompareAction(0));
        On("nks.compare.action2", w => w.CompareAction(1));
        On("nks.compare.action3", w => w.CompareAction(2));
        On("nks.compare.action4", w => w.CompareAction(3));
        Do("nks.compare.ignoreWhitespace", w => w.ToggleCompareIgnoreWhitespace());
        Do("nks.compare.ignoreCase", w => w.ToggleCompareIgnoreCase());
        On("nks.view.syncScroll", w => w.ToggleSyncScroll());
        On("nks.view.table", w => w.ToggleTable());
        On("nks.table.editRegion", w => { if (w.Active is not { Custom: null }) return CommandOutcome.NotHandled; _ = w.EditSelectionAsTableAsync(); return CommandOutcome.Handled; });
        On("nks.table.dialect", w => { w.ShowTableDialectMenu(); return w.ActiveTable is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
        On("nks.table.insertRow", w => w.TableCommand(t => t.InsertRow()));
        On("nks.table.deleteRow", w => w.TableCommand(t => t.DeleteRows()));
        On("nks.table.insertColumn", w => { if (w.ActiveTable is null) return CommandOutcome.NotHandled; _ = w.InsertTableColumnAsync(); return CommandOutcome.Handled; });
        On("nks.table.deleteColumn", w => w.TableCommand(t => t.DeleteColumns()));
        On("nks.table.filter", w => { if (w.ActiveTable is null) return CommandOutcome.NotHandled; _ = w.FilterTableAsync(); return CommandOutcome.Handled; });
        On("nks.table.clearFilter", w => w.TableCommand(t => t.ApplyFilter("", t.Column)));
        On("nks.table.copyAs", w => { w.ShowTableCopyAsMenu(); return w.ActiveTable is null ? CommandOutcome.NotHandled : CommandOutcome.Handled; });
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
        On("nks.hex.minimap", w => w.ToggleHexMinimap());
        On("nks.hex.minimap.mode", w => w.CycleHexMapMode());
        On("nks.hex.statistics", w => w.ToggleHexStatistics());
        On("nks.hex.template", w => w.ToggleHexTemplate());
        On("nks.hex.template.open", w => w.OpenHexTemplateFile());
        On("nks.hex.compare.file", w => { if (w.Active is not { Hex: not null } tab) return CommandOutcome.NotHandled; _ = w.CompareHexWithFileAsync(tab); return CommandOutcome.Handled; });
        On("nks.hex.compare.saved", w => { if (w.Active is not { Hex: not null } tab) return CommandOutcome.NotHandled; w.CompareHexWithSaved(tab); return CommandOutcome.Handled; });
        On("nks.hex.bookmarks.panel", w => w.ToggleHexBookmarkPanel());
        On("nks.hex.bookmarks.edit", w => { if (w.ActiveHex is not { } hex) return CommandOutcome.NotHandled; _ = w.EditHexBookmarkAsync(hex); return CommandOutcome.Handled; });
        On("nks.hex.bookmarks.export", w => { if (w.ActiveHex is not { } hex) return CommandOutcome.NotHandled; _ = w.ExportHexBookmarksAsync(hex); return CommandOutcome.Handled; });
        On("nks.hex.bookmarks.import", w => { if (w.ActiveHex is not { } hex) return CommandOutcome.NotHandled; _ = w.ImportHexBookmarksAsync(hex); return CommandOutcome.Handled; });
        Do("nks.view.toggleOutput", w => w.ToggleOutput());
        On("nks.view.pluginPanels", w => w.ShowPluginPanels());
        Do("nks.view.fullScreen", w => w.ToggleFullScreen());
        On("nks.view.nextPane", w => w.FocusNextPane(forward: true));
        On("nks.view.previousPane", w => w.FocusNextPane(forward: false));

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
        On("nks.help.updateCheck", w => w.CheckForUpdates());
        On("nks.help.updateReturn", w => w.ReturnToPreviousVersion());
        Do("nks.help.about", w => _ = app.Dialogs.ShowAboutAsync(w));
        Do("nks.help.contents", w => { var topic = w.HelpContextTopic(); w.ClosePalette(); app.OpenHelp(topic); });
        Fire("nks.help.languageHelp", (w, _) => w.ShowLanguageHelpAsync());
    }

    /// <summary>Gives the commands that data plugins name in their clips, templates, converters and recipes a handler that runs that item.</summary>
    public static void BindClips(App app)
    {
        foreach (var (command, pluginId, action) in app.Clips.CommandTargets)
        {
            if (app.Workbench.Commands.HasHandler(command)) continue;
            var id = action.Id;
            var kind = action.Kind;
            app.Workbench.Commands.SetHandler(command, (_, _) => ValueTask.FromResult(app.ActiveWindow is { } w ? w.RunDataCommand(pluginId, id, kind) : CommandOutcome.NotHandled));
        }
    }
}
