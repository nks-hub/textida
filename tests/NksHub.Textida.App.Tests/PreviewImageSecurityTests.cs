using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class PreviewImageSecurityTests
{
    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static byte[] InvalidBitmap()
    {
        var bytes = new byte[54];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        bytes[14] = 40;
        bytes[26] = 1;
        bytes[28] = 24;
        BitConverter.GetBytes(1).CopyTo(bytes, 18);
        BitConverter.GetBytes(int.MinValue).CopyTo(bytes, 22);
        return bytes;
    }

    [Fact]
    public void A_bitmap_with_the_minimum_signed_height_is_not_drawable()
    {
        Assert.False(PreviewPolicy.Drawable(InvalidBitmap()));
    }

    [AvaloniaFact]
    public void A_bitmap_with_the_minimum_signed_height_is_not_decoded()
    {
        Assert.Null(PreviewImages.Decode(InvalidBitmap()));
    }

    [AvaloniaFact]
    public void A_valid_narrow_picture_is_decoded_with_a_bounded_height()
    {
        byte[] png = Pictures.Png(1, 5000);
        Assert.Equal((1, 5000), RasterImage.Size(png));
        using var bitmap = PreviewImages.Decode(png);

        Assert.NotNull(bitmap);
        Assert.Equal(1, bitmap.PixelSize.Width);
        Assert.InRange(bitmap.PixelSize.Height, 1, 4096);
    }

    [AvaloniaFact]
    public void Clicking_image_consent_inside_a_link_does_not_open_the_enclosing_link()
    {
        var asked = new List<string>();
        var opened = new List<Uri>();
        int requests = 0;
        var images = new PreviewImages(() => { requests++; return new HttpClientHandler(); }, host =>
        {
            asked.Add(host);
            return Task.FromResult(false);
        }, () => { });
        var body = PreviewRenderer.Render(PreviewFormats.Markdown,
            "[![chart](https://image.example/chart.png)](https://docs.example/)",
            new PreviewRenderer.Ctx(new Avalonia.Media.FontFamily("Consolas"), Open: opened.Add, Images: images));
        var window = new Window { Width = 600, Height = 300, Content = body };
        try
        {
            window.Show();
            window.UpdateLayout();
            var buttons = Tree(body).OfType<Button>().ToArray();
            var consent = Assert.Single(buttons, button => AutomationProperties.GetName(button) == Loc.Get(K.PreviewImageRemote, ("host", "image.example"), ("alt", "chart")));
            var outer = Assert.Single(buttons, button => !ReferenceEquals(button, consent));
            Assert.Contains(outer, consent.GetLogicalAncestors());
            var click = new RoutedEventArgs(Button.ClickEvent);
            consent.RaiseEvent(click);
            Assert.True(click.Handled);
            Assert.Equal(["image.example"], asked);
            Assert.Empty(opened);
            Assert.Equal(0, requests);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_local_picture_cannot_leave_the_document_folder_through_a_directory_link()
    {
        using var temp = new TempDir();
        string docs = Path.Combine(temp.Path, "docs");
        string outside = Path.Combine(temp.Path, "outside");
        string link = Path.Combine(docs, "linked");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(outside);
        byte[] picture = Convert.FromBase64String(PreviewSafetyTests.OnePixelPng);
        File.WriteAllBytes(Path.Combine(outside, "picture.png"), picture);
        File.WriteAllBytes(Path.Combine(docs, "inside.png"), picture);
        string ownedRoot = Path.GetFullPath(temp.Path) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(link));
        Assert.StartsWith(ownedRoot, Path.GetFullPath(outside));

        Control? body = null;
        try
        {
            CreateDirectoryLink(link, outside);
            Assert.Equal(File.ReadAllBytes(Path.Combine(outside, "picture.png")), File.ReadAllBytes(Path.Combine(link, "picture.png")));
            var images = new PreviewImages(() => new HttpClientHandler(), _ => Task.FromResult(false), () => { });
            body = PreviewRenderer.Render(PreviewFormats.Markdown, "![inside](inside.png) ![outside](linked/picture.png)",
                new PreviewRenderer.Ctx(new Avalonia.Media.FontFamily("Consolas"), Images: images, BaseDirectory: docs));
            var loading = new[] { "inside", "outside" }.Select(alt => Loc.Get(K.PreviewImageLoading, ("alt", alt))).ToHashSet();
            var clock = Stopwatch.StartNew();
            while (!Tree(body).OfType<Image>().Any() || Tree(body).OfType<TextBlock>().Any(text => loading.Contains(text.Text ?? "")))
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Both local image loads must finish.");
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            Assert.Equal("inside", AutomationProperties.GetName(Assert.Single(Tree(body).OfType<Image>())));
            Assert.Contains(Tree(body).OfType<TextBlock>(), text => text.Text == Loc.Get(K.PreviewPanelImage, ("alt", "outside")));
        }
        finally
        {
            if (body is not null)
                foreach (var bitmap in Tree(body).OfType<Image>().Select(image => image.Source).OfType<Bitmap>().Distinct()) bitmap.Dispose();
            if (Directory.Exists(link))
            {
                Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
                Directory.Delete(link, recursive: false);
            }
        }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && e is UnauthorizedAccessException or IOException)
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command",
                "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''")
                    + "' -Value '" + target.Replace("'", "''") + "' | Out-Null" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Assert.Fail("The fixture junction was not created within 10 seconds.");
            }
            Assert.True(process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
    }
}
