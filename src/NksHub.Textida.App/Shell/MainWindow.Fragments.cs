using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Fragments;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A string of a PHP or JavaScript file edited as JSON, SQL or HTML in a tab of its own (P8.9.1). The tab is an ordinary new document linked to the string by anchors
/// (<see cref="FragmentLink"/>); writing back escapes the text for the kind of string. Closing the file detaches the fragment tab: it stays open as a plain text, says so in its
/// title and writes nothing, so work in it is never lost.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The command <c>nks.edit.fragmentJson</c> and its siblings: opens the string at the caret in a new tab with the language <paramref name="languageId"/>.</summary>
    internal CommandOutcome EditStringAs(string languageId)
    {
        if (Active is not { Surface: { } surface } tab || tab.TableShown) return CommandOutcome.NotHandled;
        var parent = tab.Doc;
        bool? php = parent.Language.Id switch { "php" => true, "javascript" or "typescript" => false, _ => null };
        if (php is null || LanguageModes.GrammarId(parent) is not { } grammar)
        {
            SetStatusMessage(Loc.Get(K.FragmentLanguage));
            return CommandOutcome.Handled;
        }
        if (ToBytes(surface, surface.GetSelections()) is not [var caret, ..]) return CommandOutcome.NotHandled;
        var opened = FragmentLink.Open(parent.Doc, LanguageModes.Tokenizer(grammar), php.Value, caret.Start);
        if (opened.Link is not { } link)
        {
            SetStatusMessage(FragmentMessage(opened.Problem));
            return CommandOutcome.Handled;
        }

        var mode = LanguageModes.ById(languageId);
        long line = parent.Doc.TryGetPosition(link.Range.Start, out var position) ? position.Line + 1 : 1;
        string parentTitle = parent.Title;
        var fragmentTab = NewDocument();
        var fragment = fragmentTab.Doc;
        fragment.Fragment = link;
        fragment.FragmentParent = parent;
        fragment.ChooseLanguage(mode);
        fragment.TitleOverride = () => Loc.Get(link.IsDetached ? K.FragmentTabDetached : K.FragmentTab, ("language", mode.Name), ("title", parentTitle), ("line", line));
        fragmentTab.Surface!.Document.Text = opened.Text;
        fragment.Doc.MarkClean();
        _ = parent.Closed.Task.ContinueWith(_ => Dispatcher.UIThread.Post(() => DetachFragment(fragment)), TaskScheduler.Default);
        RefreshHeaders();
        FocusView(fragmentTab);
        return CommandOutcome.Handled;
    }

    private void DetachFragment(OpenDocument fragment)
    {
        if (fragment.Fragment is not { IsDetached: false } link) return;
        link.Detach();
        if (!_docs.Contains(fragment)) return;
        RefreshHeaders();
        SetStatusMessage(Loc.Get(K.FragmentParentClosed));
    }

    /// <summary>The command <c>nks.edit.fragmentWriteBack</c>: the text of the fragment tab goes into the string of its file.</summary>
    internal CommandOutcome WriteBackFragment()
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        if (tab.Doc.Fragment is not { } link)
        {
            SetStatusMessage(Loc.Get(K.FragmentNotFragment));
            return CommandOutcome.Handled;
        }
        _ = WriteBackFragmentAsync(tab.Doc, link, surface.Document.Text);
        return CommandOutcome.Handled;
    }

    internal async Task WriteBackFragmentAsync(OpenDocument fragment, FragmentLink link, string text)
    {
        var parent = fragment.FragmentParent;
        var problem = WriteFragment(link, parent, text, overwrite: false);
        if (problem == FragmentProblem.Conflict)
        {
            var request = new ChoiceRequest(Loc.Get(K.FragmentConflictTitle), Loc.Get(K.FragmentConflictMessage, ("title", parent!.Title)),
                [new DialogButton(Loc.Get(K.FragmentConflictOverwrite)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return;
            problem = WriteFragment(link, parent, text, overwrite: true);
        }
        if (problem != FragmentProblem.None)
        {
            SetStatusMessage(FragmentMessage(problem));
            return;
        }
        fragment.Doc.MarkClean();
        RefreshHeaders();
        SetStatusMessage(Loc.Get(K.FragmentWritten, ("title", parent!.Title)));
    }

    private static FragmentProblem WriteFragment(FragmentLink link, OpenDocument? parent, string text, bool overwrite)
    {
        if (parent is null || parent.Closed.Task.IsCompleted) link.Detach();
        else if (parent.ReadOnly) return FragmentProblem.ParentRejected;
        return link.WriteBack(text, overwrite);
    }

    private static string FragmentMessage(FragmentProblem problem) => problem switch
    {
        FragmentProblem.NoLiteral => Loc.Get(K.FragmentNoLiteral),
        FragmentProblem.Interpolation => Loc.Get(K.FragmentInterpolation),
        FragmentProblem.TemplateExpression => Loc.Get(K.FragmentTemplateExpression),
        FragmentProblem.BadEscape => Loc.Get(K.FragmentBadEscape),
        FragmentProblem.Indentation => Loc.Get(K.FragmentIndentation),
        FragmentProblem.ClosingMarker => Loc.Get(K.FragmentClosingMarker),
        FragmentProblem.NotEncodable => Loc.Get(K.FragmentNotEncodable),
        FragmentProblem.RangeLost => Loc.Get(K.FragmentRangeLost),
        FragmentProblem.Detached => Loc.Get(K.FragmentDetached),
        _ => Loc.Get(K.FragmentParentRejected),
    };
}
