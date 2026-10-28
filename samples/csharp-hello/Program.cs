using System.Text.Json.Serialization;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

// Sample plugin on the C# SDK. Publish: dotnet publish -c Release -r win-x64
// The limits are sample values; the editor's own limits are an owner decision.
return await PluginRunner.RunAsync(new HelloPlugin(), new RpcLimits(4 * 1024 * 1024, 16, 16, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));

sealed class HelloPlugin : PluginBase
{
    public HelloPlugin() : base("csharp-hello", PluginApi.Version)
    {
        On("hello/greet", HelloJson.Default.GreetRequest, HelloJson.Default.GreetResult, (request, _) => ValueTask.FromResult(new GreetResult($"Hello, {request.Name}!")));
        OnCommand("example.csharp-hello.shout", ShoutAsync);
    }

    /// <summary>Writes the selections of the document (all of it when nothing is selected) in capital letters, as one undo step. It reads and writes the revision the command started on.</summary>
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

sealed record GreetRequest(string Name);
sealed record GreetResult(string Greeting);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResult))]
sealed partial class HelloJson : JsonSerializerContext;
