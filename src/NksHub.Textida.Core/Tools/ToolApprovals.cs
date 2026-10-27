namespace NksHub.Textida.Core.Tools;

/// <summary>
/// The tools the user has confirmed, kept as the fingerprints of <see cref="ToolInvocation.Fingerprint"/>, one per line in a file of the profile.
/// A tool that is not here, or whose definition changed since, is shown with its command and directory and runs only after a yes.
/// </summary>
public sealed class ToolApprovals
{
    private readonly string _path;
    private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ToolApprovals(string path)
    {
        _path = path;
        try
        {
            foreach (var line in File.ReadAllLines(path)) if (line.Length > 0) _approved.Add(line.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* nothing approved yet */ }
    }

    public bool IsApproved(ToolInvocation tool) => Has(tool.Fingerprint);

    public bool Has(string fingerprint)
    {
        lock (_gate) return _approved.Contains(fingerprint);
    }

    /// <summary>Remembers the approval; a file that cannot be written leaves it valid until the app closes.</summary>
    public void Approve(ToolInvocation tool) => Approve(tool.Fingerprint);

    /// <summary>Remembers another consent kept the same way, such as the default server of a project (<see cref="ServerTarget.ApprovalKey"/>).</summary>
    public void Approve(string fingerprint)
    {
        lock (_gate)
        {
            if (!_approved.Add(fingerprint)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                File.AppendAllText(_path, fingerprint + "\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* asked again after a restart */ }
        }
    }
}
