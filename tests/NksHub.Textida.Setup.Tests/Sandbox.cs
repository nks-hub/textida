using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using NksHub.Textida.Install;
using NksHub.Textida.Setup;

namespace NksHub.Textida.Setup.Tests;

/// <summary>
/// A throw-away world for the install logic: a temp folder for the program, the Start menu, the desktop and the settings, and <c>HKCU\Software\Textida.Tests\{guid}</c> for the classes key and the Apps list.
/// The real registry, the real Start menu and the real profile are never opened.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Sandbox : IDisposable
{
    private readonly string _keyPath = @"Software\Textida.Tests\" + Guid.NewGuid().ToString("N");
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "textida-setup-test-" + Guid.NewGuid().ToString("N"));
    public RegistryKey Key { get; }
    public SystemLocations Locations { get; }
    public string Dir => Path.Combine(Root, "installed");
    public string Exe => Path.Combine(Dir, "Textida.exe");
    public bool Running { get; set; }

    public Sandbox()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry and the shortcuts exist on Windows only");
        Directory.CreateDirectory(Root);
        Key = Registry.CurrentUser.CreateSubKey(_keyPath);
        Locations = new SystemLocations
        {
            OpenClasses = () => Key.CreateSubKey("Classes"),
            UninstallKeyPath = _keyPath + @"\Uninstall",
            StartMenuDir = Path.Combine(Root, "start"),
            DesktopDir = Path.Combine(Root, "desktop"),
            UserDataDir = Path.Combine(Root, "appdata", "Textida"),
            DefaultInstallDir = Path.Combine(Root, "default"),
            NotifyExplorer = false,
        };
    }

    public Installer Installer(Action<SetupStage, double>? progress = null) => new(Locations) { IsRunning = _ => Running, Progress = progress };

    /// <summary>The world as the setup sees it: this sandbox, a package built from <paramref name="package"/>, and the actions recorded instead of done.</summary>
    internal SetupEnvironment Env(Recorder recorder, string language = "en", Func<Stream?>? package = null, string? setupExe = null) => new()
    {
        Locations = Locations,
        OpenPackage = package ?? (() => Package()),
        SetupExe = setupExe ?? FakeSetup(),
        UiLanguage = language,
        IsRunning = _ => Running,
        Launch = (exe, dir) => recorder.Launched.Add((exe, dir)),
        RemoveSelf = (program, dir) => recorder.Removed.Add((program, dir)),
        WaitForProcess = pid => { recorder.Waited.Add(pid); return true; },
    };

    public RegistryKey? UninstallEntry() => Registry.CurrentUser.OpenSubKey(_keyPath + @"\Uninstall");

    public InstallOptions Options(bool startMenu = true, bool desktop = false, bool menu = true, string[]? extensions = null, string? setupExe = null, string? dir = null) =>
        new(dir ?? Dir, startMenu, desktop, menu, extensions ?? [], "Open with Textida", setupExe);

    /// <summary>The ZIP of a package: the program, a plugin file, the scripts of the ZIP form, and the version.</summary>
    public static MemoryStream Package(string version = "1.0.0", string content = "old", params (string Name, string Content)[] extra)
    {
        (string Name, string Content)[] files =
        [
            ("Textida.exe", "program " + content), ("plugins/json/plugin.txt", content), ("Setup.ps1", "script"), ("Install.cmd", "cmd"), ("version.txt", version), .. extra,
        ];
        return Zip(files);
    }

    public static MemoryStream Zip(params (string Name, string Content)[] files)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>A stand-in for TextidaSetup.exe: some program bytes with a package appended, as the build makes it.</summary>
    public string FakeSetup(string name = "TextidaSetup.exe")
    {
        var program = Path.Combine(Root, "base-" + name);
        File.WriteAllText(program, new string('P', 5000));
        var package = Path.Combine(Root, "payload-" + name + ".zip");
        File.WriteAllBytes(package, Package().ToArray());
        var setup = Path.Combine(Root, name);
        PayloadFile.Append(program, package, setup);
        return setup;
    }

    public void Add(string relative, string content)
    {
        var path = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string Read(string relative) => File.ReadAllText(Path.Combine(Dir, relative));

    /// <summary>Every key, value and kind below the classes key, sorted: two snapshots are equal when nothing was changed.</summary>
    public List<string> ClassesSnapshot()
    {
        var lines = new List<string>();
        using var classes = Key.CreateSubKey("Classes");
        Walk(classes, "", lines);
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static void Walk(RegistryKey key, string prefix, List<string> lines)
    {
        foreach (var name in key.GetValueNames())
            lines.Add($"{prefix}|{name}|{key.GetValueKind(name)}|{(key.GetValue(name) is byte[] b ? Convert.ToHexString(b) : key.GetValue(name))}");
        foreach (var sub in key.GetSubKeyNames())
        {
            lines.Add(prefix + @"\" + sub);
            using var child = key.OpenSubKey(sub)!;
            Walk(child, prefix + @"\" + sub, lines);
        }
    }

    public void Dispose()
    {
        Key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        try { Directory.Delete(Root, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
