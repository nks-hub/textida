using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Tests;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
[Trait("Category", "Large")]
public sealed class MinimapIndexingTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void A_two_gibibyte_file_keeps_its_view_and_minimap_still_while_the_index_grows()
    {
        Assert.SkipUnless(HugeText.Enabled, "Set TEXTIDA_HUGE=1 on the test machine for the 2 GiB minimap check.");
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "indexing.txt");
        const long size = 2L << 30;
        HugeText.RequireRoom(path, size + (1L << 30));
        HugeText.WriteFile(path, size);
        Assert.Equal(size, new FileInfo(path).Length);

        using var begin = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        App? app = null;
        try
        {
            // The two gates expose a 128 MiB prefix and a 1 GiB prefix before the full index.
            MappedFileSource.IndexBlockHook = (source, block) =>
            {
                if (source.Path != path || block < 128) return;
                begin.Wait();
                if (block >= 1024) finish.Wait();
            };
            app = TestApp.Prepare(temp);
            app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
            var window = app.CreateWindow(null);
            window.Width = 1200;
            window.Height = 800;
            var tab = window.OpenFile(path, null);
            Assert.True(tab.Doc.IsLarge);
            var view = tab.Paged!;
            var document = ((PagedBackend)tab.Doc.Doc.Backend).Paged;
            var map = tab.Minimap!;
            var host = (IMinimapHost)view;

            void Pump()
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            void WaitForPrefix(long bytes)
            {
                var clock = Stopwatch.StartNew();
                while (document.IndexedBytes < bytes && clock.Elapsed < TimeSpan.FromMinutes(2)) Pump();
                Assert.Equal(bytes, document.IndexedBytes);
                Assert.False(view.Index.Complete);
            }

            WaitForPrefix(128L << 20);
            host.ScrollToLine(1_000_000);
            for (int i = 0; i < 30; i++) Pump();
            Assert.True(map.IsSampled);
            Assert.Equal(1_000_000, view.TopLine);
            var layout = Assert.IsType<MinimapLayout>(map.Layout);
            var visible = host.VisibleBytes;
            Assert.Equal(1_000_000L * HugeText.LineBytes, visible.Start);
            Assert.True(layout.ThumbTop > 5, $"The minimap pointer is at {layout.ThumbTop} px.");
            int builds = map.BuildsStarted;
            Assert.True(builds > 0, "The minimap did not start drawing its picture.");

            void CheckPosition()
            {
                Assert.Equal(1_000_000, view.TopLine);
                Assert.Equal(visible, host.VisibleBytes);
                Assert.Equal(layout, map.Layout);
                Assert.Equal(builds, map.BuildsStarted);
            }

            int observations = 0;
            begin.Set();
            var indexClock = Stopwatch.StartNew();
            while (document.IndexedBytes < 1L << 30 && indexClock.Elapsed < TimeSpan.FromMinutes(2))
            {
                Pump();
                CheckPosition();
                observations++;
            }
            WaitForPrefix(1L << 30);
            CheckPosition();
            Assert.True(observations > 0, "No view was checked while the prefix grew to 1 GiB.");

            int atHalf = observations;
            finish.Set();
            indexClock.Restart();
            while (!view.Index.Complete && indexClock.Elapsed < TimeSpan.FromMinutes(2))
            {
                Pump();
                CheckPosition();
                observations++;
            }
            Assert.True(view.Index.Complete, $"Only {document.IndexedBytes} of {size} bytes were indexed.");
            Assert.Equal(size, document.IndexedBytes);
            Assert.Equal(size / HugeText.LineBytes + 1, view.Index.Lines);
            Assert.True(observations > atHalf, "No view was checked while the second GiB was indexed.");
            for (int i = 0; i < 30; i++)
            {
                Pump();
                CheckPosition();
            }
            output.WriteLine($"2 GiB indexed; {observations} intermediate views; line {view.TopLine}; byte {visible.Start}; pointer {layout.ThumbTop:F3} px.");
        }
        finally
        {
            begin.Set();
            finish.Set();
            MappedFileSource.IndexBlockHook = null;
            if (app is not null) TestApp.Cleanup(app);
            Loc.SetLanguage(Loc.DefaultLanguage);
        }
    }
}
