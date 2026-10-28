using System.Diagnostics;

namespace NksHub.Textida.Rpc.Tests;

public class ProcessTests
{
    [Fact]
    public async Task ASlowConsumerHoldsBackTheSenderAtTheQueueLimitAndLosesNothing()
    {
        var psi = new ProcessStartInfo(T.DotnetHost) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { T.PluginDll, "--sleep-before-read", "2500" }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        try
        {
            var payload = T.Json("\"" + new string('s', 100_000) + "\"");
            var size = RpcWire.Notification("sink", payload).Length;
            var limits = T.Roomy with { MaxOutgoingQueueBytes = size * 7 / 2 };
            await using var rpc = new JsonRpcDispatcher(proc.StandardOutput.BaseStream, proc.StandardInput.BaseStream, RpcProtocols.Plugin, limits, new DelegateHandler());

            var sends = Enumerable.Range(0, 40).Select(_ => rpc.NotifyAsync("sink", payload)).ToArray();
            // checked at once: 40 messages of this size cannot all be queued before the plugin has read any, and a pause here would let a slow machine wait out the plugin's sleep
            Assert.True(sends.Count(s => !s.IsCompleted) > 0, "the sender should be held back while the plugin does not read");
            Assert.True(rpc.QueuedOutgoingBytes <= size * 7 / 2);

            await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(60));
            await T.Until(() => rpc.QueuedOutgoingBytes == 0);
            long count = 0;
            await T.Until(() =>
            {
                var stats = rpc.RequestAsync("stats").GetAwaiter().GetResult();
                count = stats.GetProperty("sinkCount").GetInt64();
                return count == 40;
            }, 30_000);
            Assert.Equal(40, count);
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
    }
}
