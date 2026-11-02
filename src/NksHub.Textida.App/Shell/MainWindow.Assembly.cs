using System.Text;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Dotnet;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A .NET assembly opened as decompiled C# (PZ.5.4). The tab is a read-only virtual document named after the file with <c>.cs</c>, so it takes the C# colours and can never be saved over the assembly. It first says that it is working
/// and gets the text when the decompiler is done, which happens off the interface thread. The setting <c>files.decompileAssemblies</c> turns this off; the file then opens as any binary file does.
/// </summary>
internal sealed partial class MainWindow
{
    private static readonly string[] AssemblyExtensions = [".dll", ".exe", ".winmd"];
    private readonly Dictionary<string, DocTab> _assemblyTabs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The finished decompilations of this window, for tests to wait on.</summary>
    internal Task AssemblyWork { get; private set; } = Task.CompletedTask;

    /// <summary>The tab of the decompiled assembly when <paramref name="path"/> is a managed assembly and the setting allows it; null when the file is to open the usual way.</summary>
    private DocTab? OpenAssembly(string path)
    {
        if (!AssemblyExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || !_app.Workbench.Settings.GetBool("files.decompileAssemblies") || !AssemblyDecompiler.IsManaged(path)) return null;
        if (_assemblyTabs.TryGetValue(path, out var open) && open.Group is not null)
        {
            SelectTab(open);
            return open;
        }
        string name = Path.GetFileName(path);
        var document = Document.CreateVirtual(name + ".cs", Encoding.UTF8.GetBytes(Loc.Get(K.AssemblyWorking, ("name", name)).Insert(0, "// ")), AvaloniaEditBackendFactory.Instance, TextCodec.Utf8);
        var tab = OpenVirtualDocument(document, Loc.Get(K.AssemblyTitle, ("name", name)));
        _assemblyTabs[path] = tab;
        var closed = new CancellationTokenSource(); // closing the tab stops the decompiler; the time limit is only for a tab that stays open
        _ = tab.Closed.Task.ContinueWith(_ => closed.Cancel(), TaskScheduler.Default);
        AssemblyWork = DecompileIntoAsync(path, name, document, tab.Closed.Task, closed.Token);
        return tab;
    }

    private async Task DecompileIntoAsync(string path, string name, Document document, Task tabClosed, CancellationToken closed)
    {
        string text;
        try
        {
            var result = await Task.Run(() => AssemblyDecompiler.Decompile(path, null, closed), CancellationToken.None);
            text = result.Text + (result.Truncated ? Environment.NewLine + "// " + Loc.Get(K.AssemblyTruncated, ("done", result.TypesDone), ("total", result.TypesTotal)) + Environment.NewLine : "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or BadImageFormatException or InvalidOperationException or NotSupportedException)
        {
            text = "// " + Loc.Get(K.AssemblyFailed, ("name", name), ("reason", e is OperationCanceledException ? Loc.Get(K.AssemblyTooSlow) : e.Message));
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (tabClosed.IsCompleted) return; // the tab was closed: its document is gone, nothing is shown
            document.ApplyVirtual([new ByteEdit(new ByteRange(0, document.ByteLength), Encoding.UTF8.GetBytes(text))], null, "decompile");
        });
    }
}
