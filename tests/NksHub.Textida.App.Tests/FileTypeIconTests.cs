using System.Buffers.Binary;
using NksHub.Textida.Install;

namespace NksHub.Textida.App.Tests;

/// <summary>The icons of the document types: the extension to group mapping, the icon files and the Linux package layout. The registry and the macOS plist are checked in ShellIntegrationTests and MacIntegrationTests.</summary>
public class FileTypeIconTests
{
    private static string Assets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "NksHub.Textida.App", "Assets", "FileTypes");
    }

    [Theory]
    [InlineData(".md", "md")]
    [InlineData(".json", "data")] [InlineData(".yaml", "data")] [InlineData(".yml", "data")] [InlineData(".xml", "data")] [InlineData(".csv", "data")]
    [InlineData(".txt", "text")]
    [InlineData(".log", "log")]
    [InlineData(".html", "web")] [InlineData(".css", "web")] [InlineData(".js", "web")] [InlineData(".ts", "web")]
    [InlineData(".php", "code")] [InlineData(".cs", "code")] [InlineData(".py", "code")] [InlineData(".sh", "code")] [InlineData(".bat", "code")]
    [InlineData(".sql", "sql")]
    [InlineData(".ini", "config")] [InlineData(".cfg", "config")]
    public void An_extension_belongs_to_a_fixed_group(string extension, string group) => Assert.Equal(group, FileTypeIcons.GroupOf(extension)?.Id);

    [Fact]
    public void Every_integration_extension_has_exactly_one_group_and_the_groups_name_nothing_else()
    {
        var listed = FileTypeIcons.Groups.SelectMany(g => g.Extensions).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(IntegrationExtensions.All.Order(), listed.Order());
        Assert.Equal(FileTypeIcons.Groups.Count, FileTypeIcons.Groups.Select(g => g.Id).Distinct().Count());
        Assert.Null(FileTypeIcons.GroupOf(".unknown"));
        Assert.Equal("md", FileTypeIcons.GroupOf(".MD")?.Id);
    }

    [Fact]
    public void Every_group_has_an_ico_with_four_sizes_png_files_and_an_svg()
    {
        foreach (var g in FileTypeIcons.Groups)
        {
            var ico = File.ReadAllBytes(Path.Combine(Assets(), g.IcoFile));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
            var sizes = Enumerable.Range(0, count).Select(i => ico[6 + 16 * i] is 0 ? 256 : ico[6 + 16 * i]).ToList();
            Assert.Equal([16, 32, 48, 256], sizes);

            foreach (var n in FileTypeIcons.LinuxSizes)
            {
                var png = File.ReadAllBytes(Path.Combine(Assets(), $"{g.Id}-{n}.png"));
                Assert.Equal(n, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
                Assert.Equal(n, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
            }
            Assert.Contains(">" + g.Label + "</text>", File.ReadAllText(Path.Combine(Assets(), g.Id + ".svg")));
        }
    }

    [Fact]
    public void The_linux_package_installs_each_icon_under_hicolor_and_the_mime_file_names_it()
    {
        var files = FileTypeIcons.LinuxIconFiles(Assets());

        Assert.Equal(FileTypeIcons.Groups.Count * (FileTypeIcons.LinuxSizes.Count + 1), files.Count);
        foreach (var g in FileTypeIcons.Groups)
        {
            Assert.Contains($"icons/hicolor/48x48/mimetypes/textida-{g.Id}.png", files.Keys);
            Assert.Contains($"icons/hicolor/256x256/mimetypes/textida-{g.Id}.png", files.Keys);
            Assert.Contains($"icons/hicolor/scalable/mimetypes/textida-{g.Id}.svg", files.Keys);
        }
        Assert.All(files.Values, source => Assert.True(File.Exists(source), source));

        var xml = System.Xml.Linq.XDocument.Parse(FileTypeIcons.LinuxMimeXml());
        var icons = xml.Root!.Elements().ToDictionary(e => e.Attribute("type")!.Value, e => e.Elements().Single().Attribute("name")!.Value);
        Assert.Equal("textida-md", icons["text/markdown"]);
        Assert.Equal("textida-data", icons["application/json"]);
        Assert.Equal(FileTypeIcons.Groups.SelectMany(g => g.MimeTypes).Count(), icons.Count);
    }
}
