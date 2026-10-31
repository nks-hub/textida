namespace NksHub.Textida.App.Services;

/// <summary>The encodings the user picked last, newest first, kept in the profile so the picker lists them on top after a restart.</summary>
internal sealed class RecentEncodings(string file, int capacity = 6)
{
    private List<string>? _names;

    /// <summary>The names of the encodings picked last, newest first.</summary>
    public IReadOnlyList<string> Names => Load();

    /// <summary>Puts an encoding on top of the list.</summary>
    public void Used(string name)
    {
        var names = Load();
        names.Remove(name);
        names.Insert(0, name);
        if (names.Count > capacity) names.RemoveRange(capacity, names.Count - capacity);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, names);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the list holds for this run */ }
    }

    private List<string> Load()
    {
        if (_names is not null) return _names;
        try { _names = File.Exists(file) ? [.. File.ReadAllLines(file).Where(l => l.Length > 0).Distinct().Take(capacity)] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _names = []; }
        return _names;
    }
}
