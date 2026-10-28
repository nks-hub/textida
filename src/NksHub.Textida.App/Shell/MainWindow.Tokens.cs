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
/// The size of a selection, of the document and of a copy for an AI assistant in tokens (F255). The number is an estimate by a published rule of thumb (<see cref="TokenEstimator"/>), counted
/// here on this computer: no text is sent anywhere. It is always shown as an estimate, and the rule in use can be chosen in the menu of the status bar item.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Button _tokensItem = StatusItem();
    private TokenPreset _tokenRule = TokenEstimator.Generic;

    /// <summary>The status bar item with the estimate for the selection; visible only while something is selected.</summary>
    internal Button TokensItem => _tokensItem;

    /// <summary>The rule the estimates follow; it lasts until the window closes.</summary>
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
    /// Characters in the selection of a text tab, or null when nothing is selected. A selection in paged mode is decoded up to <see cref="PluginLimitsPlaceholders.TokenEstimateMaxSelectionBytes"/>;
    /// a larger one is counted by its bytes, which is never less than its characters.
    /// </summary>
    private static long? SelectionCharacters(DocTab tab)
    {
        if (tab.Surface is { } surface) return surface.SelectedLength is > 0 and var length ? length : null;
        if (tab.Paged is { HasSelection: true } paged)
        {
            var range = paged.SelectionRange;
            return range.Length <= PluginLimitsPlaceholders.TokenEstimateMaxSelectionBytes ? paged.SelectedText().Length : range.Length;
        }
        return null;
    }

    /// <summary>Called by the status update of a text tab.</summary>
    private void UpdateTokensItem(DocTab tab)
    {
        if (SelectionCharacters(tab) is not { } characters)
        {
            _tokensItem.IsVisible = false;
            return;
        }
        string count = FormatTokens(TokenEstimator.Estimate(characters, _tokenRule));
        _tokensItem.IsVisible = true;
        _tokensItem.Content = Loc.Get(K.TokensStatus, ("count", count));
        ToolTip.SetTip(_tokensItem, Loc.Get(K.TokensStatusTip, ("rule", TokenRuleName(_tokenRule))));
        AutomationProperties.SetName(_tokensItem, Loc.Get(K.TokensStatusA11y, ("value", (string)_tokensItem.Content)));
    }

    /// <summary>The menu of the status bar item: the rule to estimate by, and the details.</summary>
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

    /// <summary>Agent, Estimate Tokens: the selection and the document of the active text tab in tokens, with the rule and where it comes from.</summary>
    internal CommandOutcome ShowTokenEstimate()
    {
        if (Active is not { Hex: null, Custom: null } tab || tab.Doc.IsResults) return CommandOutcome.NotHandled;
        var lines = new List<string>();
        long? selected = SelectionCharacters(tab);
        if (selected is { } characters)
            lines.Add(Loc.Get(K.TokensSelection, ("count", FormatTokens(TokenEstimator.Estimate(characters, _tokenRule))), ("chars", FormatTokens(characters))));
        if (tab.Surface is { } surface)
        {
            long length = surface.Document.TextLength;
            lines.Add(Loc.Get(K.TokensDocument, ("count", FormatTokens(TokenEstimator.Estimate(length, _tokenRule))), ("chars", FormatTokens(length))));
        }
        else if (selected is null) lines.Add(Loc.Get(K.TokensPagedNote));
        string message = string.Join("\n", lines) + "\n\n" + Loc.Get(K.TokensRule, ("rule", TokenRuleName(_tokenRule)), ("source", _tokenRule.Source)) + " " + Loc.Get(K.TokensEstimateNote);
        TokenDialogTask = _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.TokensDialogTitle), message, [new DialogButton(Loc.Get(K.PanelOutputClose), IsDefault: true, IsCancel: true)]));
        return CommandOutcome.Handled;
    }
}
