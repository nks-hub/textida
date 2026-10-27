using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Shell;

/// <summary>Projects (named sets of folders), favorites and named sessions. The data is kept by <see cref="WorkspaceStore"/> in the profile.</summary>
internal sealed partial class MainWindow
{
    private readonly List<string> _projectLanguages = [];

    /// <summary>At start: says what in the saved projects and sessions could not be read and puts the settings of the active project in place.</summary>
    internal void StartWorkspace()
    {
        ReportWorkspaceProblems();
        ApplyProjectSettings(_app.Workspace.Active);
    }

    private void ReportWorkspaceProblems()
    {
        if (_app.Workspace.Problems.Count > 0)
            ShowNotice("workspace", Loc.Get(K.WorkspaceDamaged, ("files", string.Join("; ", _app.Workspace.Problems))), warning: true);
    }

    // ---- projects ----

    /// <summary>The folders of the panel become a project with this name; a project of the same name is replaced.</summary>
    internal void SaveProject(string name)
    {
        if (_fileTree is null) { SetStatusMessage(Loc.Get(K.ProjectNeedsFolder)); return; }
        var existing = _app.Workspace.Find(name);
        var project = new ProjectData { Name = existing?.Name ?? name, Folders = [.. _fileTree.Roots] };
        FillTabs(project);
        _app.Workspace.SaveProject(project);
        _app.Workspace.SetActive(project.Name);
        _filesTitle.Text = FilesTitle(_fileTree);
        SetStatusMessage(Loc.Get(K.ProjectSaved, ("name", project.Name)));
    }

    private void FillTabs(ProjectData project)
    {
        bool Inside(string p) => project.Folders.Any(f => PathMove.IsInside(p, f));
        var open = Tabs.Where(t => t.Path is not null && !t.Doc.IsVirtual && !t.Doc.IsResults && t.Doc.Source is null).Select(t => t.Path!).Where(Inside).Distinct(StringComparer.Ordinal).ToList();
        project.Files = open;
        project.ActiveFile = Active?.Path is { } a && open.Contains(a) ? a : null;
    }

    /// <summary>Remembers the tabs of the project this window shows; called before another project replaces it and when the window closes.</summary>
    internal void UpdateActiveProject()
    {
        if (_app.Workspace.Active is not { } project || _fileTree is not { } tree || !project.Folders.SequenceEqual(tree.Roots, StringComparer.OrdinalIgnoreCase)) return;
        FillTabs(project);
        try { _app.Workspace.SaveProject(project); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the tabs of a project are a convenience; closing must not wait on them */ }
    }

    /// <summary>Shows the folders of the project in the panel and opens the files it had open. Folders and files that are gone are named in the status bar.</summary>
    internal async Task<bool> OpenProjectAsync(ProjectData project)
    {
        UpdateActiveProject();
        var folders = project.Folders.Where(Directory.Exists).ToList();
        var missing = project.Folders.Where(f => !Directory.Exists(f)).Select(f => Path.GetFileName(f) is { Length: > 0 } n ? n : f).ToList();
        if (folders.Count == 0) { SetStatusMessage(Loc.Get(K.ProjectNoFolder, ("name", project.Name))); return false; }
        var before = _app.Workspace.Active?.Name;
        _app.Workspace.SetActive(project.Name);
        if (!await ShowTreeAsync(folders[0], folders.Skip(1))) { _app.Workspace.SetActive(before); return false; }
        ApplyProjectSettings(project);
        foreach (var file in project.Files)
        {
            try { if (File.Exists(file)) OpenFile(file, null); else missing.Add(Path.GetFileName(file)); }
            catch (IOException) { missing.Add(Path.GetFileName(file)); }
        }
        if (project.ActiveFile is { } active && Tabs.FirstOrDefault(t => string.Equals(t.Path, active, PathComparison)) is { } tab) SelectTab(tab);
        SetStatusMessage(missing.Count == 0
            ? Loc.Get(K.ProjectOpened, ("name", project.Name))
            : Loc.Get(K.ProjectOpenedMissing, ("name", project.Name), ("missing", string.Join(", ", missing))));
        return true;
    }

    /// <summary>Adds a folder to the panel next to the ones shown; in a project it becomes part of the project.</summary>
    internal async Task AddFolderToPanelAsync(string folder)
    {
        if (_fileTree is null) { await ShowFolderAsync(folder); return; }
        var roots = _fileTree.Roots.Append(folder).ToList();
        if (!await ShowTreeAsync(roots[0], roots.Skip(1))) return;
        if (_app.Workspace.Active is { } project)
        {
            project.Folders = [.. _fileTree!.Roots];
            _app.Workspace.SaveProject(project);
        }
        SetStatusMessage(Loc.Get(K.ProjectFolderAdded, ("name", Path.GetFileName(folder) is { Length: > 0 } n ? n : folder)));
    }

    internal async Task AddFolderToPanelWithDialogAsync()
    {
        if (await _app.Dialogs.PickFolderAsync(this) is { } folder) await AddFolderToPanelAsync(folder);
    }

    internal async Task SaveProjectWithPromptAsync()
    {
        if (_fileTree is null) { SetStatusMessage(Loc.Get(K.ProjectNeedsFolder)); return; }
        string initial = _app.Workspace.Active?.Name ?? FilesTitle(_fileTree);
        if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.ProjectSaveTitle), Loc.Get(K.ProjectNameLabel), initial) is { } name && name.Trim().Length > 0) SaveProject(name.Trim());
    }

    internal void ShowProjects(bool delete = false)
    {
        var rows = _app.Workspace.Projects.Select(p => new QuickRow(p.Name, null, string.Join("; ", p.Folders), null, true, null, p.Name,
            delete ? () => DeleteProject(p) : () => _ = OpenProjectAsync(p))).ToList();
        ShowChoice(Loc.Get(delete ? K.ProjectDeleteWatermark : K.ProjectWatermark), Loc.Get(K.A11yProjects), rows, emptyText: Loc.Get(K.ProjectEmpty));
    }

    private void DeleteProject(ProjectData project)
    {
        try
        {
            _app.Workspace.DeleteProject(project.Name);
            if (_app.Workspace.Active is null) ApplyProjectSettings(null);
            SetStatusMessage(Loc.Get(K.ProjectDeleted, ("name", project.Name)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(e.Message); }
    }

    /// <summary>The settings file of a project has the format of the user settings and is the Workspace layer while the project is active.</summary>
    private void ApplyProjectSettings(ProjectData? project)
    {
        var stack = _app.Workbench.Settings;
        stack.SetLayer(SettingSource.Workspace, null);
        foreach (var language in _projectLanguages) stack.SetLayer(SettingSource.WorkspaceLanguage, null, language);
        _projectLanguages.Clear();
        if (project is null) return;
        var file = SettingsFile.Load(_app.Workspace.ProjectSettingsPath(project.Name));
        if (file.LoadError is { } reason) { SetStatusMessage(Loc.Get(K.ProjectSettingsBroken, ("name", project.Name), ("reason", reason))); return; }
        file.ApplyTo(stack, SettingSource.Workspace, SettingSource.WorkspaceLanguage, project.Name);
        _projectLanguages.AddRange(file.Languages.Select(l => l.ToLowerInvariant()));
    }

    /// <summary>Opens the settings file of the active project in a tab, creating it empty when it does not exist yet.</summary>
    internal void EditProjectSettings()
    {
        if (_app.Workspace.Active is not { } project) { SetStatusMessage(Loc.Get(K.ProjectNone)); return; }
        string path = _app.Workspace.ProjectSettingsPath(project.Name);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var saved = SettingsFile.Create().Save(path);
            if (saved != SettingsSaveResult.Saved) { SetStatusMessage(saved.ToString()); return; }
        }
        OpenFile(path, null);
    }

    // ---- favorites ----

    /// <summary>The row selected in the panel, else the active file, else the first folder of the panel.</summary>
    internal void AddFavorite()
    {
        string? path = SelectedFileRow?.Entry.Path ?? Active?.Path ?? _fileTree?.Root;
        if (path is null) { SetStatusMessage(Loc.Get(K.FavoriteNothing)); return; }
        SetStatusMessage(Loc.Get(_app.Workspace.AddFavorite(path) ? K.FavoriteAdded : K.FavoriteExists, ("path", path)));
    }

    /// <summary>The favorites as a list; one that is gone is shown dimmed and says so.</summary>
    internal void ShowFavorites(bool remove = false)
    {
        var rows = _app.Workspace.Favorites.Select(path =>
        {
            bool exists = Directory.Exists(path) || File.Exists(path);
            string name = Path.GetFileName(path) is { Length: > 0 } n ? n : path;
            return new QuickRow(name, null, path, null, remove || exists, remove || exists ? null : Loc.Get(K.FavoriteMissing), name,
                remove ? () => RemoveFavorite(path) : () => OpenFavorite(path));
        }).ToList();
        ShowChoice(Loc.Get(remove ? K.FavoriteRemoveWatermark : K.FavoriteWatermark), Loc.Get(K.A11yFavorites), rows, emptyText: Loc.Get(K.FavoritesEmpty));
    }

    internal void OpenFavorite(string path)
    {
        if (Directory.Exists(path)) _ = ShowFolderAsync(path);
        else if (File.Exists(path)) JumpToFile(path, null);
        else SetStatusMessage(Loc.Get(K.FavoriteMissing) + ": " + path);
    }

    private void RemoveFavorite(string path)
    {
        try
        {
            _app.Workspace.RemoveFavorite(path);
            SetStatusMessage(Loc.Get(K.FavoriteRemoved, ("path", path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(e.Message); }
    }

    // ---- named sessions ----

    /// <summary>The tabs, their groups and carets as data. A tab with unsaved changes, or without a file, keeps its text so it comes back as it was.</summary>
    internal SessionData CaptureNamedSession(string name)
    {
        var session = new SessionData { Name = name };
        foreach (var group in _groups.Where(g => g.Tabs.Count > 0))
        {
            var data = new SessionGroupData();
            foreach (var tab in group.Tabs.Where(t => !t.Doc.IsResults && !t.Doc.IsVirtual && t.Doc.Source is null && (t.Path is not null || t.Doc.HasUnsavedWork)))
            {
                var entry = new SessionTabData { Path = tab.Path };
                if (tab.Surface is { } surface)
                {
                    var text = surface.Document;
                    var sel = surface.GetSelections()[0];
                    var anchor = text.GetLocation(sel.Anchor);
                    var caret = text.GetLocation(sel.Caret);
                    (entry.AnchorLine, entry.AnchorColumn, entry.CaretLine, entry.CaretColumn) = (anchor.Line, anchor.Column, caret.Line, caret.Column);
                    if (tab.Path is null || tab.Doc.HasUnsavedWork) entry.Text = text.Text;
                }
                if (tab == group.Active) data.ActiveTab = data.Tabs.Count;
                data.Tabs.Add(entry);
            }
            if (data.Tabs.Count == 0) continue;
            if (group == _activeGroup) session.ActiveGroup = session.Groups.Count;
            session.Groups.Add(data);
        }
        return session;
    }

    /// <summary>Saves the open tabs under the name, replacing a session of that name. False when there is nothing to save.</summary>
    internal bool SaveNamedSession(string name)
    {
        var session = CaptureNamedSession(name);
        if (session.Groups.Count == 0) { SetStatusMessage(Loc.Get(K.SessionNothing)); return false; }
        try
        {
            _app.Workspace.SaveSession(session);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(e.Message);
            return false;
        }
        SetStatusMessage(Loc.Get(K.SessionSaved, ("name", name)));
        return true;
    }

    internal async Task SaveNamedSessionWithPromptAsync()
    {
        if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.SessionSaveTitle), Loc.Get(K.SessionNameLabel), "") is { } name && name.Trim().Length > 0) SaveNamedSession(name.Trim());
    }

    /// <summary>Opens the tabs of a session next to the ones already open: files by path, the others from the text kept in the session. Returns the tabs that are missing.</summary>
    internal IReadOnlyList<string> OpenNamedSession(SessionData session)
    {
        var missing = new List<string>();
        TabGroup? wantActive = null;
        int index = 0;
        foreach (var data in session.Groups.Where(g => g.Tabs.Count > 0))
        {
            var group = index < _groups.Count ? _groups[index] : AddGroup();
            DocTab? active = null;
            for (int i = 0; i < data.Tabs.Count; i++)
            {
                var saved = data.Tabs[i];
                _activeGroup = group;
                var tab = OpenSessionTab(saved, missing);
                if (tab is null) continue;
                if (saved.Text is not null && tab.Surface is { } surface && surface.Document.Text != saved.Text) surface.Document.Replace(0, surface.Document.TextLength, saved.Text);
                ApplyCaret(tab, saved);
                if (i == data.ActiveTab) active = tab;
            }
            if (active is not null) group.Select(active);
            if (index == session.ActiveGroup) wantActive = group;
            index++;
        }
        _activeGroup = wantActive ?? _activeGroup;
        OnActiveTabChanged();
        if (Active is { } shown) FocusView(shown);
        SetStatusMessage(missing.Count == 0
            ? Loc.Get(K.SessionOpened, ("name", session.Name))
            : Loc.Get(K.SessionOpenedMissing, ("name", session.Name), ("missing", string.Join(", ", missing))));
        return missing;
    }

    private DocTab? OpenSessionTab(SessionTabData saved, List<string> missing)
    {
        if (saved.Path is { } path)
        {
            try
            {
                if (File.Exists(path)) return OpenFile(path, null);
            }
            catch (IOException) { /* reported below with the other missing files */ }
            missing.Add(Path.GetFileName(path));
            // the file is gone but the session still holds its text: it comes back as an unnamed document, not lost
            return saved.Text is null ? null : NewDocument();
        }
        return saved.Text is null ? null : NewDocument();
    }

    private static void ApplyCaret(DocTab tab, SessionTabData saved)
    {
        if (tab.Surface is not { } surface) return;
        var text = surface.Document;
        int Offset(int line, int column)
        {
            var l = text.GetLineByNumber(Math.Clamp(line, 1, text.LineCount));
            return l.Offset + Math.Clamp(column - 1, 0, l.Length);
        }
        surface.SetSelections([new Sel(Offset(saved.AnchorLine, saved.AnchorColumn), Offset(saved.CaretLine, saved.CaretColumn))]);
    }

    internal void ShowSessions(bool delete = false)
    {
        var sessions = _app.Workspace.Sessions();
        ReportWorkspaceProblems();
        var rows = sessions.Select(s => new QuickRow(s.Name, null, s.Saved.LocalDateTime.ToString("g"), null, true, null, s.Name,
            delete ? () => _ = DeleteSessionAsync(s) : () => OpenNamedSession(s))).ToList();
        ShowChoice(Loc.Get(delete ? K.SessionDeleteWatermark : K.SessionWatermark), Loc.Get(K.A11ySessions), rows, emptyText: Loc.Get(K.SessionEmpty));
    }

    internal async Task DeleteSessionAsync(SessionData session)
    {
        var ask = new ChoiceRequest(Loc.Get(K.SessionDeleteTitle), Loc.Get(K.SessionDeleteMessage, ("name", session.Name)),
            [new DialogButton(Loc.Get(K.SessionDeleteConfirm)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;
        try
        {
            _app.Workspace.DeleteSession(session.Name);
            SetStatusMessage(Loc.Get(K.SessionDeleted, ("name", session.Name)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(e.Message); }
    }
}
