using System.Text;
using Avalonia.Controls;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Shell;

/// <summary>One entry of the encoding menus. <see cref="Lost"/> counts the characters a conversion would lose, known before the choice is made.</summary>
internal sealed record EncodingChoice(TextCodec Codec, int Lost, bool IsCurrent, bool Enabled)
{
    public string Label => Lost > 0 ? $"{Codec.Name} · {Loc.Plural(K.EncodingLossy, Lost)}" : Codec.Name;
}

internal sealed partial class MainWindow
{
    /// <summary>Encodings to show the same bytes in ("Re-read as"): nothing is written, so nothing is lossy.</summary>
    public IReadOnlyList<EncodingChoice> RereadChoices(DocTab tab) =>
        [.. TextCodec.All.Select(c => new EncodingChoice(c, 0, c == tab.Doc.Doc.Codec, tab.Doc.Doc.Backend.Supports(c)))];

    /// <summary>Encodings to rewrite the bytes in ("Convert to"), each with the number of characters it cannot hold.</summary>
    public IReadOnlyList<EncodingChoice> ConvertChoices(DocTab tab)
    {
        var doc = tab.Doc.Doc;
        if (tab.Doc.IsLarge) return [.. TextCodec.All.Select(c => new EncodingChoice(c, 0, c == doc.Codec, false))];
        var targets = ByteFidelity.ListTargets(doc.Codec.Decode(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
        return [.. targets.Select(t => new EncodingChoice(t.Codec, t.Lost, t.Codec == doc.Codec, doc.Backend.Supports(t.Codec)))];
    }

    /// <summary>"Re-read as": the same bytes through another encoding. The file on disk and the revision of its content stay as they are.</summary>
    public void RereadAs(DocTab tab, TextCodec codec)
    {
        var od = tab.Doc;
        od.Doc.ReinterpretAs(codec);
        od.SetEncodingChosen();
        tab.Paged?.ApplyOptions(Options);
        od.NotifyChanged();
        SetStatusMessage(Loc.Get(K.StatusRereadAs, ("encoding", codec.Name)));
    }

    /// <summary>"Convert to": rewrites the bytes. A target that would lose characters needs the user's explicit replacement first.</summary>
    public async Task<bool> ConvertToAsync(DocTab tab, TextCodec codec)
    {
        var od = tab.Doc;
        var doc = od.Doc;
        if (codec == doc.Codec || od.IsLarge) return false;
        var before = doc.Codec;
        if (!await ConfirmTypedLossAsync(tab)) return false;
        if (doc.Codec != before) return true; // the inspector already converted to the encoding the user picked
        var preview = doc.PreviewConvert(codec);
        string? replacement = null;
        if (preview.Loss.HasLoss)
        {
            var (answer, other) = await ConfirmLossAsync(od, preview.Loss, codec, Loc.Get(K.LossConvertReplaced));
            if (answer == LossAnswer.ChangeEncoding) return await ConvertToAsync(tab, other!);
            if (answer != LossAnswer.Replace) return false;
            replacement = "?";
        }
        var result = doc.ConvertTo(codec, replacement);
        if (!result.Applied)
        {
            SetStatusMessage(Loc.Get(K.StatusConvertFailed, ("encoding", codec.Name)));
            return false;
        }
        od.SetEncodingChosen();
        od.NotifyChanged();
        SetStatusMessage(Loc.Get(K.StatusConverted, ("encoding", codec.Name)));
        return true;
    }

    /// <summary>Unifies the line endings after showing exactly how many change. One undo step.</summary>
    public async Task<bool> UnifyEolAsync(DocTab tab, EolStyle target)
    {
        var doc = tab.Doc.Doc;
        if (tab.Doc.IsLarge) return false;
        var preview = doc.PreviewUnifyEol(target);
        string name = target switch { EolStyle.CrLf => "CRLF", EolStyle.Cr => "CR", _ => "LF" };
        if (preview.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.StatusNothingToChange));
            return false;
        }
        if (!await ConfirmPreviewAsync(tab, preview, Loc.Plural(K.PreviewEol, preview.Count, ("style", name)))) return false;
        return Apply(tab, preview);
    }

    /// <summary>NFC or NFD, only on request and only after showing how many places change.</summary>
    public async Task<bool> NormalizeAsync(DocTab tab, NormalizationForm form)
    {
        var doc = tab.Doc.Doc;
        if (tab.Doc.IsLarge) return false;
        var preview = doc.PreviewNormalize(form);
        if (preview.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.StatusNothingToChange));
            return false;
        }
        if (preview.IsBlocked)
        {
            SetStatusMessage(Loc.Get(K.StatusNormalizeBlocked, ("encoding", doc.Codec.Name)));
            return false;
        }
        if (!await ConfirmPreviewAsync(tab, preview, Loc.Plural(K.PreviewNormalize, preview.Count, ("form", FormName(form))))) return false;
        return Apply(tab, preview);
    }

    private static string FormName(NormalizationForm form) => form switch { NormalizationForm.FormC => "NFC", NormalizationForm.FormD => "NFD", NormalizationForm.FormKC => "NFKC", _ => "NFKD" };

    private bool Apply(DocTab tab, ChangePreview preview)
    {
        var result = tab.Doc.Doc.ApplyPreview(preview);
        if (!result.Applied) SetStatusMessage(Loc.Get(K.StatusChangeConflict));
        return result.Applied;
    }

    private async Task<bool> ConfirmPreviewAsync(DocTab tab, ChangePreview preview, string message)
    {
        var doc = tab.Doc.Doc;
        var lines = new List<string>();
        foreach (var edit in preview.Edits.Take(50))
            lines.Add(doc.TryGetPosition(edit.Range.Start, out var p)
                ? Loc.Get(K.PreviewLine, ("line", p.Line + 1), ("column", p.Column + 1), ("bytes", edit.Range.Length))
                : Loc.Get(K.PreviewBytes, ("start", edit.Range.Start), ("bytes", edit.Range.Length)));
        if (preview.Count > lines.Count) lines.Add(Loc.Get(K.PreviewMore, ("count", preview.Count - lines.Count)));
        var request = new ChoiceRequest(Loc.Get(K.PreviewTitle), message,
            [new DialogButton(Loc.Get(K.PreviewApply), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], lines);
        return await _app.Dialogs.ChooseAsync(this, request) == 0;
    }

    // ---- the menu of the encoding item ----

    /// <summary>Opens the encoding menu at the status bar item: both actions as submenus, or only one of them (<paramref name="onlyConvert"/>).</summary>
    public MenuFlyout? ShowEncodingMenu(bool? onlyConvert)
    {
        if (Active is not { } tab) return null;
        var flyout = new MenuFlyout();
        MenuItem Submenu(string header, IReadOnlyList<EncodingChoice> choices, Action<EncodingChoice> pick)
        {
            var parent = new MenuItem { Header = header };
            foreach (var c in choices) parent.Items.Add(ChoiceItem(c, pick));
            return parent;
        }
        if (onlyConvert is null)
        {
            flyout.Items.Add(Submenu(Loc.Get(K.EncodingRereadAs), RereadChoices(tab), c => RereadAs(tab, c.Codec)));
            flyout.Items.Add(Submenu(Loc.Get(K.EncodingConvertTo), ConvertChoices(tab), c => _ = ConvertToAsync(tab, c.Codec)));
        }
        else
        {
            foreach (var c in onlyConvert.Value ? ConvertChoices(tab) : RereadChoices(tab))
                flyout.Items.Add(ChoiceItem(c, onlyConvert.Value ? c2 => _ = ConvertToAsync(tab, c2.Codec) : c2 => RereadAs(tab, c2.Codec)));
        }
        flyout.ShowAt(_encodingItem);
        return flyout;
    }

    private static MenuItem ChoiceItem(EncodingChoice choice, Action<EncodingChoice> pick)
    {
        var item = new MenuItem { Header = choice.Label, IsEnabled = choice.Enabled, ToggleType = MenuItemToggleType.Radio, IsChecked = choice.IsCurrent, GroupName = "encoding", Tag = choice };
        item.Click += (_, _) => pick(choice);
        return item;
    }
}
