using NksHub.Textida.Core.Dotnet;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.5.4: a .NET assembly as C#; the file is only read.</summary>
public class AssemblyDecompilerTests
{
    private static string Assembly => typeof(System.Collections.Generic.SortedSet<int>).Assembly.Location;

    [Fact]
    public void A_managed_assembly_is_recognised_and_other_files_are_not()
    {
        Assert.True(AssemblyDecompiler.IsManaged(Assembly));
        Assert.True(AssemblyDecompiler.IsManaged(typeof(AssemblyDecompilerTests).Assembly.Location));
        string text = Path.GetTempFileName();
        try
        {
            File.WriteAllText(text, "not an assembly");
            Assert.False(AssemblyDecompiler.IsManaged(text));
            Assert.False(AssemblyDecompiler.IsManaged(text + ".missing"));
            Assert.Throws<InvalidDataException>(() => AssemblyDecompiler.Decompile(text));
        }
        finally { File.Delete(text); }
    }

    [Fact]
    public void A_module_is_decompiled_into_C_sharp_with_its_types()
    {
        var result = AssemblyDecompiler.Decompile(Assembly, DecompileLimits.Default with { WholeModuleTypes = 100_000 });

        Assert.False(result.Truncated);
        Assert.Contains("class SortedSet<T>", result.Text);
        Assert.Contains("namespace System.Collections.Generic", result.Text);
        Assert.Contains("public bool Contains(T item)", result.Text);
    }

    [Fact]
    public void A_big_module_is_written_type_by_type_and_cut_at_the_limit_with_the_count()
    {
        var result = AssemblyDecompiler.Decompile(Assembly, DecompileLimits.Default with { WholeModuleTypes = 1, MaxChars = 4000 });

        Assert.True(result.Truncated);
        Assert.InRange(result.TypesDone, 1, result.TypesTotal - 1);
        Assert.Contains("class ", result.Text);
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused_and_a_cancelled_run_stops()
    {
        Assert.Throws<InvalidDataException>(() => AssemblyDecompiler.Decompile(Assembly, DecompileLimits.Default with { MaxFileBytes = 10 }));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AssemblyDecompiler.Decompile(Assembly, DecompileLimits.Default with { WholeModuleTypes = 1 }, cancelled.Token));
    }
}
