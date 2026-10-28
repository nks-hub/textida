namespace NksHub.Textida.Plugin.Tests;

internal static class PublicApiBaseline
{
    /// <summary>The types of the connection layer that are part of the API of the SDK package; the other public types of that assembly serve the protocols of the editor itself.</summary>
    private static readonly string[] RpcPromised =
    [
        "JsonRpcDispatcher", "IRpcHandler", "RpcException", "RpcErrorCodes", "RpcLimits", "RpcId", "RpcConnectionClosedException", "RpcTimeoutException", "RpcFramingException", "RpcRequestContext",
    ];

    /// <summary>The path of <c>PublicApi.txt</c>, next to the SDK project.</summary>
    public static string FilePath() => Path.Combine(T.RepoRoot(), "src", "NksHub.Textida.Plugin.Sdk", "PublicApi.txt");

    /// <summary>What the package promises today, as the lines of <c>PublicApi.txt</c>.</summary>
    public static List<string> Current()
    {
        var lines = new List<string> { "# NksHub.Textida.Plugin.Sdk" };
        lines.AddRange(PublicApi.Describe(typeof(NksHub.Textida.PluginSdk.PluginBase).Assembly));
        lines.Add("# NksHub.Textida.Rpc (the connection layer that ships in the package; only these types are part of the API)");
        lines.AddRange(PublicApi.Describe(typeof(NksHub.Textida.Rpc.JsonRpcDispatcher).Assembly, t => RpcPromised.Contains(t.Name)));
        return lines;
    }
}
