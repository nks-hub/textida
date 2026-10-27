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

    /// <summary>"Convert to": rewrites the bytes after the inspector listed what changes. A target that would lose characters needs the user's explicit replacement first.</summary>
    public async Task<bool> ConvertToAsync(DocTab tab, TextCodec codec, string? headline = null)
    {
        var od = tab.Doc;
        var doc = od.Doc;
        if (codec == doc.Codec || od.IsLarge) return false;
        var before = doc.Codec;
        if (!await ConfirmTypedLossAsync(tab)) return false;
        if (doc.Codec != before) return true; // the inspector already converted to the encoding the user picked
        var report = doc.InspectConvert(codec);
        var (answer, other) = await ConfirmInspectionAsync(od, report, headline ?? Loc.Get(K.InspectConvert, ("encoding", codec.Name)), Loc.Get(K.LossConvertReplaced));
        if (answer == LossAnswer.ChangeEncoding) return await ConvertToAsync(tab, other!, headline);
        if (answer != LossAnswer.Replace) return false;
        report.Confirm(report.HasLoss ? "?" : null);
        var result = doc.Apply(report);
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

    /// <summary>Unifies the line endings after the inspector listed every ending that changes. One undo step.</summary>
    public async Task<bool> UnifyEolAsync(DocTab tab, EolStyle target)
    {
        var doc = tab.Doc.Doc;
        if (tab.Doc.IsLarge) return false;
        var report = doc.InspectUnifyEol(target);
        if (report.RangeCount == 0)
        {
            SetStatusMessage(Loc.Get(K.StatusNothingToChange));
            return false;
        }
        return await ApplyInspectedAsync(tab, report, Loc.Plural(K.PreviewEol, report.RangeCount, ("style", EolName(target))));
    }

    private static string EolName(EolStyle target) => target switch { EolStyle.CrLf => "CRLF", EolStyle.Cr => "CR", _ => "LF" };

    /// <summary>NFC or NFD, only on request and only after the inspector listed the places that change.</summary>
    public async Task<bool> NormalizeAsync(DocTab tab, NormalizationForm form)
    {
        var doc = tab.Doc.Doc;
        if (tab.Doc.IsLarge) return false;
        var report = doc.InspectNormalize(form);
        if (report.RangeCount == 0)
        {
            SetStatusMessage(Loc.Get(K.StatusNothingToChange));
            return false;
        }
        if (report.IsBlocked)
        {
            SetStatusMessage(Loc.Get(K.StatusNormalizeBlocked, ("encoding", doc.Codec.Name)));
            return false;
        }
        return await ApplyInspectedAsync(tab, report, Loc.Plural(K.PreviewNormalize, report.RangeCount, ("form", FormName(form))));
    }

    private static string FormName(NormalizationForm form) => form switch { NormalizationForm.FormC => "NFC", NormalizationForm.FormD => "NFD", NormalizationForm.FormKC => "NFKC", _ => "NFKD" };

    /// <summary>Shows the report and applies it when the user confirms; the one place where a line-ending or normalisation action reaches the bytes.</summary>
    private async Task<bool> ApplyInspectedAsync(DocTab tab, ByteInspection report, string headline)
    {
        var (answer, _) = await ConfirmInspectionAsync(tab.Doc, report, headline, Loc.Get(K.PreviewApply));
        if (answer != LossAnswer.Replace) return false;
        report.Confirm(report.HasLoss ? "?" : null);
        var result = tab.Doc.Doc.Apply(report);
        if (!result.Applied) SetStatusMessage(Loc.Get(K.StatusChangeConflict));
        return result.Applied;
    }

    /// <summary>The encoding the <c>charset</c> property names, with a BOM only for <c>utf-8-bom</c> as the EditorConfig specification says; null for a value this editor has no encoding for.</summary>
    internal static TextCodec? EditorConfigCodec(string? charset) => charset switch
    {
        "utf-8" => TextCodec.Utf8,
        "utf-8-bom" => TextCodec.Utf8Bom,
        "utf-16le" => TextCodec.Utf16LE,
        "utf-16be" => TextCodec.Utf16BE,
        "latin1" => TextCodec.Cp1252,
        _ => null,
    };

    /// <summary>
    /// Brings the file to what its <c>.editorconfig</c> says: the <c>charset</c> first, then <c>end_of_line</c>, each through the inspector. Properties the
    /// file does not set are left alone, and nothing happens on open or save without being asked.
    /// </summary>
    public async Task<bool> ApplyEditorConfigFormatAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (od.IsLarge || od.IsResults || od.IsVirtual) return false;
        od.RefreshEditorConfig();
        var codec = EditorConfigCodec(od.EditorConfig.Get("charset"));
        bool hasCharset = codec is not null;
        EolStyle? eol = od.EditorConfig.Get("end_of_line") switch { "lf" => EolStyle.Lf, "crlf" => EolStyle.CrLf, "cr" => EolStyle.Cr, _ => null };
        if (!hasCharset && eol is null)
        {
            SetStatusMessage(Loc.Get(K.InspectEditorConfigNone));
            return false;
        }
        bool changed = false;
        if (codec is not null && codec != od.Doc.Codec)
        {
            if (!await ConvertToAsync(tab, codec, Loc.Get(K.InspectEditorConfig, ("encoding", codec.Name)))) return false;
            changed = true;
        }
        if (eol is { } style && od.Doc.InspectUnifyEol(style).RangeCount > 0) changed |= await UnifyEolAsync(tab, style);
        if (!changed) SetStatusMessage(Loc.Get(K.InspectEditorConfigDone));
        return changed;
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
