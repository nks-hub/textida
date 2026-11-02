using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

namespace NksHub.Textida.Core.Dotnet;

/// <summary>
/// What the decompiler may spend on one assembly. A module with more top-level types than <see cref="WholeModuleTypes"/> is decompiled type by type until <see cref="MaxChars"/> or <see cref="MaxTime"/> is reached.
/// <see cref="MaxTime"/> bounds work that closing the tab does not stop; System.Collections decompiles whole in about 3 s on the 2.4 GHz test machine with nothing else running.
/// </summary>
public sealed record DecompileLimits(long MaxFileBytes = 256L << 20, int WholeModuleTypes = 300, int MaxChars = 8_000_000, TimeSpan? Time = null)
{
    public TimeSpan MaxTime => Time ?? TimeSpan.FromSeconds(60);
    public static DecompileLimits Default { get; } = new();
}

/// <summary>The C# text of an assembly; <see cref="Truncated"/> says that types were left out, <see cref="TypesDone"/> of <see cref="TypesTotal"/> were written.</summary>
public sealed record DecompiledAssembly(string Text, bool Truncated, int TypesDone, int TypesTotal);

/// <summary>
/// Shows a .NET assembly as C# (PZ.5.4) with ICSharpCode.Decompiler (MIT). The assembly is only read; no code of it is loaded or run, and references are not resolved over the network
/// (a reference that is not next to the file or in the runtime folder is left as a name). The library is trimmed by the native compiler with the rest of the editor; the build checks that it still decompiles.
/// </summary>
public static class AssemblyDecompiler
{
    /// <summary>True when the file is a PE image with .NET metadata (a managed .dll, .exe or .winmd); false for anything else, including a file that cannot be read.</summary>
    public static bool IsManaged(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            return pe.HasMetadata && pe.GetMetadataReader().IsAssembly;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { return false; }
    }

    /// <summary>Decompiles the assembly. Throws <see cref="InvalidDataException"/> for a file that is too large or not a .NET assembly, and <see cref="OperationCanceledException"/> when cancelled or when a module decompiled as a whole takes longer than the limit.</summary>
    public static DecompiledAssembly Decompile(string path, DecompileLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= DecompileLimits.Default;
        if (new FileInfo(path).Length > limits.MaxFileBytes) throw new InvalidDataException("The assembly is too large to decompile.");
        if (!IsManaged(path)) throw new InvalidDataException("The file is not a .NET assembly.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limits.MaxTime);
        var settings = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
        var decompiler = new CSharpDecompiler(path, settings) { CancellationToken = timeout.Token };
        var types = decompiler.TypeSystem.MainModule.TypeDefinitions.Where(t => t.DeclaringType is null && t.Name != "<Module>").OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        if (types.Count <= limits.WholeModuleTypes)
        {
            string whole = decompiler.DecompileWholeModuleAsString();
            return whole.Length > limits.MaxChars ? new DecompiledAssembly(whole[..limits.MaxChars], true, types.Count, types.Count) : new DecompiledAssembly(whole, false, types.Count, types.Count);
        }
        var text = new StringBuilder();
        int done = 0;
        try
        {
            text.Append(decompiler.DecompileModuleAndAssemblyAttributesToString());
            foreach (var type in types)
            {
                if (text.Length >= limits.MaxChars || timeout.IsCancellationRequested) break;
                ct.ThrowIfCancellationRequested();
                string part = decompiler.DecompileTypesAsString([(TypeDefinitionHandle)type.MetadataToken]);
                text.AppendLine().Append(part);
                done++;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // the time limit ran out inside a type: the types written before it are the result, cut like one cut by its length
        }
        ct.ThrowIfCancellationRequested();
        return new DecompiledAssembly(text.ToString(), done < types.Count, done, types.Count);
    }
}
