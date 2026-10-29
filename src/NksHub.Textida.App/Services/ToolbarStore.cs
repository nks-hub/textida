using System.Text;
using NksHub.Textida.Workbench.Toolbar;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The buttons of the user toolbar in <c>toolbar.json</c> of the profile, written through a temporary file like the other profile files. A profile without the file has
/// <see cref="ToolbarLayout.Default"/>; a file that cannot be read gives the default set too, and <see cref="Problem"/> says why, so the toolbar still works and the editor can show the reason.
/// The windows share one store, so a save in the editor reaches every toolbar through <see cref="Changed"/>.
/// </summary>
internal sealed class ToolbarStore(string directory)
{
    public string FilePath { get; } = Path.Combine(directory, ToolbarLayout.FileName);

    /// <summary>Why <c>toolbar.json</c> was not used by the last <see cref="Load"/>; null when it was read or is not there.</summary>
    public string? Problem { get; private set; }

    /// <summary>Raised on the thread that saved, after the file changed.</summary>
    public event Action? Changed;

    /// <summary>The buttons of the profile.</summary>
    public IReadOnlyList<string> Load()
    {
        Problem = null;
        if (!File.Exists(FilePath)) return ToolbarLayout.Default;
        try
        {
            if (ToolbarLayout.TryRead(File.ReadAllText(FilePath, Encoding.UTF8), PluginLimitsPlaceholders.ToolbarMaxButtons, out var buttons, out var problem)) return buttons;
            Problem = problem;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem = e.Message;
        }
        return ToolbarLayout.Default;
    }

    /// <summary>Writes the buttons (cleaned like <see cref="ToolbarLayout.Clean"/>). False with the reason in <paramref name="detail"/> when the file cannot be written.</summary>
    public bool Save(IReadOnlyList<string> buttons, out string detail)
    {
        detail = "";
        var json = ToolbarLayout.Write(ToolbarLayout.Clean(buttons, PluginLimitsPlaceholders.ToolbarMaxButtons));
        try
        {
            Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            detail = e.Message;
            return false;
        }
        Changed?.Invoke();
        return true;
    }
}
