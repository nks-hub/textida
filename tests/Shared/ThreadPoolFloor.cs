using System.Runtime.CompilerServices;

namespace NksHub.Textida.Tests;

/// <summary>
/// A read on a redirected child pipe or an anonymous pipe holds a pool thread until data arrives, and these tests keep dozens of such readers open at once
/// (plugin processes, language servers, in-process pipes). With the default minimum of one thread per core the pool then adds one thread every half second,
/// so the continuation that would answer a handshake or a listener waits for tens of seconds on a busy machine. Threads are created only when work is queued,
/// so a high floor costs nothing on a quiet one.
/// </summary>
internal static class ThreadPoolFloor
{
    private const int Threads = 512;

#pragma warning disable CA2255 // a test assembly, not a library: nothing else loads it
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Raise()
    {
        ThreadPool.GetMinThreads(out int worker, out int io);
        ThreadPool.SetMinThreads(Math.Max(worker, Threads), Math.Max(io, Threads));
    }
}
