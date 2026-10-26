using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Files;

public enum FileFailure { NotFound, AlreadyExists, AccessDenied, InUse, DiskFull, InvalidName, Unsupported, Other }

/// <summary>A file operation that did not happen. Nothing is left half done: the source is whole where it was and no partial target remains.</summary>
public sealed class FileOperationException(FileFailure kind, string path, string detail, Exception? inner = null) : IOException(detail, inner)
{
    public FileFailure Kind { get; } = kind;

    /// <summary>The file or folder the operation could not touch.</summary>
    public string Path { get; } = path;
}

/// <summary>Create, duplicate, rename and move of files and folders for the file panel. Every failure is a <see cref="FileOperationException"/> with a reason.</summary>
public static class FileOperations
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The name of a single entry: not empty, no separators or characters the system forbids.</summary>
    public static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\']) >= 0
            || (OperatingSystem.IsWindows() && name.EndsWith('.')))
            throw new FileOperationException(FileFailure.InvalidName, name, name);
        return name;
    }

    public static string CreateFile(string directory, string name)
    {
        string path = Path.Combine(directory, ValidateName(name));
        Run(path, () =>
        {
            if (Exists(path)) throw new FileOperationException(FileFailure.AlreadyExists, path, path);
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        });
        return path;
    }

    public static string CreateFolder(string directory, string name)
    {
        string path = Path.Combine(directory, ValidateName(name));
        Run(path, () =>
        {
            if (Exists(path)) throw new FileOperationException(FileFailure.AlreadyExists, path, path);
            Directory.CreateDirectory(path);
        });
        return path;
    }

    /// <summary>Copies a file or a folder next to the original under "name copy", "name copy 2" and so on. A failed copy leaves nothing behind.</summary>
    public static string Duplicate(string source)
    {
        bool isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source)) throw new FileOperationException(FileFailure.NotFound, source, source);
        string directory = Path.GetDirectoryName(source)!;
        string stem = isDirectory ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
        string extension = isDirectory ? "" : Path.GetExtension(source);
        string target = Path.Combine(directory, stem + " copy" + extension);
        for (int n = 2; Exists(target); n++) target = Path.Combine(directory, $"{stem} copy {n}{extension}");

        string created = target;
        Run(source, () =>
        {
            try
            {
                if (isDirectory) CopyDirectory(source, created);
                else File.Copy(source, created, overwrite: false);
            }
            catch
            {
                Discard(created);
                throw;
            }
        });
        return target;
    }

    /// <summary>Renames within the same folder.</summary>
    public static string Rename(string path, string newName) => Move(path, Path.GetDirectoryName(path)!, ValidateName(newName));

    /// <summary>
    /// Moves a file or a folder into <paramref name="directory"/>, optionally under another name. On one volume this is a single rename. Across volumes a file
    /// is copied next to its target first and the original removed only afterwards; when the original cannot be removed the copy is deleted again.
    /// </summary>
    public static string Move(string path, string directory, string? newName = null)
    {
        bool isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path)) throw new FileOperationException(FileFailure.NotFound, path, path);
        if (!Directory.Exists(directory)) throw new FileOperationException(FileFailure.NotFound, directory, directory);
        string target = Path.Combine(directory, newName ?? Path.GetFileName(path));
        bool caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, target, StringComparison.Ordinal);
        if (string.Equals(path, target, StringComparison.Ordinal)) return target;
        if (!caseOnly && Exists(target)) throw new FileOperationException(FileFailure.AlreadyExists, target, target);
        if (isDirectory && PathMove.IsInside(target, path)) throw new FileOperationException(FileFailure.InvalidName, target, target);

        Run(path, () =>
        {
            if (isDirectory) MoveDirectory(path, target);
            else MoveFile(path, target);
        });
        return target;
    }

    private static void MoveFile(string path, string target)
    {
        try
        {
            File.Move(path, target);
        }
        catch (IOException) when (!SameRoot(path, target))
        {
            string temp = target + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            bool placed = false;
            try
            {
                File.Copy(path, temp, overwrite: false);
                File.Move(temp, target);
                placed = true;
                File.Delete(path);
            }
            catch
            {
                Discard(temp);
                if (placed) Discard(target);
                throw;
            }
        }
    }

    private static void MoveDirectory(string path, string target)
    {
        if (!SameRoot(path, target)) throw new FileOperationException(FileFailure.Unsupported, path, path);
        Directory.Move(path, target);
    }

    private static bool SameRoot(string a, string b) => string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), Comparison);

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the original is untouched; a leftover copy is reported by the failure that brought us here
        }
    }

    private static void Run(string path, Action action)
    {
        try
        {
            action();
        }
        catch (FileOperationException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new FileOperationException(Classify(e), path, e.Message, e);
        }
    }

    /// <summary>Why an exception from the file system happened, from the Win32 error or the errno behind it.</summary>
    public static FileFailure Classify(Exception e)
    {
        if (e is UnauthorizedAccessException) return FileFailure.AccessDenied;
        if (e is FileNotFoundException or DirectoryNotFoundException) return FileFailure.NotFound;
        int code = e.HResult;
        if ((code & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)) code &= 0xFFFF;
        return code switch
        {
            32 or 33 or 16 or 26 => FileFailure.InUse,
            112 or 39 or 28 => FileFailure.DiskFull,
            5 or 13 or 1 => FileFailure.AccessDenied,
            2 or 3 => FileFailure.NotFound,
            80 or 183 or 17 => FileFailure.AlreadyExists,
            _ => FileFailure.Other,
        };
    }
}
