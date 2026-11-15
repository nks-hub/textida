using System.Globalization;
using NksHub.Textida.App.Startup;

namespace NksHub.Textida.App.Tests;

public class MeasureSamplesTests
{
    [Fact]
    public void Csv_preserves_sample_order_missing_frames_and_input_delivery_across_runs()
    {
        using var temp = new TempDir();
        string log = System.IO.Path.Combine(temp.Path, "measure.jsonl");
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
            var first = new MeasureSamples(log);
            first.Append("type", "json", [new(100, 12.5, 33.25), new(200, 4.25, 31.5)]);
            var second = new MeasureSamples(log);
            second.Append("osInput", "json", [new(300, -1, InputPath: "SendInput", Sent: true, Delivered: false)]);
            var lines = File.ReadAllLines(first.Path);
            Assert.Equal(4, lines.Length);
            Assert.Equal(22, lines[0].Split(',').Length);
            var slow = lines[1].Split(',');
            var fast = lines[2].Split(',');
            var missing = lines[3].Split(',');
            Assert.Equal(new[] { "0", "100" }, slow[3..5]);
            Assert.Equal(new[] { "12.5", "33.25" }, slow[9..11]);
            Assert.Equal(new[] { "1", "200" }, fast[3..5]);
            Assert.Equal(new[] { "4.25", "31.5" }, fast[9..11]);
            Assert.Equal(first.RunId, slow[0]);
            Assert.Equal(first.RunId, fast[0]);
            Assert.Equal(second.RunId, missing[0]);
            Assert.NotEqual(first.RunId, second.RunId);
            Assert.Equal(new[] { "SendInput", "true", "false", "" }, missing[6..10]);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Csv_quotes_commas_quotes_and_newlines()
    {
        using var temp = new TempDir();
        var samples = new MeasureSamples(System.IO.Path.Combine(temp.Path, "measure.jsonl"));
        samples.Append("type,\"quoted\"", "json", [new(1, InputPath: "line\nbreak")]);
        Assert.Contains(",\"type,\"\"quoted\"\"\",json,0,", File.ReadAllText(samples.Path));
        Assert.Contains(",\"line\nbreak\",", File.ReadAllText(samples.Path));
    }

    [Fact]
    public void Csv_distinguishes_a_drawn_frame_from_an_observed_desktop_frame()
    {
        using var temp = new TempDir();
        var samples = new MeasureSamples(System.IO.Path.Combine(temp.Path, "measure.jsonl"));
        long observed = 100 + System.Diagnostics.Stopwatch.Frequency;
        samples.Append("type", "json", [new(100, 12.5, Desktop: new(0x1753A9, observed, 2.5, 1.25, 4, "seen")),
            new(0, 14.5, Desktop: new(0x1753AA, 0, 1.5, 2.25, 5, "timeout")),
            new(0, 13.5, Desktop: new(0x1753AB, 0, 0, 0, 0, "external"))]);
        var rows = File.ReadAllLines(samples.Path).Skip(1).Select(l => l.Split(',')).ToArray();
        Assert.Equal(new[] { "1753A9", observed.ToString(CultureInfo.InvariantCulture), "1000", "2.5", "1.25", "4", "seen" }, rows[0][15..]);
        Assert.Equal("14.5", rows[1][9]);
        Assert.Equal(new[] { "1753AA", "", "", "1.5", "2.25", "5", "timeout" }, rows[1][15..]);
        Assert.Equal(new[] { "1753AB", "", "", "", "", "", "external" }, rows[2][15..]);
    }

    [Fact]
    public void A_different_csv_schema_is_rejected_without_changing_the_existing_file()
    {
        using var temp = new TempDir();
        var samples = new MeasureSamples(System.IO.Path.Combine(temp.Path, "measure.jsonl"));
        File.WriteAllText(samples.Path, "old,header\n");
        Assert.Throws<InvalidDataException>(() => samples.Append("type", "json", [new(1)]));
        Assert.Equal("old,header\n", File.ReadAllText(samples.Path));
    }
}
