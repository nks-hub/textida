using System.Text;

namespace NksHub.Textida.Core.Encodings;

public enum EolStyle { None, Lf, CrLf, Cr, Mixed }

/// <summary>Counts of each line ending found in a text.</summary>
public readonly record struct EolStats(int Lf, int CrLf, int Cr)
{
    public int Total => Lf + CrLf + Cr;

    public EolStyle Style =>
        Total == 0 ? EolStyle.None
        : (Lf > 0 ? 1 : 0) + (CrLf > 0 ? 1 : 0) + (Cr > 0 ? 1 : 0) > 1 ? EolStyle.Mixed
        : Lf > 0 ? EolStyle.Lf : CrLf > 0 ? EolStyle.CrLf : EolStyle.Cr;

    public bool IsMixed => Style == EolStyle.Mixed;

    /// <summary>The most frequent ending (CRLF, LF, CR on a tie); None when there is no line ending.</summary>
    public EolStyle Dominant =>
        Total == 0 ? EolStyle.None
        : CrLf >= Lf && CrLf >= Cr ? EolStyle.CrLf
        : Lf >= Cr ? EolStyle.Lf : EolStyle.Cr;

    /// <summary>Text for the status bar.</summary>
    public string Label => Style switch
    {
        EolStyle.Lf => "LF", EolStyle.CrLf => "CRLF", EolStyle.Cr => "CR", EolStyle.Mixed => "mixed", _ => "none",
    };
}

/// <summary>Counts line endings block by block; a CRLF split between two blocks is counted once.</summary>
public struct EolScanner
{
    private int _lf, _crlf, _cr;
    private bool _pendingCr;

    public void Add(ReadOnlySpan<char> block)
    {
        foreach (char c in block)
        {
            if (_pendingCr)
            {
                _pendingCr = false;
                if (c == '\n') { _crlf++; continue; }
                _cr++;
            }
            if (c == '\r') _pendingCr = true;
            else if (c == '\n') _lf++;
        }
    }

    public readonly EolStats Finish() => new(_lf, _crlf, _cr + (_pendingCr ? 1 : 0));
}

/// <summary>What an explicit change of line endings would touch.</summary>
public sealed record EolPreview(IReadOnlyList<TextEdit> Edits, int Changed, int Unchanged);

public static class Eol
{
    public static EolStats Analyze(ReadOnlySpan<char> text)
    {
        var s = new EolScanner();
        s.Add(text);
        return s.Finish();
    }

    public static string Sequence(EolStyle style) => style switch
    {
        EolStyle.Lf => "\n", EolStyle.CrLf => "\r\n", EolStyle.Cr => "\r",
        _ => throw new ArgumentException("Only LF, CRLF and CR are line ending sequences.", nameof(style)),
    };

    /// <summary>Line ending for text the user types: the configured one, else the document's dominant one, else <paramref name="fallback"/>.</summary>
    public static string ForNewLine(EolStats detected, EolStyle? configured = null, EolStyle fallback = EolStyle.Lf)
    {
        if (configured is EolStyle.Lf or EolStyle.CrLf or EolStyle.Cr) return Sequence(configured.Value);
        return Sequence(detected.Dominant is EolStyle.None ? fallback : detected.Dominant);
    }

    /// <summary>
    /// Preview of an explicit unification to <paramref name="target"/>. Apply the edits with
    /// <see cref="ByteFidelity.PlanSave"/>: only the line ending bytes change, everything else stays verbatim.
    /// </summary>
    public static EolPreview PreviewNormalize(DecodedText doc, EolStyle target)
    {
        string seq = Sequence(target);
        var text = doc.Text;
        var edits = new List<TextEdit>();
        int unchanged = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n') continue;
            int len = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            if (len == seq.Length && string.CompareOrdinal(text, i, seq, 0, len) == 0) unchanged++;
            else edits.Add(new TextEdit(i, len, seq));
            i += len - 1;
        }
        return new EolPreview(edits, edits.Count, unchanged);
    }
}

/// <summary>Explicit Unicode normalisation (NFC, NFD...) as an edit list; never applied implicitly.</summary>
public static class UnicodeNormalizer
{
    /// <summary>
    /// Line-sized edits for every line that is not yet in <paramref name="form"/>. Characters standing for invalid
    /// bytes are excluded, so those bytes survive.
    /// </summary>
    public static IReadOnlyList<TextEdit> Preview(DecodedText doc, NormalizationForm form)
    {
        var text = doc.Text;
        var edits = new List<TextEdit>();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool boundary = i == text.Length || text[i] == '\r' || text[i] == '\n' || doc.IsInvalid(i);
            if (!boundary) continue;
            if (i > start)
            {
                var seg = text.Substring(start, i - start);
                if (!seg.IsNormalized(form)) edits.Add(new TextEdit(start, seg.Length, seg.Normalize(form)));
            }
            start = i + 1;
        }
        return edits;
    }
}
