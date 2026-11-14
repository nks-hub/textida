using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using NksHub.Textida.App.Services;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Editing;

internal sealed partial class PagedTextView
{
    private sealed partial class LinesSurface
    {
        private readonly record struct LineFormatKey(LineView View, long Revision, Token[]? Tokens, string Family,
            double FontSize, CultureInfo Culture, EditorColorTheme Theme, SyntaxPalette Palette, Color Foreground,
            double Opacity, bool SecretsHidden, bool SecretsRevealed);
        private sealed record FormattedLine(LineFormatKey Key, FormattedText Text);
        private FormattedLine? formattedLine;

        private FormattedText FormatLine(DrawingContext context, LineView view, long line, double y, string shown,
            EditorColorTheme look, IBrush fore, ImmutableSolidColorBrush dim, int insertLength)
        {
            var palette = look.Palette;
            owner._tokens.TryGetValue(line, out var tokens);
            LineFormatKey? reusable = null;
            if (view.Truncated && ReferenceEquals(view.Display, view.Map.Text) && ReferenceEquals(shown, view.Display)
                && !owner._doc.IsLog && !owner.HideAnsi && owner._doc.LogHighlight is null
                && owner._preedit.Length == 0 && !owner._options.ShowWhitespace && fore is ISolidColorBrush solid)
            {
                var key = new LineFormatKey(view, owner.Doc.Revision, tokens, owner._options.FullFontFamily,
                    owner._options.FontSize, CultureInfo.CurrentCulture, look, palette, solid.Color, solid.Opacity,
                    owner._secrets.Hidden, owner._secrets.Revealed);
                if (formattedLine is { } cached && cached.Key == key && ReferenceEquals(cached.Key.Culture, key.Culture))
                    return cached.Text;
                reusable = key;
                fore = new ImmutableSolidColorBrush(solid.Color, solid.Opacity);
            }

            var levels = owner._doc.IsLog ? owner._doc.LogLevels : null;
            bool dimmed = levels is not null && owner.FoldCriteria is { } fold && !LogFold.KeepsAlone(view.Map.Text, levels, fold);
            var text = Format(shown, dimmed ? dim : fore);
            if (!dimmed && levels?.Classify(view.Map.Text) is { } level && palette.GetBrush(LogLevelKinds.Of(level)) is { } levelBrush) text.SetForegroundBrush(levelBrush);
            if (tokens is not null)
                foreach (var token in tokens)
                {
                    int from = view.DisplayAt[Math.Min(token.Start, view.Map.Text.Length)], to = view.DisplayAt[Math.Min(token.End, view.Map.Text.Length)];
                    if (_composed.Count > 0)
                    {
                        (int start, int end) = (from, to);
                        foreach (int at in _composed)
                        {
                            if (start >= at) from += insertLength;
                            if (end > at) to += insertLength;
                        }
                    }
                    if (to > from) Paint(text, palette, token.Kind, from, to - from);
                }
            if (owner.HideAnsi && _composed.Count == 0 && owner.AnsiOf(line, view) is { Spans.Count: > 0 } ansi) DrawAnsi(context, text, view, y, ansi.Spans);
            for (int k = 0; k < _composed.Count; k++) text.SetTextDecorations(TextDecorations.Underline, _composed[k] + k * insertLength, insertLength);
            if (owner._options.ShowWhitespace) DimMarks(text, shown, dim);
            if (reusable is { } current) formattedLine = new(current, text);
            return text;
        }
    }
}
