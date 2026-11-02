using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Agents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The size of a selection, of the document and of a copy for an AI assistant in tokens (F255), counted here on this computer: no text is sent anywhere (<see cref="TokenEstimator"/>).
/// The OpenAI rule counts exactly with its encoding on a background thread, and shows the estimate marked ≈ until the count is done; every other rule is an estimate and always shown as one.
/// The rule in use can be chosen in the menu of the status bar item.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Button _tokensItem = StatusItem();
    private TokenPreset _tokenRule = TokenEstimator.Generic;
    private (WeakReference<DocTab> Tab, string Key, TokenCount Count)? _tokenCounted; // weak: a closed tab is not kept alive by its last count
    private bool _tokenCounting;

    /// <summary>The status bar item with the count for the selection; visible only while something is selected.</summary>
    internal Button TokensItem => _tokensItem;

    /// <summary>The rule the counts follow; it lasts until the window closes.</summary>
    internal TokenPreset TokenRule
    {
        get => _tokenRule;
        set
        {
            _tokenRule = value;
            UpdateStatus();
        }
    }

    /// <summary>The task of the last details dialog, for tests.</summary>
    internal Task TokenDialogTask { get; private set; } = Task.CompletedTask;

    /// <summary>The task of the last exact count for the status bar, for tests.</summary>
    internal Task TokenCountTask { get; private set; } = Task.CompletedTask;

    internal static string FormatTokens(long count) => count.ToString("N0", CultureInfo.CurrentCulture);

    internal string TokenRuleName(TokenPreset preset) => Loc.Get(preset.Id switch
    {
        "openai" => K.TokensPresetOpenAi,
        "gemini" => K.TokensPresetGemini,
        "claude" => K.TokensPresetClaude,
        "claude47" => K.TokensPresetClaudeNew,
        _ => K.TokensPresetGeneric,
    });

    /// <summary>
    /// Characters in the selection of a text tab, or null when nothing is selected. A selection in paged mode is decoded up to <see cref="PluginLimits.TokenEstimateMaxSelectionBytes"/>;
    /// a larger one is counted by its bytes, which is never less than its characters.
    /// </summary>
    private static long? SelectionCharacters(DocTab tab)
    {
        if (tab.Surface is { } surface) return surface.SelectedLength is > 0 and var length ? length : null;
        if (tab.Paged is { HasSelection: true } paged)
        {
            var range = paged.SelectionRange;
            return range.Length <= PluginLimits.TokenEstimateMaxSelectionBytes ? paged.SelectedText().Length : range.Length;
        }
        return null;
    }

    /// <summary>The selected texts for an exact count, or null when a paged selection is too large to decode (its characters are then only its bytes).</summary>
    private static List<string>? SelectionTexts(DocTab tab) =>
        tab.Paged is { } paged && paged.SelectionRange.Length > PluginLimits.TokenEstimateMaxSelectionBytes ? null : SelectionPieces(tab).ConvertAll(p => p.Text);

    /// <summary>What the selection of a tab is: the revision of the document and the selected ranges.</summary>
    private static string SelectionKey(DocTab tab) => tab.Surface is { } surface
        ? tab.Doc.Doc.Revision + ":" + string.Join(",", surface.GetSelections().Select(s => s.Start + "+" + s.Length))
        : tab.Doc.Doc.Revision + ":" + tab.Paged!.SelectionRange.Start + "+" + tab.Paged.SelectionRange.Length;

    /// <summary>
    /// Called by the status update of a text tab. An exact count runs on a background thread, one at a time; when it is done the item is updated again, which shows it or, when the
    /// selection changed in the meantime, starts the count of the new one.
    /// </summary>
    private void UpdateTokensItem(DocTab tab)
    {
        if (SelectionCharacters(tab) is not { } characters)
        {
            _tokensItem.IsVisible = false;
            return;
        }
        var count = new TokenCount(TokenEstimator.Estimate(characters, _tokenRule), false);
        if (TokenEstimator.CountsExactly(characters, _tokenRule))
        {
            string key = SelectionKey(tab);
            if (_tokenCounted is { } done && done.Key == key && done.Tab.TryGetTarget(out var counted) && counted == tab) count = done.Count;
            else if (!_tokenCounting && SelectionTexts(tab) is { } texts) TokenCountTask = CountTokensAsync(tab, key, texts, _tokenRule);
        }
        ShowTokens(count);
    }

    private async Task CountTokensAsync(DocTab tab, string key, List<string> texts, TokenPreset rule)
    {
        _tokenCounting = true;
        try { _tokenCounted = (new(tab), key, await Task.Run(() => TokenEstimator.Count(texts, rule))); }
        finally { _tokenCounting = false; }
        if (Active is { Hex: null, Custom: null } active && !active.Doc.IsResults) UpdateTokensItem(active);
    }

    private void ShowTokens(TokenCount count)
    {
        string number = FormatTokens(count.Tokens);
        _tokensItem.IsVisible = true;
        if (count.Exact)
        {
            string encoding = _tokenRule.Encoding!;
            _tokensItem.Content = Loc.Get(K.TokensStatusExact, ("tokens", number), ("encoding", encoding));
            ToolTip.SetTip(_tokensItem, Loc.Get(K.TokensStatusExactTip, ("encoding", encoding)));
            AutomationProperties.SetName(_tokensItem, Loc.Get(K.TokensStatusExactA11y, ("value", (string)_tokensItem.Content)));
            return;
        }
        _tokensItem.Content = Loc.Get(K.TokensStatus, ("count", number));
        ToolTip.SetTip(_tokensItem, Loc.Get(K.TokensStatusTip, ("rule", TokenRuleName(_tokenRule))));
        AutomationProperties.SetName(_tokensItem, Loc.Get(K.TokensStatusA11y, ("value", (string)_tokensItem.Content)));
    }

    /// <summary>The menu of the status bar item: the rule to count by, and the details.</summary>
    internal MenuFlyout ShowTokenMenu()
    {
        var flyout = new MenuFlyout();
        foreach (var preset in TokenEstimator.Presets)
        {
            var chosen = preset;
            var item = new MenuItem { Header = TokenRuleName(preset), ToggleType = MenuItemToggleType.Radio, IsChecked = preset == _tokenRule, GroupName = "tokenrule" };
            item.Click += (_, _) => TokenRule = chosen;
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var details = new MenuItem { Header = Loc.Get(K.TokensDetails) };
        details.Click += (_, _) => ShowTokenEstimate();
        flyout.Items.Add(details);
        flyout.ShowAt(_tokensItem);
        return flyout;
    }

    /// <summary>Agent, Estimate Tokens: the selection and the document of the active text tab in tokens, with the rule and where it comes from. Exact counts run on a background thread.</summary>
    internal CommandOutcome ShowTokenEstimate()
    {
        if (Active is not { Hex: null, Custom: null } tab || tab.Doc.IsResults) return CommandOutcome.NotHandled;
        var rule = _tokenRule;
        long? selected = SelectionCharacters(tab);
        var selectedTexts = selected is { } characters && TokenEstimator.CountsExactly(characters, rule) ? SelectionTexts(tab) : null;
        long? length = tab.Surface?.Document.TextLength;
        string? documentText = length is { } all && TokenEstimator.CountsExactly(all, rule) ? tab.Surface!.Document.Text : null;
        TokenDialogTask = ShowTokenDialogAsync(rule, selected, selectedTexts, length, documentText);
        return CommandOutcome.Handled;
    }

    private async Task ShowTokenDialogAsync(TokenPreset rule, long? selected, List<string>? selectedTexts, long? length, string? documentText)
    {
        static TokenCount CountOf(long characters, IReadOnlyList<string>? texts, TokenPreset rule) =>
            texts is null ? new(TokenEstimator.Estimate(characters, rule), false) : TokenEstimator.Count(texts, rule);

        var (selection, document) = await Task.Run(() => (
            selected is { } s ? CountOf(s, selectedTexts, rule) : default,
            length is { } l ? CountOf(l, documentText is null ? null : [documentText], rule) : default));
        var lines = new List<string>();
        if (selected is { } characters)
            lines.Add(Loc.Get(selection.Exact ? K.TokensSelectionExact : K.TokensSelection, ("tokens", FormatTokens(selection.Tokens)), ("chars", FormatTokens(characters))));
        if (length is { } all)
            lines.Add(Loc.Get(document.Exact ? K.TokensDocumentExact : K.TokensDocument, ("tokens", FormatTokens(document.Tokens)), ("chars", FormatTokens(all))));
        else if (selected is null) lines.Add(Loc.Get(K.TokensPagedNote));
        string note = rule.Encoding is { } encoding
            ? Loc.Get(K.TokensExactNote, ("encoding", encoding), ("size", FormatTokens(TokenEstimator.ExactMaxCharacters)))
            : Loc.Get(K.TokensEstimateNote);
        string message = string.Join("\n", lines) + "\n\n" + Loc.Get(K.TokensRule, ("rule", TokenRuleName(rule)), ("source", rule.Source)) + " " + note;
        await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.TokensDialogTitle), message, [new DialogButton(Loc.Get(K.PanelOutputClose), IsDefault: true, IsCancel: true)]));
    }
}
