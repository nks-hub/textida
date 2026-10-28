using NkspadPlugin;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

// The editor starts this program, talks to it in JSON-RPC on standard input and output, and ends it when it is done with it.
// The limits of the connection are values for a start; the editor applies its own to what it sends and accepts.
return await PluginRunner.RunAsync(new ExamplePlugin(), new RpcLimits(
    MaxMessageBytes: 4 * 1024 * 1024, MaxConcurrentIncoming: 16, MaxPendingOutgoing: 16, MaxResultItems: 100_000, MaxSessionBytes: 64 * 1024 * 1024, MaxOutgoingQueueBytes: 64 * 1024 * 1024));
