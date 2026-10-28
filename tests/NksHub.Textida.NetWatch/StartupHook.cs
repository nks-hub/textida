using NksHub.Textida.NetWatch;

/// <summary>The entry point the runtime calls in every process started with this assembly in <c>DOTNET_STARTUP_HOOKS</c>. The type has to be in the global namespace.</summary>
internal class StartupHook
{
    public static void Initialize()
    {
        // a hook that throws takes the process down with it, and the process under test must start
        try { NetWatchHook.Start(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
