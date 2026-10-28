using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests;

internal static class T
{
    /// <summary>The dotnet host that runs a plugin that is a managed assembly; a manifest does not take a bare name, the test host's process start does.</summary>
    public static string DotnetHost => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    /// <summary>The sample plugin <c>samples/csharp-hello</c>, built next to the tests.</summary>
    public static string SampleDll
    {
        get
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Samples.CsHello.dll");
            Assert.True(File.Exists(dll), dll);
            return dll;
        }
    }

    /// <summary>The full path of the dotnet host: a manifest names a program by its path, never by a bare name.</summary>
    public static string DotnetFullPath
    {
        get
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (host is not null && File.Exists(host)) return host;
            var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("The dotnet host was not found.");
        }
    }

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    public static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "textida-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The editor with no plugin, driven by the test; the plugin holds exactly <paramref name="grants"/>.</summary>
    public static Task<TestEditor> RawEditor(params string[] grants) => TestEditor.StartRawAsync(new TestEditorOptions { Grants = grants });

    public static Task<TestEditor> RawEditor(TestEditorOptions options) => TestEditor.StartRawAsync(options);

    /// <summary>The error a call ends with; fails the test when the call succeeds.</summary>
    public static async Task<RpcException> ErrorOf(Func<Task> call)
    {
        try { await call(); }
        catch (RpcException e) { return e; }
        throw new Xunit.Sdk.XunitException("The call succeeded; an error was expected.");
    }

    /// <summary>A member of the <c>data</c> of an error, as text.</summary>
    public static string? Data(RpcException error, string member)
    {
        if (error.DataJson is null) return null;
        using var data = JsonDocument.Parse(error.DataJson);
        return data.RootElement.TryGetProperty(member, out var value) ? value.ToString() : null;
    }

    public static async Task Until(Func<bool> condition, int timeoutMs = 15_000)
    {
        var stop = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > stop) throw new TimeoutException("Condition not reached.");
            await Task.Delay(10);
        }
    }

    public static string Doc(TestDocument document, string rest = "") => $"{{\"document\":\"{document.Id}\",\"revision\":{document.Revision}{(rest.Length > 0 ? "," + rest : "")}}}";
}

/// <summary>A small plugin on the public SDK: uppercases the selections of a document, shows notices and remembers the events it heard.</summary>
internal sealed class UpperPlugin : PluginBase
{
    private readonly List<string> _events = [];

    public UpperPlugin() : base("upper", PluginApi.Version)
    {
        OnCommand("upper.run", async (command, ct) =>
        {
            var document = DocumentOf(command);
            var selections = await document.SelectionsAsync(ct);
            var edits = new List<RangeEdit>();
            foreach (var selection in selections.Where(s => !s.IsEmpty))
                edits.Add(new RangeEdit(selection.Start, selection.End, (await document.ReadTextAsync(selection.Start, selection.End, ct)).Text.ToUpperInvariant()));
            if (edits.Count == 0) return new CommandResult("unchanged");
            await document.ApplyAsync(edits, null, ct);
            return new CommandResult("applied");
        });
        OnCommand("upper.propose", async (command, ct) =>
        {
            var document = DocumentOf(command);
            var info = await document.InfoAsync(ct);
            var text = await document.ReadTextAsync(info.BomLength, info.ByteLength, ct);
            await document.ProposeAsync("Uppercase", [new RangeEdit(info.BomLength, info.ByteLength, text.Text.ToUpperInvariant())], ct);
            return new CommandResult("proposed");
        });
        OnCommand("upper.greet", async (command, ct) =>
        {
            await NotifyAsync(NoticeLevel.Info, "Hello, " + (command.String("who") ?? "world") + "!", ct);
            return new CommandResult("greeted");
        });
    }

    public IReadOnlyList<string> Events { get { lock (_events) return [.. _events]; } }

    protected override ValueTask OnEventAsync(JsonElement eventPayload)
    {
        lock (_events) _events.Add(eventPayload.GetProperty("name").GetString() ?? "");
        return ValueTask.CompletedTask;
    }
}
