using NksHub.Textida.PluginSdk;

namespace NkspadPlugin;

/// <summary>
/// The example command writes the selections of the document (all of it when nothing is selected) in capital letters. It reads and writes the revision the command started on, so
/// a document that changed in the meantime is a revision conflict and nothing is written; all the edits are one batch, which is one undo step.
/// </summary>
public sealed class ExamplePlugin : PluginBase
{
    /// <summary>The command, as <c>contributes.commands</c> of nkspad-plugin.json names it.</summary>
    public const string ShoutCommand = "example.nkspad-plugin.shout";

    public ExamplePlugin() : base("NkspadPlugin", PluginApi.Version) => OnCommand(ShoutCommand, ShoutAsync);

    private async ValueTask<CommandResult> ShoutAsync(CommandContext command, CancellationToken cancellation)
    {
        var document = DocumentOf(command);
        var info = await document.InfoAsync(cancellation);
        var ranges = (await document.SelectionsAsync(cancellation)).Where(s => !s.IsEmpty).Select(s => (s.Start, s.End)).ToList();
        if (ranges.Count == 0) ranges.Add((info.BomLength, info.ByteLength));

        var edits = new List<RangeEdit>();
        foreach (var (start, end) in ranges)
        {
            var read = await document.ReadTextAsync(start, end, cancellation);
            if (read.HasInvalid) return new CommandResult("failed", ["The range holds bytes that are not valid in the encoding of the file."]);
            var shouted = read.Text.ToUpperInvariant();
            if (shouted != read.Text) edits.Add(new RangeEdit(start, end, shouted));
        }
        if (edits.Count == 0) return new CommandResult("unchanged");

        await document.ApplyAsync(edits, null, cancellation);
        return new CommandResult("applied");
    }
}
