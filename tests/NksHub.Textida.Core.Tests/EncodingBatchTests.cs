using System.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.5.2: batch conversion of the encoding of many files with a preview of what is lost, a journal and recovery.</summary>
public class EncodingBatchTests
{
    static EncodingBatchTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private sealed class Crash : Exception;

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string Make(string dir, string name, byte[] content)
    {
        string path = Path.GetFullPath(Path.Combine(dir, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static EncodingBatchOptions To(TextCodec target) => new() { Target = target };

    /// <summary>100 files; every 14th holds an emoji that Windows-1250 cannot write.</summary>
    private static (string[] Paths, HashSet<string> Lossy) Hundred(string dir)
    {
        var paths = new string[100];
        var lossy = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            bool bad = i % 14 == 0;
            string text = $"Soubor {i}\r\nPříliš žluťoučký kůň úpěl ďábelské ódy {i}\r\n" + (bad ? "😀\r\n" : "");
            paths[i] = Make(dir, $"sub{i % 4}/f{i:000}.txt", Utf8(text));
            if (bad) lossy.Add(paths[i]);
        }
        return (paths, lossy);
    }

    [Fact]
    public void A_hundred_files_show_the_lossy_ones_before_anything_is_written()
    {
        using var dir = new JournalTempDir();
        var (paths, lossy) = Hundred(dir.Path);
        var before = paths.ToDictionary(p => p, File.ReadAllBytes);

        var plan = EncodingBatch.Plan(dir.Path, To(TextCodec.Cp1250));

        Assert.Equal(100, plan.Items.Count);
        Assert.Equal(lossy.Order(), plan.Lossy.Select(i => i.Path).Order());
        Assert.Equal(100 - lossy.Count, plan.Clean.Count());
        Assert.All(plan.Lossy, i => { Assert.Equal(1, i.Loss.Total); Assert.Equal(0x1F600, i.Loss.Entries[0].CodePoint); Assert.Equal(TextCodec.Utf8, i.Source); });
        Assert.All(plan.Clean, i => Assert.True(i.NewSize < i.Size));
        foreach (var p in paths) Assert.Equal(before[p], File.ReadAllBytes(p)); // the preview wrote nothing
    }

    [Fact]
    public void Run_converts_the_clean_files_and_leaves_the_lossy_ones_until_a_replacement_is_chosen()
    {
        using var dir = new JournalTempDir();
        var (paths, lossy) = Hundred(dir.Path);
        var before = paths.ToDictionary(p => p, File.ReadAllBytes);
        var plan = EncodingBatch.Plan(dir.Path, To(TextCodec.Cp1250));
        string journal = dir.Combine("journal");
        var cp1250 = Encoding.GetEncoding(1250);

        var result = EncodingBatch.Run(plan, replacement: null, journal);

        Assert.Equal(100 - lossy.Count, result.Converted);
        Assert.Equal(lossy.Count, result.NotRun.Count(i => i.Skip == BatchSkip.Lossy));
        foreach (var p in paths)
            Assert.Equal(lossy.Contains(p) ? before[p] : cp1250.GetBytes(NormalText(p)), File.ReadAllBytes(p));
        Assert.Empty(BatchRecovery.Find(journal));

        var again = EncodingBatch.Run(EncodingBatch.Plan(paths.Where(lossy.Contains).ToList(), To(TextCodec.Cp1250)), "?", journal);
        Assert.Equal(lossy.Count, again.Converted);
        foreach (var p in lossy)
            Assert.Equal(cp1250.GetBytes(NormalText(p) + "?\r\n"), File.ReadAllBytes(p));
    }

    private static string NormalText(string path)
    {
        int i = int.Parse(Path.GetFileNameWithoutExtension(path)[1..]);
        return $"Soubor {i}\r\nPříliš žluťoučký kůň úpěl ďábelské ódy {i}\r\n";
    }

    public static IEnumerable<object[]> CrashPoints()
    {
        yield return ["plan"];
        for (int i = 0; i < 5; i++)
        {
            yield return [$"before-swap:{i}"];
            yield return [$"after-swap:{i}"];
            yield return [$"after-file:{i}"];
        }
        yield return ["before-end"];
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public void A_batch_cut_off_at_any_point_can_be_finished_or_rolled_back(string point)
    {
        using var dir = new JournalTempDir();
        var paths = Enumerable.Range(0, 5).Select(i => Make(dir.Path, $"f{i}.txt", Utf8($"Příliš žluťoučký kůň {i}\n"))).ToArray();
        var original = paths.ToDictionary(p => p, File.ReadAllBytes);
        var cp1250 = Encoding.GetEncoding(1250);
        string journal = dir.Combine("journal");
        var plan = EncodingBatch.Plan(paths, To(TextCodec.Cp1250));
        Assert.Throws<Crash>(() => EncodingBatch.Run(plan, null, journal, faultPoint: p => { if (p == point) throw new Crash(); }));

        string copy = dir.Combine("copy");
        Directory.CreateDirectory(copy);
        foreach (var f in paths.Concat(Directory.GetFiles(journal))) File.Copy(f, Path.Combine(copy, Path.GetFileName(f)), overwrite: true);

        using (var pending = Assert.Single(BatchRecovery.Find(journal)))
            Assert.All(pending.Finish(), o => Assert.Equal(ChangeStatus.Applied, o.Status));
        foreach (var p in paths) Assert.Equal(cp1250.GetBytes(Encoding.UTF8.GetString(original[p])), File.ReadAllBytes(p));

        // roll back from the interrupted state
        foreach (var p in paths) File.Copy(Path.Combine(copy, Path.GetFileName(p)), p, overwrite: true);
        foreach (var f in Directory.GetFiles(copy).Where(f => f.EndsWith(".njl") || f.EndsWith(".lock"))) File.Copy(f, Path.Combine(journal, Path.GetFileName(f)), overwrite: true);
        using (var pending = Assert.Single(BatchRecovery.Find(journal)))
            Assert.All(pending.RollBack(), o => Assert.NotEqual(ChangeStatus.Failed, o.Status));
        foreach (var p in paths) Assert.Equal(original[p], File.ReadAllBytes(p));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public void Binary_unchanged_and_uncertain_files_are_skipped_with_their_reason()
    {
        using var dir = new JournalTempDir();
        string binary = Make(dir.Path, "b.bin", [.. Enumerable.Range(0, 64).Select(i => (byte)(i % 7 == 0 ? 0 : 0x80 + i))]);
        string ascii = Make(dir.Path, "a.txt", "plain ascii\n"u8.ToArray());
        string guess = Make(dir.Path, "g.txt", [0x61, 0xE8, 0x62]); // one high byte: the 8-bit page is a guess
        string real = Make(dir.Path, "r.txt", Utf8("příliš\n"));

        var plan = EncodingBatch.Plan([binary, ascii, guess, real], To(TextCodec.Cp1250));

        Assert.Equal(BatchSkip.Binary, plan.Items[0].Skip);
        Assert.Equal(BatchSkip.Unchanged, plan.Items[1].Skip);
        Assert.Equal(BatchSkip.UncertainEncoding, plan.Items[2].Skip);
        Assert.Equal(BatchSkip.None, plan.Items[3].Skip);
        var result = EncodingBatch.Run(plan, null, null);
        Assert.Equal(1, result.Converted);
        Assert.Equal([0x61, 0xE8, 0x62], File.ReadAllBytes(guess)); // never converted from a guess

        // naming the source encoding takes the guess out of it
        var forced = EncodingBatch.Plan([guess], new EncodingBatchOptions { Target = TextCodec.Utf8, Source = TextCodec.Cp1250 });
        Assert.Equal(BatchSkip.None, forced.Items[0].Skip);
        Assert.Equal(1, EncodingBatch.Run(forced, null, null).Converted);
        Assert.Equal(Utf8("ačb"), File.ReadAllBytes(guess));
    }

    [Fact]
    public void A_file_that_changed_after_the_preview_is_a_conflict_and_stays_as_it_is()
    {
        using var dir = new JournalTempDir();
        string a = Make(dir.Path, "a.txt", Utf8("řeč\n"));
        string b = Make(dir.Path, "b.txt", Utf8("řeč\n"));
        var plan = EncodingBatch.Plan([a, b], To(TextCodec.Cp1250));
        File.WriteAllBytes(b, Utf8("řeč changed\n"));

        var result = EncodingBatch.Run(plan, null, null);

        Assert.Equal(1, result.Converted);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(Utf8("řeč changed\n"), File.ReadAllBytes(b));
        Assert.Equal(Encoding.GetEncoding(1250).GetBytes("řeč\n"), File.ReadAllBytes(a));
    }

    [Fact]
    public void Masks_limit_the_files_and_an_excluded_file_is_left_alone()
    {
        using var dir = new JournalTempDir();
        string txt = Make(dir.Path, "a.txt", Utf8("řeč\n"));
        string md = Make(dir.Path, "b.md", Utf8("řeč\n"));
        string skip = Make(dir.Path, "c.txt", Utf8("řeč\n"));
        var plan = EncodingBatch.Plan(dir.Path, To(TextCodec.Cp1250) with { Masks = ["*.txt"] });
        Assert.Equal([txt, skip], plan.Items.Select(i => i.Path).Order());

        var result = EncodingBatch.Run(plan, null, null, exclude: new HashSet<string> { skip });

        Assert.Equal(1, result.Converted);
        Assert.Equal(Utf8("řeč\n"), File.ReadAllBytes(md));
        Assert.Equal(Utf8("řeč\n"), File.ReadAllBytes(skip));
        Assert.Equal(Encoding.GetEncoding(1250).GetBytes("řeč\n"), File.ReadAllBytes(txt));
    }

    [Fact]
    public void Converting_to_a_bom_encoding_adds_only_the_bom()
    {
        using var dir = new JournalTempDir();
        string a = Make(dir.Path, "a.txt", Utf8("řeč\n"));
        var plan = EncodingBatch.Plan([a], To(TextCodec.Utf8Bom));
        Assert.Equal(3, plan.Items[0].NewSize - plan.Items[0].Size);
        Assert.Equal(1, EncodingBatch.Run(plan, null, null).Converted);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Utf8("řeč\n")], File.ReadAllBytes(a));
    }
}
