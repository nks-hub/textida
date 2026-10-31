// Publishes the sample plugin samples/csharp-hello (the internal C# SDK, as the plugin template uses it) with NativeAOT for one RID and runs the plugin validator on the result:
// the manifest, the permissions, the native program, the handshake through the plugin host and a recorded session that must run as recorded.
//
//   dotnet run tools/CheckSamplePlugin.cs -- [--rid RID] [--dotnet PATH]
//
// NativeAOT links with the tool chain of the system it runs on, so the RID must be one this system can publish (win-x64 on Windows, osx-arm64 or osx-x64 on a Mac, linux-x64 on Linux).
using System.Diagnostics;

var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i++)
{
    if (i + 1 < args.Length) options[args[i]] = args[++i];
    else
    {
        Console.Error.WriteLine($"CheckSamplePlugin: {args[i]} needs a value.");
        return 2;
    }
}

var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var root = Path.GetFullPath(Path.Combine(tools, ".."));
var dotnet = options.GetValueOrDefault("--dotnet", "dotnet");
var rid = options.GetValueOrDefault("--rid", "");
if (rid.Length == 0)
{
    Console.Error.WriteLine("CheckSamplePlugin: --rid is required.");
    return 2;
}

const string Session = """
    {
      "documents": [{ "name": "a.txt", "text": "hello world\n", "selections": [[0, 5]] }],
      "steps": [
        { "kind": "command", "name": "example.csharp-hello.shout", "document": "a.txt", "status": "applied" },
        { "kind": "expectText", "document": "a.txt", "text": "HELLO world\n" }
      ]
    }
    """;

var work = Path.Combine(Path.GetTempPath(), "textida-sample-plugin-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var sample = Path.Combine(root, "samples", "csharp-hello");
    var folder = Path.Combine(work, "plugin");
    Run(dotnet, "publish", sample, "-c", "Release", "-r", rid, "-o", folder, "--nologo");

    var program = "NksHub.Textida.Samples.CsHello" + (rid.StartsWith("win", StringComparison.Ordinal) ? ".exe" : "");
    if (!File.Exists(Path.Combine(folder, program))) throw new InvalidOperationException($"The publish for {rid} has no {program}.");
    var manifest = File.ReadAllText(Path.Combine(sample, "nkspad-plugin.json")).Replace("/path/to/NksHub.Textida.Samples.CsHello", "./" + program);
    if (!manifest.Contains("./" + program, StringComparison.Ordinal)) throw new InvalidOperationException("The manifest of the sample has no executable placeholder to fill in.");
    File.WriteAllText(Path.Combine(folder, "nkspad-plugin.json"), manifest);
    var scenario = Path.Combine(work, "session.json");
    File.WriteAllText(scenario, Session);

    Run(dotnet, "run", "--project", Path.Combine(root, "tools", "NksHub.Textida.Plugin.Validate"), "-c", "Release", "--",
        folder, "--run", "--scenario", scenario, "--source", sample);
    Console.WriteLine($"CheckSamplePlugin: the sample plugin published for {rid} passed the validator.");
    return 0;
}
finally
{
    try { Directory.Delete(work, recursive: true); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the temp folder of the runner is cleared anyway */ }
}

static void Run(string file, params string[] arguments)
{
    var info = new ProcessStartInfo(file) { UseShellExecute = false };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} did not start.");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{file} {string.Join(' ', arguments.Take(3))} ended with {process.ExitCode}.");
}
