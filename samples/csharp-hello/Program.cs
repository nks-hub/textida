using System.Text.Json.Serialization;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

// Sample plugin on the C# SDK. Publish: dotnet publish -c Release -r win-x64
// The limits are sample values; the editor's own limits are an owner decision.
return await PluginRunner.RunAsync(new HelloPlugin(), new RpcLimits(4 * 1024 * 1024, 16, 16, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));

sealed class HelloPlugin : PluginBase
{
    public HelloPlugin() : base("csharp-hello", PluginApi.Version) =>
        On("hello/greet", HelloJson.Default.GreetRequest, HelloJson.Default.GreetResult, (request, _) => ValueTask.FromResult(new GreetResult($"Hello, {request.Name}!")));
}

sealed record GreetRequest(string Name);
sealed record GreetResult(string Greeting);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResult))]
sealed partial class HelloJson : JsonSerializerContext;
